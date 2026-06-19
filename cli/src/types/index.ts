export interface Project {
  id: number
  name: string
  path: string
  description?: string
  created_at: string
  updated_at: string
}

export interface Session {
  id: number
  project_id: number
  started_at: string
  ended_at?: string
  claude_session_id: string
}

export interface PromptEntry {
  id: number
  session_id: number
  project_id: number
  prompt_text: string
  claude_response: string       // Claude's text response
  submitted_at: string
  finalized: number
  accepted: number
  accepted_at?: string
  diff?: string
  files_changed: number
  lines_added: number
  lines_removed: number
  file_extensions: string       // JSON array of extensions touched
  languages: string             // JSON array of detected languages
  prompt_category: string       // "question" | "code_change" | "command" | "other"
  prompt_uuid: string           // UUID from conversation log for dedup
}

export interface PromptResponse {
  id: number
  prompt_entry_id: number
  tool_name: string
  tool_input: string            // JSON string
  tool_output?: string          // JSON string
  status: 'pending' | 'accepted' | 'rejected'
  tool_use_id: string           // Links to conversation log tool_use_id
  created_at: string
  resolved_at?: string
}

// Hook payloads

export interface UserPromptSubmitPayload {
  hook_event_name: 'UserPromptSubmit'
  session_id: string
  cwd: string
  prompt: string
}

export interface StopPayload {
  hook_event_name: 'Stop'
  session_id: string
  cwd: string
}

export interface DiffStats {
  diff: string
  files_changed: number
  lines_added: number
  lines_removed: number
}

export interface SessionCacheEntry {
  promptEntryId: number
  projectId: number
  projectPath: string
}

export interface SessionCache {
  [claude_session_id: string]: SessionCacheEntry
}
