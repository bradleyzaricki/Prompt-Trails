import type { Project, Session, PromptEntry, PromptResponse } from '../types/index.js'
import { getDb } from './index.js'

// ─── Projects ─────────────────────────────────────────────────────────────

export function insertProject(name: string, path: string, description?: string): Project {
  const db = getDb()
  const result = db.prepare(`
    INSERT INTO projects (name, path, description)
    VALUES (?, ?, ?)
  `).run(name, path, description ?? null)
  return getProjectById(result.lastInsertRowid as number)!
}

export function getProjectByPath(path: string): Project | null {
  const db = getDb()
  return db.prepare('SELECT * FROM projects WHERE path = ?').get(path) as Project | null
}

export function getProjectById(id: number): Project | null {
  const db = getDb()
  return db.prepare('SELECT * FROM projects WHERE id = ?').get(id) as Project | null
}

export function listProjects(): Project[] {
  const db = getDb()
  return db.prepare('SELECT * FROM projects ORDER BY created_at DESC').all() as Project[]
}

export function deleteProject(id: number): void {
  const db = getDb()
  db.prepare('DELETE FROM projects WHERE id = ?').run(id)
}

export function findProjectForCwd(cwd: string): Project | null {
  const db = getDb()
  const projects = db.prepare('SELECT * FROM projects').all() as Project[]
  return projects.find(p => cwd.startsWith(p.path)) ?? null
}

export function updateProjectTimestamp(id: number): void {
  const db = getDb()
  db.prepare(`UPDATE projects SET updated_at = datetime('now') WHERE id = ?`).run(id)
}

// ─── Sessions ─────────────────────────────────────────────────────────────

export function getOrCreateSession(projectId: number, claudeSessionId: string): Session {
  const db = getDb()
  const existing = db.prepare(
    'SELECT * FROM sessions WHERE claude_session_id = ?'
  ).get(claudeSessionId) as Session | null

  if (existing) return existing

  const result = db.prepare(`
    INSERT INTO sessions (project_id, claude_session_id)
    VALUES (?, ?)
  `).run(projectId, claudeSessionId)

  return db.prepare('SELECT * FROM sessions WHERE id = ?')
    .get(result.lastInsertRowid) as Session
}

export function getClaudeSessionId(promptEntryId: number): string | null {
  const db = getDb()
  const row = db.prepare(`
    SELECT s.claude_session_id FROM sessions s
    JOIN prompt_entries pe ON pe.session_id = s.id
    WHERE pe.id = ?
  `).get(promptEntryId) as { claude_session_id: string } | undefined
  return row?.claude_session_id ?? null
}

export function closeSession(claudeSessionId: string): void {
  const db = getDb()
  db.prepare(`
    UPDATE sessions SET ended_at = datetime('now')
    WHERE claude_session_id = ?
  `).run(claudeSessionId)
}

// ─── Prompt Entries ───────────────────────────────────────────────────────

export function insertPromptEntry(
  sessionId: number,
  projectId: number,
  promptText: string,
  submittedAt: string,
  claudeResponse: string = '',
  promptUuid: string = ''
): PromptEntry {
  const db = getDb()
  const result = db.prepare(`
    INSERT INTO prompt_entries (session_id, project_id, prompt_text, submitted_at, claude_response, prompt_uuid)
    VALUES (?, ?, ?, ?, ?, ?)
  `).run(sessionId, projectId, promptText, submittedAt, claudeResponse, promptUuid)

  return db.prepare('SELECT * FROM prompt_entries WHERE id = ?')
    .get(result.lastInsertRowid) as PromptEntry
}

export function getPromptEntry(id: number): PromptEntry | null {
  const db = getDb()
  return db.prepare('SELECT * FROM prompt_entries WHERE id = ?').get(id) as PromptEntry | null
}

export function updateClaudeResponse(promptEntryId: number, claudeResponse: string): void {
  const db = getDb()
  db.prepare('UPDATE prompt_entries SET claude_response = ? WHERE id = ?')
    .run(claudeResponse, promptEntryId)
}

export function finalizePromptEntry(
  promptEntryId: number,
  diff: string,
  filesChanged: number,
  linesAdded: number,
  linesRemoved: number,
  fileExtensions: string[],
  languages: string[],
  promptCategory: string
): void {
  const db = getDb()
  const accepted = filesChanged > 0 ? 1 : 0
  const acceptedAt = accepted ? new Date().toISOString() : null

  db.prepare(`
    UPDATE prompt_entries
    SET diff = ?, files_changed = ?, lines_added = ?, lines_removed = ?,
        accepted = ?, accepted_at = ?,
        file_extensions = ?, languages = ?, prompt_category = ?,
        finalized = 1
    WHERE id = ?
  `).run(
    diff, filesChanged, linesAdded, linesRemoved,
    accepted, acceptedAt,
    JSON.stringify(fileExtensions), JSON.stringify(languages), promptCategory,
    promptEntryId
  )
}

export function listPromptEntriesForProject(projectId: number, acceptedOnly = false): PromptEntry[] {
  const db = getDb()
  const sql = acceptedOnly
    ? 'SELECT * FROM prompt_entries WHERE project_id = ? AND accepted = 1 ORDER BY submitted_at DESC'
    : 'SELECT * FROM prompt_entries WHERE project_id = ? ORDER BY submitted_at DESC'
  return db.prepare(sql).all(projectId) as PromptEntry[]
}

export function deletePromptEntriesForProject(projectId: number): void {
  const db = getDb()
  db.prepare('DELETE FROM prompt_entries WHERE project_id = ?').run(projectId)
}

export function searchPromptEntries(query: string, projectId?: number): PromptEntry[] {
  const db = getDb()
  if (projectId) {
    return db.prepare(`
      SELECT pe.* FROM prompt_entries pe
      JOIN prompt_entries_fts fts ON pe.id = fts.rowid
      WHERE prompt_entries_fts MATCH ?
        AND pe.project_id = ?
      ORDER BY rank
      LIMIT 20
    `).all(query, projectId) as PromptEntry[]
  }
  return db.prepare(`
    SELECT pe.* FROM prompt_entries pe
    JOIN prompt_entries_fts fts ON pe.id = fts.rowid
    WHERE prompt_entries_fts MATCH ?
    ORDER BY rank
    LIMIT 20
  `).all(query) as PromptEntry[]
}

// ─── Prompt Responses ─────────────────────────────────────────────────────

export function insertPromptResponse(
  promptEntryId: number,
  toolName: string,
  toolInput: Record<string, unknown>,
  toolUseId: string = '',
  status: 'pending' | 'accepted' | 'rejected' = 'pending',
  toolOutput?: string
): PromptResponse {
  const db = getDb()
  const resolvedAt = status !== 'pending' ? new Date().toISOString() : null

  const result = db.prepare(`
    INSERT INTO prompt_responses (prompt_entry_id, tool_name, tool_input, tool_use_id, status, tool_output, resolved_at)
    VALUES (?, ?, ?, ?, ?, ?, ?)
  `).run(promptEntryId, toolName, JSON.stringify(toolInput), toolUseId, status, toolOutput ?? null, resolvedAt)

  return db.prepare('SELECT * FROM prompt_responses WHERE id = ?')
    .get(result.lastInsertRowid) as PromptResponse
}

export function listResponsesForEntry(promptEntryId: number): PromptResponse[] {
  const db = getDb()
  return db.prepare(`
    SELECT * FROM prompt_responses
    WHERE prompt_entry_id = ?
    ORDER BY created_at ASC
  `).all(promptEntryId) as PromptResponse[]
}

export function resolvePendingResponses(promptEntryId: number, diff: string): void {
  const db = getDb()

  const pending = db.prepare(`
    SELECT * FROM prompt_responses
    WHERE prompt_entry_id = ? AND status = 'pending'
  `).all(promptEntryId) as PromptResponse[]

  const WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit'])

  for (const resp of pending) {
    if (!WRITE_TOOLS.has(resp.tool_name)) {
      db.prepare(`
        UPDATE prompt_responses SET status = 'accepted', resolved_at = datetime('now')
        WHERE id = ?
      `).run(resp.id)
      continue
    }

    const input = JSON.parse(resp.tool_input)
    const newString = input.new_string as string | undefined
    const filePath = input.file_path as string | undefined

    let wasAccepted = false
    if (newString && diff) {
      const newLines = newString.split('\n').filter(l => l.trim().length > 0)
      wasAccepted = newLines.some(line => diff.includes('+' + line) || diff.includes(line))
    } else if (!newString && filePath && diff) {
      wasAccepted = diff.includes(filePath)
    }

    db.prepare(`
      UPDATE prompt_responses SET status = ?, resolved_at = datetime('now')
      WHERE id = ?
    `).run(wasAccepted ? 'accepted' : 'rejected', resp.id)
  }
}

// ─── Stats ────────────────────────────────────────────────────────────────

export function getStatsForProject(projectId: number) {
  const db = getDb()
  return db.prepare(`
    SELECT
      COUNT(*) as total_prompts,
      SUM(accepted) as accepted_count,
      SUM(files_changed) as total_files_changed,
      SUM(lines_added) as total_lines_added,
      SUM(lines_removed) as total_lines_removed
    FROM prompt_entries
    WHERE project_id = ?
  `).get(projectId)
}
