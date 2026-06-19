import { getDb } from '../../db/index.js'
import {
  findProjectForCwd,
  getOrCreateSession,
  getProjectById,
  getClaudeSessionId,
  insertPromptEntry,
  updateClaudeResponse,
  finalizePromptEntry,
  getPromptEntry,
  insertPromptResponse,
  resolvePendingResponses,
  closeSession,
} from '../../db/queries.js'
import {
  setPromptCacheEntry,
  getPromptCacheEntry,
  clearPromptCacheEntry,
} from '../../hooks/prompt-cache.js'
import { initShadowRepo, snapshotBefore, snapshotAfter } from '../../hooks/shadow-git.js'
import { getShadowGitDir } from '../../db/index.js'
import { extractFileExtensions, detectLanguages, categorizeByToolUsage } from '../utils/metadata.js'
import { findPromptByText } from '../../hooks/conversation-log.js'
import type { UserPromptSubmitPayload, StopPayload } from '../../types/index.js'

// ─── Tag stripping ────────────────────────────────────────────────────────

const SYSTEM_TAG_PATTERNS = [
  /<ide_opened_file>[\s\S]*?<\/ide_opened_file>\s*/g,
  /<ide_selection>[\s\S]*?<\/ide_selection>\s*/g,
  /<system-reminder>[\s\S]*?<\/system-reminder>\s*/g,
  /<local-command-caveat>[\s\S]*?<\/local-command-caveat>\s*/g,
  /<available-deferred-tools>[\s\S]*?<\/available-deferred-tools>\s*/g,
  /<command-name>[\s\S]*?<\/command-name>\s*/g,
  /<command-message>[\s\S]*?<\/command-message>\s*/g,
  /<command-args>[\s\S]*?<\/command-args>\s*/g,
  /<local-command-stdout>[\s\S]*?<\/local-command-stdout>\s*/g,
]

function cleanPromptText(raw: string): string {
  let text = raw
  for (const pattern of SYSTEM_TAG_PATTERNS) {
    text = text.replace(pattern, '')
  }
  return text.trim()
}

// ─── Conversation log backfill ────────────────────────────────────────────

function backfillFromConversationLog(promptEntryId: number, projectId: number): {
  toolCalls: Array<{ tool_name: string; tool_input: Record<string, unknown> }>
} {
  const toolCalls: Array<{ tool_name: string; tool_input: Record<string, unknown> }> = []

  const entry = getPromptEntry(promptEntryId)
  if (!entry) return { toolCalls }

  const project = getProjectById(projectId)
  if (!project) return { toolCalls }

  const claudeSessionId = getClaudeSessionId(promptEntryId)
  if (!claudeSessionId) return { toolCalls }

  const matched = findPromptByText(project.path, claudeSessionId, entry.prompt_text)
  if (!matched) return { toolCalls }

  // Backfill Claude's response text
  const claudeResponse = matched.responses
    .map(r => r.text)
    .filter(t => t.trim())
    .join('\n\n')
  if (claudeResponse) {
    updateClaudeResponse(promptEntryId, claudeResponse)
  }

  // Insert the full breadcrumb trail of tool calls
  for (const turn of matched.responses) {
    for (const tc of turn.toolCalls) {
      toolCalls.push({ tool_name: tc.toolName, tool_input: tc.toolInput })
      insertPromptResponse(
        promptEntryId,
        tc.toolName,
        tc.toolInput,
        tc.toolUseId,
        tc.status,
        tc.toolOutput
      )
    }
  }

  return { toolCalls }
}

// ─── Finalization ─────────────────────────────────────────────────────────

async function finalizeCachedEntry(cacheEntry: {
  promptEntryId: number
  projectId: number
  projectPath: string
}): Promise<void> {
  const entry = getPromptEntry(cacheEntry.promptEntryId)
  if (!entry || entry.finalized === 1) return

  const shadowDir = getShadowGitDir(cacheEntry.projectId)
  const diffStats = await snapshotAfter(shadowDir, cacheEntry.projectPath)

  let toolCalls: Array<{ tool_name: string; tool_input: Record<string, unknown> }> = []
  try {
    const result = backfillFromConversationLog(cacheEntry.promptEntryId, cacheEntry.projectId)
    toolCalls = result.toolCalls
  } catch {
    // Best-effort — conversation log may not be available
  }

  const fileExtensions = extractFileExtensions(toolCalls)
  const languages = detectLanguages(fileExtensions)
  const promptCategory = categorizeByToolUsage(toolCalls)

  finalizePromptEntry(
    cacheEntry.promptEntryId,
    diffStats.diff,
    diffStats.files_changed,
    diffStats.lines_added,
    diffStats.lines_removed,
    fileExtensions,
    languages,
    promptCategory
  )

  resolvePendingResponses(cacheEntry.promptEntryId, diffStats.diff)
}

// ─── Hook handlers ────────────────────────────────────────────────────────

async function handleUserPromptSubmit(payload: UserPromptSubmitPayload): Promise<void> {
  const project = findProjectForCwd(payload.cwd)
  if (!project) return

  // Finalize previous prompt before starting the new one
  const previousEntry = getPromptCacheEntry(payload.session_id)
  if (previousEntry) {
    await finalizeCachedEntry(previousEntry)
  }

  const session = getOrCreateSession(project.id, payload.session_id)

  const cleanPrompt = cleanPromptText(payload.prompt)
  if (!cleanPrompt) return

  const entry = insertPromptEntry(
    session.id,
    project.id,
    cleanPrompt,
    new Date().toISOString()
  )

  setPromptCacheEntry(payload.session_id, {
    promptEntryId: entry.id,
    projectId: project.id,
    projectPath: project.path,
  })

  const shadowDir = getShadowGitDir(project.id)
  await initShadowRepo(shadowDir, project.path)
  await snapshotBefore(shadowDir, project.path)
}

async function handleStop(payload: StopPayload): Promise<void> {
  const cacheEntry = getPromptCacheEntry(payload.session_id)
  if (!cacheEntry) return

  await finalizeCachedEntry(cacheEntry)
  clearPromptCacheEntry(payload.session_id)
  closeSession(payload.session_id)
}

// ─── Entry point ──────────────────────────────────────────────────────────

export async function runRecord(): Promise<void> {
  getDb()

  let raw = ''

  process.stdin.setEncoding('utf-8')
  process.stdin.on('data', chunk => { raw += chunk })
  process.stdin.on('end', async () => {
    try {
      const payload = JSON.parse(raw)

      switch (payload.hook_event_name) {
        case 'UserPromptSubmit':
          await handleUserPromptSubmit(payload as UserPromptSubmitPayload)
          break
        case 'Stop':
          await handleStop(payload as StopPayload)
          break
      }
    } catch (err) {
      process.stderr.write(`prompt-trail error: ${err}\n`)
    }

    process.exit(0)
  })
}
