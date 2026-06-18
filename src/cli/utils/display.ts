import type { PromptEntry, PromptResponse } from '../../types/index.js'

// ─── ANSI color codes ──────────────────────────────────────────────────────

const RESET = '\x1b[0m'
const BOLD = '\x1b[1m'
const DIM = '\x1b[2m'
const GREEN = '\x1b[32m'
const RED = '\x1b[31m'
const YELLOW = '\x1b[33m'
const CYAN = '\x1b[36m'

// ─── Helpers ───────────────────────────────────────────────────────────────

function label(entry: PromptEntry): string {
  // Not yet finalized — still in progress
  if (entry.finalized === 0) return `${YELLOW}pending${RESET}`
  // Finalized with no write tools used
  if (entry.prompt_category === 'question') return `${DIM}question${RESET}`
  // Finalized with changes
  if (entry.accepted === 1) return `${GREEN}accepted${RESET}`
  // Finalized with write tools but no changes stuck
  return `${RED}not accepted${RESET}`
}

function responseStatusLabel(status: string): string {
  if (status === 'accepted') return `${GREEN}accepted${RESET}`
  if (status === 'rejected') return `${RED}rejected${RESET}`
  return `${YELLOW}pending${RESET}`
}

function colorDiff(diff: string): string {
  return diff
    .split('\n')
    .map(line => {
      if (line.startsWith('+') && !line.startsWith('+++')) return `${GREEN}${line}${RESET}`
      if (line.startsWith('-') && !line.startsWith('---')) return `${RED}${line}${RESET}`
      if (line.startsWith('@@')) return `${CYAN}${line}${RESET}`
      return line
    })
    .join('\n')
}

// ─── Public formatters ─────────────────────────────────────────────────────

export function formatPromptEntry(entry: PromptEntry): string {
  const toolCalls = JSON.parse(entry.tool_calls) as Array<{ tool_name: string }>
  const lines: string[] = []

  lines.push(
    `${BOLD}[${entry.id}]${RESET} ${entry.prompt_text.slice(0, 80)}${entry.prompt_text.length > 80 ? '...' : ''}`
  )
  lines.push(
    `${DIM}${entry.submitted_at}${RESET}  ${label(entry)}  ` +
    `${GREEN}+${entry.lines_added}${RESET} ${RED}-${entry.lines_removed}${RESET} ` +
    `${DIM}(${entry.files_changed} files, ${toolCalls.length} tool calls)${RESET}`
  )

  return lines.join('\n')
}

export function formatPromptEntryDetail(entry: PromptEntry, responses?: PromptResponse[]): string {
  const lines: string[] = []

  lines.push(`${BOLD}Prompt #${entry.id}${RESET}`)
  lines.push(`${DIM}${'─'.repeat(60)}${RESET}`)
  lines.push(`${BOLD}Text:${RESET} ${entry.prompt_text}`)
  lines.push(`${BOLD}Submitted:${RESET} ${entry.submitted_at}`)
  lines.push(`${BOLD}Status:${RESET} ${label(entry)}`)
  lines.push(`${BOLD}Changes:${RESET} ${GREEN}+${entry.lines_added}${RESET} ${RED}-${entry.lines_removed}${RESET} across ${entry.files_changed} files`)

  if (responses && responses.length > 0) {
    const accepted = responses.filter(r => r.status === 'accepted').length
    const rejected = responses.filter(r => r.status === 'rejected').length
    const pending = responses.filter(r => r.status === 'pending').length

    const parts: string[] = []
    if (accepted > 0) parts.push(`${GREEN}${accepted} accepted${RESET}`)
    if (rejected > 0) parts.push(`${RED}${rejected} rejected${RESET}`)
    if (pending > 0) parts.push(`${YELLOW}${pending} pending${RESET}`)

    lines.push(`\n${BOLD}Responses:${RESET} ${parts.join(', ')} of ${responses.length} total`)
    lines.push(`${DIM}${'─'.repeat(60)}${RESET}`)

    for (const resp of responses) {
      const input = JSON.parse(resp.tool_input)
      const filePath = input.file_path ?? input.path ?? ''
      lines.push(`  ${responseStatusLabel(resp.status)}  ${CYAN}${resp.tool_name}${RESET} ${DIM}${filePath}${RESET}`)
    }
  } else {
    // Fallback to legacy tool_calls if no responses exist
    const toolCalls = JSON.parse(entry.tool_calls) as Array<{
      tool_name: string
      tool_input: Record<string, unknown>
      timestamp: string
    }>

    if (toolCalls.length > 0) {
      lines.push(`\n${BOLD}Tool calls:${RESET}`)
      for (const call of toolCalls) {
        lines.push(`  ${CYAN}${call.tool_name}${RESET} ${DIM}${call.timestamp}${RESET}`)
        const inputStr = JSON.stringify(call.tool_input)
        lines.push(`    ${DIM}${inputStr.slice(0, 100)}${inputStr.length > 100 ? '...' : ''}${RESET}`)
      }
    }
  }

  if (entry.diff) {
    lines.push(`\n${BOLD}Diff:${RESET}`)
    lines.push(colorDiff(entry.diff))
  }

  return lines.join('\n')
}
