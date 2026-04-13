import type { PromptEntry } from '../../types/index.js'

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
  const toolCalls = JSON.parse(entry.tool_calls) as Array<{ tool_name: string }>
  const hasWriteOps = toolCalls.some(t =>
    t.tool_name === 'Write' || t.tool_name === 'Edit' || t.tool_name === 'MultiEdit'
  )

  if (!hasWriteOps) return `${DIM}question${RESET}`
  if (entry.accepted === 0) return `${YELLOW}not accepted${RESET}`
  return `${GREEN}accepted${RESET}`
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

export function formatPromptEntryDetail(entry: PromptEntry): string {
  const toolCalls = JSON.parse(entry.tool_calls) as Array<{
    tool_name: string
    tool_input: Record<string, unknown>
    timestamp: string
  }>

  const lines: string[] = []

  lines.push(`${BOLD}Prompt #${entry.id}${RESET}`)
  lines.push(`${DIM}${'─'.repeat(60)}${RESET}`)
  lines.push(`${BOLD}Text:${RESET} ${entry.prompt_text}`)
  lines.push(`${BOLD}Submitted:${RESET} ${entry.submitted_at}`)
  lines.push(`${BOLD}Status:${RESET} ${label(entry)}`)
  lines.push(`${BOLD}Changes:${RESET} ${GREEN}+${entry.lines_added}${RESET} ${RED}-${entry.lines_removed}${RESET} across ${entry.files_changed} files`)

  if (toolCalls.length > 0) {
    lines.push(`\n${BOLD}Tool calls:${RESET}`)
    for (const call of toolCalls) {
      lines.push(`  ${CYAN}${call.tool_name}${RESET} ${DIM}${call.timestamp}${RESET}`)
      const inputStr = JSON.stringify(call.tool_input)
      lines.push(`    ${DIM}${inputStr.slice(0, 100)}${inputStr.length > 100 ? '...' : ''}${RESET}`)
    }
  }

  if (entry.diff) {
    lines.push(`\n${BOLD}Diff:${RESET}`)
    lines.push(colorDiff(entry.diff))
  }

  return lines.join('\n')
}