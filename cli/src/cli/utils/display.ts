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
  if (entry.finalized === 0) return `${YELLOW}pending${RESET}`
  if (entry.accepted === 1) return `${GREEN}accepted${RESET}`
  if (entry.prompt_category === 'question') return `${DIM}question${RESET}`
  return `${RED}not accepted${RESET}`
}

function statusLabel(status: string): string {
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

// ─── List view ─────────────────────────────────────────────────────────────

export function formatPromptEntry(entry: PromptEntry, responseCount = 0): string {
  const lines: string[] = []

  lines.push(
    `${BOLD}[${entry.id}]${RESET} ${entry.prompt_text.slice(0, 80)}${entry.prompt_text.length > 80 ? '...' : ''}`
  )
  lines.push(
    `${DIM}${entry.submitted_at}${RESET}  ${label(entry)}` +
    `${entry.prompt_category !== 'question' ? `  ${CYAN}${entry.prompt_category}${RESET}` : ''}  ` +
    `${GREEN}+${entry.lines_added}${RESET} ${RED}-${entry.lines_removed}${RESET} ` +
    `${DIM}(${entry.files_changed} files, ${responseCount} steps)${RESET}`
  )

  return lines.join('\n')
}

// ─── Detail view ───────────────────────────────────────────────────────────

export function formatPromptEntryDetail(entry: PromptEntry, responses?: PromptResponse[]): string {
  const lines: string[] = []

  lines.push(`${BOLD}Prompt #${entry.id}${RESET}`)
  lines.push(`${DIM}${'─'.repeat(60)}${RESET}`)
  lines.push(`${BOLD}Text:${RESET} ${entry.prompt_text}`)
  lines.push(`${BOLD}Submitted:${RESET} ${entry.submitted_at}`)
  lines.push(`${BOLD}Status:${RESET} ${label(entry)}`)
  lines.push(`${BOLD}Changes:${RESET} ${GREEN}+${entry.lines_added}${RESET} ${RED}-${entry.lines_removed}${RESET} across ${entry.files_changed} files`)

  if (entry.claude_response) {
    const preview = entry.claude_response.length > 200
      ? entry.claude_response.slice(0, 200) + '...'
      : entry.claude_response
    lines.push(`\n${BOLD}Claude's Response:${RESET}`)
    lines.push(`${DIM}${preview}${RESET}`)
  }

  if (responses && responses.length > 0) {
    const accepted = responses.filter(r => r.status === 'accepted').length
    const rejected = responses.filter(r => r.status === 'rejected').length
    const pending = responses.filter(r => r.status === 'pending').length

    const parts: string[] = []
    if (accepted > 0) parts.push(`${GREEN}${accepted} accepted${RESET}`)
    if (rejected > 0) parts.push(`${RED}${rejected} rejected${RESET}`)
    if (pending > 0) parts.push(`${YELLOW}${pending} pending${RESET}`)

    lines.push(`\n${BOLD}Trail:${RESET} ${parts.join(', ')} of ${responses.length} steps`)
    lines.push(`${DIM}${'─'.repeat(60)}${RESET}`)

    for (let i = 0; i < responses.length; i++) {
      const resp = responses[i]
      const input = JSON.parse(resp.tool_input)
      const stepNum = `${DIM}${String(i + 1).padStart(2)}.${RESET}`

      if (resp.tool_name === 'AskUserQuestion') {
        const questions = input.questions as Array<{ question: string }> | undefined
        const questionText = questions?.[0]?.question ?? 'Asked a question'
        const answer = resp.tool_output
          ? String(resp.tool_output).slice(0, 100)
          : '(no answer recorded)'
        lines.push(`${stepNum} ${CYAN}Claude asked:${RESET} ${questionText}`)
        lines.push(`     ${YELLOW}User answered:${RESET} ${answer}`)
      } else {
        const filePath = input.file_path ?? input.path ?? ''
        lines.push(`${stepNum} ${statusLabel(resp.status)}  ${CYAN}${resp.tool_name}${RESET} ${DIM}${filePath}${RESET}`)
      }
    }
  }

  if (entry.diff) {
    lines.push(`\n${BOLD}Diff:${RESET}`)
    lines.push(colorDiff(entry.diff))
  }

  return lines.join('\n')
}
