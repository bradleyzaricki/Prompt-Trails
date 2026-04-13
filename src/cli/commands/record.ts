import { getDb } from '../../db/index.js'
import {
  findProjectForCwd,
  getOrCreateSession,
  insertPromptEntry,
  appendToolCall,
  finalizePromptEntry,
} from '../../db/queries.js'
import {
  setPromptCacheEntry,
  getPromptCacheEntry,
  clearPromptCacheEntry,
} from '../../hooks/prompt-cache.js'
import { initShadowRepo, snapshotBefore, snapshotAfter } from '../../hooks/shadow-git.js'
import { getShadowGitDir } from '../../db/index.js'
import type {
  UserPromptSubmitPayload,
  PreToolUsePayload,
  StopPayload,
} from '../../types/index.js'

async function handleUserPromptSubmit(payload: UserPromptSubmitPayload): Promise<void> {
  const project = findProjectForCwd(payload.cwd)
  if (!project) return

  const session = getOrCreateSession(project.id, payload.session_id)
  const entry = insertPromptEntry(
    session.id,
    project.id,
    payload.prompt,
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

  appendToolCall(cacheEntry.promptEntryId, {
    tool_name: payload.tool_name,
    tool_input: payload.tool_input,
    timestamp: new Date().toISOString(),
  })
}

async function handleStop(payload: StopPayload): Promise<void> {
  const cacheEntry = getPromptCacheEntry(payload.session_id)
  if (!cacheEntry) return

  const shadowDir = getShadowGitDir(cacheEntry.projectId)
  const diffStats = await snapshotAfter(shadowDir, cacheEntry.projectPath)

  finalizePromptEntry(
    cacheEntry.promptEntryId,
    diffStats.diff,
    diffStats.files_changed,
    diffStats.lines_added,
    diffStats.lines_removed
  )

  clearPromptCacheEntry(payload.session_id)
  
  const { closeSession } = await import('../../db/queries.js')
  closeSession(payload.session_id)
}

export async function runRecord(): Promise<void> {
  // ensure db is initialized
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