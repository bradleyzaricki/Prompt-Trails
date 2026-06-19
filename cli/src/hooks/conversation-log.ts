import fs from 'fs'
import path from 'path'
import os from 'os'

// ─── Types for JSONL conversation log entries ─────────────────────────────

interface LogEntry {
  type: string
  sessionId?: string
  uuid?: string
  parentUuid?: string | null
  timestamp?: string
  message?: {
    role: string
    content: string | ContentBlock[]
  }
  toolUseResult?: boolean
  promptSource?: string
  origin?: { kind: string }
}

interface ContentBlock {
  type: string
  text?: string
  id?: string
  name?: string
  input?: Record<string, unknown>
  tool_use_id?: string
  content?: string
  is_error?: boolean
}

// ─── Parsed conversation types ────────────────────────────────────────────

export interface ParsedPrompt {
  uuid: string
  text: string
  timestamp: string
  responses: ParsedAssistantTurn[]
}

export interface ParsedAssistantTurn {
  uuid: string
  timestamp: string
  text: string
  toolCalls: ParsedToolCall[]
}

export interface ParsedToolCall {
  toolUseId: string
  toolName: string
  toolInput: Record<string, unknown>
  status: 'accepted' | 'rejected' | 'pending'
  toolOutput?: string
}

// ─── Log file discovery ───────────────────────────────────────────────────

function getProjectLogDir(projectPath: string): string {
  const encoded = projectPath.replace(/\//g, '-')
  return path.join(os.homedir(), '.claude', 'projects', encoded)
}

function findSessionLogFile(projectPath: string, claudeSessionId: string): string | null {
  const logDir = getProjectLogDir(projectPath)
  if (!fs.existsSync(logDir)) return null

  const logFile = path.join(logDir, `${claudeSessionId}.jsonl`)
  if (fs.existsSync(logFile)) return logFile

  return null
}

// ─── JSONL parser ─────────────────────────────────────────────────────────

function parseLogFile(filePath: string): LogEntry[] {
  const content = fs.readFileSync(filePath, 'utf-8')
  const entries: LogEntry[] = []

  for (const line of content.split('\n')) {
    if (!line.trim()) continue
    try {
      entries.push(JSON.parse(line))
    } catch {
      // Skip malformed lines
    }
  }

  return entries
}

// ─── Extract text from message content ────────────────────────────────────

function extractText(content: string | ContentBlock[]): string {
  if (typeof content === 'string') return content

  return content
    .filter(block => block.type === 'text' && block.text)
    .map(block => block.text!)
    .join('\n')
}

// ─── Extract tool_use calls from assistant message ────────────────────────

function extractToolCalls(content: string | ContentBlock[]): ParsedToolCall[] {
  if (typeof content === 'string') return []

  return content
    .filter(block => block.type === 'tool_use' && block.name && block.id)
    .map(block => ({
      toolUseId: block.id!,
      toolName: block.name!,
      toolInput: block.input ?? {},
      status: 'pending' as const,
    }))
}

// ─── Match tool results to tool calls ─────────────────────────────────────

function resolveToolResults(
  toolCalls: ParsedToolCall[],
  entries: LogEntry[],
  startIndex: number
): void {
  // Look for user messages with toolUseResult that follow
  for (let i = startIndex; i < entries.length; i++) {
    const entry = entries[i]
    if (entry.type !== 'user' || !entry.toolUseResult) continue
    if (entry.type === 'user' && !entry.toolUseResult) break // Next real user prompt

    const content = entry.message?.content
    if (!Array.isArray(content)) continue

    for (const block of content) {
      if (block.type !== 'tool_result') continue

      const toolCall = toolCalls.find(tc => tc.toolUseId === block.tool_use_id)
      if (!toolCall) continue

      toolCall.toolOutput = typeof block.content === 'string' ? block.content : JSON.stringify(block.content)

      if (block.is_error === true) {
        toolCall.status = 'rejected'
      } else {
        toolCall.status = 'accepted'
      }
    }
  }
}

// ─── Main: parse conversation for a session ───────────────────────────────

export function parseSessionConversation(
  projectPath: string,
  claudeSessionId: string
): ParsedPrompt[] | null {
  const logFile = findSessionLogFile(projectPath, claudeSessionId)
  if (!logFile) return null

  const entries = parseLogFile(logFile)
  const prompts: ParsedPrompt[] = []

  for (let i = 0; i < entries.length; i++) {
    const entry = entries[i]

    // Only process real user prompts (not tool results)
    if (entry.type !== 'user') continue
    if (entry.toolUseResult) continue
    if (!entry.message?.content) continue

    const text = extractText(entry.message.content)
    if (!text.trim()) continue

    const prompt: ParsedPrompt = {
      uuid: entry.uuid ?? '',
      text: text.trim(),
      timestamp: entry.timestamp ?? new Date().toISOString(),
      responses: [],
    }

    // Collect all assistant turns that follow this prompt
    for (let j = i + 1; j < entries.length; j++) {
      const next = entries[j]

      // Stop at next real user prompt
      if (next.type === 'user' && !next.toolUseResult && next.message?.content) {
        const nextText = extractText(next.message.content)
        if (nextText.trim()) break
      }

      if (next.type !== 'assistant') continue
      if (!next.message?.content) continue

      const assistantText = extractText(next.message.content)
      const toolCalls = extractToolCalls(next.message.content)

      // Resolve tool results for these calls
      resolveToolResults(toolCalls, entries, j + 1)

      prompt.responses.push({
        uuid: next.uuid ?? '',
        timestamp: next.timestamp ?? new Date().toISOString(),
        text: assistantText,
        toolCalls,
      })
    }

    prompts.push(prompt)
  }

  return prompts
}

// Get only the latest prompt from a session (for incremental processing)
export function getLatestPrompt(
  projectPath: string,
  claudeSessionId: string
): ParsedPrompt | null {
  const prompts = parseSessionConversation(projectPath, claudeSessionId)
  if (!prompts || prompts.length === 0) return null
  return prompts[prompts.length - 1]
}

// Find a specific prompt by matching its text (searches from most recent)
export function findPromptByText(
  projectPath: string,
  claudeSessionId: string,
  promptText: string
): ParsedPrompt | null {
  const prompts = parseSessionConversation(projectPath, claudeSessionId)
  if (!prompts) return null

  // Search from the end since we're usually looking for recent prompts
  for (let i = prompts.length - 1; i >= 0; i--) {
    if (prompts[i].text.includes(promptText.slice(0, 80))) {
      return prompts[i]
    }
  }
  return null
}
