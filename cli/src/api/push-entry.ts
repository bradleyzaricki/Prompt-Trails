import { createApiClient, type IngestResponseItem } from './client.js'
import { getConfig } from './config.js'
import {
  getProjectById,
  getClaudeSessionId,
  listResponsesForEntry,
  markEntryPushed,
  setProjectServerId,
} from '../db/queries.js'
import type { PromptEntry } from '../types/index.js'

// ─── Helpers ──────────────────────────────────────────────────────────────
function safeParse(json: string): unknown {
  try { return JSON.parse(json) } catch { return json }
}

function safeParseArray(json: string): string[] {
  try {
    const parsed = JSON.parse(json)
    return Array.isArray(parsed) ? parsed : []
  } catch { return [] }
}

function toIso(value: string): string {
  const d = new Date(value)
  return isNaN(d.getTime()) ? new Date().toISOString() : d.toISOString()
}

// Cache: local project id → server project id (per process run)
const serverProjectIdCache = new Map<number, number>()

async function resolveServerProjectId(
  localProjectId: number,
  api: ReturnType<typeof createApiClient>
): Promise<number> {
  const cached = serverProjectIdCache.get(localProjectId)
  if (cached) return cached

  const project = getProjectById(localProjectId)
  if (!project) throw new Error(`local project ${localProjectId} not found`)

  let serverId = project.server_id
  if (!serverId) {
    const created = await api.createProject(project.name, project.description)
    serverId = created.id
    setProjectServerId(project.id, serverId)
  }

  serverProjectIdCache.set(localProjectId, serverId)
  return serverId
}

// ─── Public API ───────────────────────────────────────────────────────────

/**
 * Push a single finalized prompt entry to the server.
 * Only calls markEntryPushed() on success.
 * Throws if the server is unreachable or returns an error — callers decide
 * whether to swallow or surface the error.
 */
export async function pushEntry(entry: PromptEntry): Promise<{ deduped: boolean }> {
  const config = getConfig()
  if (!config?.token) throw new Error('not logged in')

  const api = createApiClient(config)
  const projectId = await resolveServerProjectId(entry.project_id, api)

  const responses: IngestResponseItem[] = listResponsesForEntry(entry.id).map(r => ({
    toolName: r.tool_name,
    toolInput: safeParse(r.tool_input),
    toolOutput: r.tool_output ? safeParse(r.tool_output) : undefined,
    status: r.status,
    toolUseId: r.tool_use_id,
    resolvedAt: r.resolved_at,
  }))

  const result = await api.ingestPrompt({
    projectId,
    agentSessionId: getClaudeSessionId(entry.id) ?? '',
    promptUuid: entry.prompt_uuid || `local-${entry.id}`,
    promptText: entry.prompt_text,
    assistantResponse: entry.claude_response,
    submittedAt: toIso(entry.submitted_at),
    category: entry.prompt_category,
    diff: entry.diff ?? undefined,
    filesChanged: entry.files_changed,
    linesAdded: entry.lines_added,
    linesRemoved: entry.lines_removed,
    fileExtensions: safeParseArray(entry.file_extensions),
    languages: safeParseArray(entry.languages),
    responses,
  })

  markEntryPushed(entry.id)
  return result
}
