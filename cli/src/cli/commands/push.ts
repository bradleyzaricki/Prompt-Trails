import { getConfig } from '../../api/config.js'
import { createApiClient, type IngestResponseItem } from '../../api/client.js'
import {
  getUnpushedEntries,
  markEntryPushed,
  getProjectById,
  setProjectServerId,
  getClaudeSessionId,
  listResponsesForEntry,
} from '../../db/queries.js'

export async function runPush(): Promise<void> {
  const config = getConfig()
  if (!config?.token) {
    console.error('Not logged in. Run: prompt-trail login')
    process.exit(1)
  }
  const api = createApiClient(config)

  const entries = getUnpushedEntries()
  if (entries.length === 0) {
    console.log('Nothing to push — all finalized prompts are synced.')
    return
  }

  console.log(`Pushing ${entries.length} prompt(s) to ${config.apiUrl} ...`)

  // local project id -> server project id (resolved once per run)
  const serverProjectId = new Map<number, number>()
  let pushed = 0
  let deduped = 0
  let failed = 0

  for (const entry of entries) {
    try {
      const projectId = await resolveServerProjectId(entry.project_id, serverProjectId, api)

      const responses: IngestResponseItem[] = listResponsesForEntry(entry.id).map((r) => ({
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
      if (result.deduped) deduped++
      else pushed++
    } catch (err) {
      failed++
      console.error(`  ✗ prompt ${entry.id}: ${(err as Error).message}`)
    }
  }

  console.log(`✓ Pushed ${pushed}, already-synced ${deduped}, failed ${failed}`)
}

async function resolveServerProjectId(
  localProjectId: number,
  cache: Map<number, number>,
  api: ReturnType<typeof createApiClient>
): Promise<number> {
  const cached = cache.get(localProjectId)
  if (cached) return cached

  const project = getProjectById(localProjectId)
  if (!project) throw new Error(`local project ${localProjectId} not found`)

  let serverId = project.server_id
  if (!serverId) {
    // First push for this project — create it on the server (no path is ever sent).
    const created = await api.createProject(project.name, project.description)
    serverId = created.id
    setProjectServerId(project.id, serverId)
  }

  cache.set(localProjectId, serverId)
  return serverId
}

function safeParse(json: string): unknown {
  try {
    return JSON.parse(json)
  } catch {
    return json
  }
}

function safeParseArray(json: string): string[] {
  try {
    const parsed = JSON.parse(json)
    return Array.isArray(parsed) ? parsed : []
  } catch {
    return []
  }
}

// The server binds DateTimeOffset; normalize SQLite's "YYYY-MM-DD HH:MM:SS" (and anything
// already ISO) to a proper ISO-8601 string. Fall back to now if unparseable.
function toIso(value: string): string {
  const d = new Date(value)
  return isNaN(d.getTime()) ? new Date().toISOString() : d.toISOString()
}
