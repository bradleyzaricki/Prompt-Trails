import { getDb } from '../../db/index.js'
import {
  findProjectForCwd,
  getOrCreateSession,
  insertPromptEntry,
  appendToolCall,
  finalizePromptEntry,
  getPromptEntry,
  insertPromptResponse,
  resolvePromptResponse,
  getLatestPendingResponse,
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
import type {
  UserPromptSubmitPayload,
  PreToolUsePayload,
  PostToolUsePayload,
  StopPayload,
} from '../../types/index.js'

// Strip system/IDE tags that Claude Code injects into prompts
function cleanPromptText(raw: string): string {
  return raw
    .replace(/<ide_opened_file>[\s\S]*?<\/ide_opened_file>\s*/g, '')
    .replace(/<ide_selection>[\s\S]*?<\/ide_selection>\s*/g, '')
    .replace(/<system-reminder>[\s\S]*?<\/system-reminder>\s*/g, '')
    .replace(/<local-command-caveat>[\s\S]*?<\/local-command-caveat>\s*/g, '')
    .replace(/<available-deferred-tools>[\s\S]*?<\/available-deferred-tools>\s*/g, '')
    .replace(/<command-name>[\s\S]*?<\/command-name>\s*/g, '')
    .replace(/<command-message>[\s\S]*?<\/command-message>\s*/g, '')
    .replace(/<command-args>[\s\S]*?<\/command-args>\s*/g, '')
    .replace(/<local-command-stdout>[\s\S]*?<\/local-command-stdout>\s*/g, '')
    .trim()
}

async function finalizeCachedEntry(cacheEntry: { promptEntryId: number; projectId: number; projectPath: string }): Promise<void> {
  // Guard against double finalization
  const entry = getPromptEntry(cacheEntry.promptEntryId)
  if (!entry || entry.finalized === 1) return

  const shadowDir = getShadowGitDir(cacheEntry.projectId)
  const diffStats = await snapshotAfter(shadowDir, cacheEntry.projectPath)

  const toolCalls = JSON.parse(entry.tool_calls)
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

  // Resolve any responses still pending (fallback when PostToolUse doesn't fire)
  resolvePendingResponses(cacheEntry.promptEntryId, diffStats.diff)
}

async function handleUserPromptSubmit(payload: UserPromptSubmitPayload): Promise<void> {
  const cleanPrompt = cleanPromptText(payload.prompt)
  if (!cleanPrompt) return

  const project = findProjectForCwd(payload.cwd)
  if (!project) return

  // Finalize previous prompt entry if one exists for this session
  const previousEntry = getPromptCacheEntry(payload.session_id)
  if (previousEntry) {
    await finalizeCachedEntry(previousEntry)
  }

  const session = getOrCreateSession(project.id, payload.session_id)
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

async function handlePreToolUse(payload: PreToolUsePayload): Promise<void> {
  const cacheEntry = getPromptCacheEntry(payload.session_id)
  if (!cacheEntry) return

  // Create a response row (pending until PostToolUse or finalization)
  insertPromptResponse(
    cacheEntry.promptEntryId,
    payload.tool_name,
    payload.tool_input
  )

  appendToolCall(cacheEntry.promptEntryId, {
    tool_name: payload.tool_name,
    tool_input: payload.tool_input,
    timestamp: new Date().toISOString(),
    phase: 'pre',
  })
}

async function handlePostToolUse(payload: PostToolUsePayload): Promise<void> {
  const cacheEntry = getPromptCacheEntry(payload.session_id)
  if (!cacheEntry) return

  // Resolve the pending response with accept/reject from tool output
  const pendingResponse = getLatestPendingResponse(cacheEntry.promptEntryId)
  if (pendingResponse) {
    const output = payload.tool_output
    const outputStr = typeof output === 'string' ? output : JSON.stringify(output)
    const rejected = outputStr.includes('was rejected')
      || outputStr.includes("doesn't want to proceed")
      || outputStr.includes('was NOT written')

    resolvePromptResponse(
      pendingResponse.id,
      rejected ? 'rejected' : 'accepted',
      payload.tool_output
    )
  }

  appendToolCall(cacheEntry.promptEntryId, {
    tool_name: payload.tool_name,
    tool_input: payload.tool_input,
    tool_output: payload.tool_output,
    timestamp: new Date().toISOString(),
    phase: 'post',
  })
}

async function handleStop(payload: StopPayload): Promise<void> {
  const cacheEntry = getPromptCacheEntry(payload.session_id)
  if (!cacheEntry) return

  await finalizeCachedEntry(cacheEntry)
  clearPromptCacheEntry(payload.session_id)
  closeSession(payload.session_id)
}

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
        case 'PreToolUse':
          await handlePreToolUse(payload as PreToolUsePayload)
          break
        case 'PostToolUse':
          await handlePostToolUse(payload as PostToolUsePayload)
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
