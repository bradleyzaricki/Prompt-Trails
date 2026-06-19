import path from 'path'

// ─── Extension → Language mapping ─────────────────────────────────────────

const EXT_TO_LANGUAGE: Record<string, string> = {
  '.ts': 'typescript',
  '.tsx': 'typescript',
  '.js': 'javascript',
  '.jsx': 'javascript',
  '.py': 'python',
  '.cs': 'csharp',
  '.java': 'java',
  '.go': 'go',
  '.rs': 'rust',
  '.rb': 'ruby',
  '.php': 'php',
  '.swift': 'swift',
  '.kt': 'kotlin',
  '.cpp': 'cpp',
  '.c': 'c',
  '.h': 'c',
  '.hpp': 'cpp',
  '.css': 'css',
  '.scss': 'scss',
  '.html': 'html',
  '.json': 'json',
  '.yaml': 'yaml',
  '.yml': 'yaml',
  '.md': 'markdown',
  '.sql': 'sql',
  '.sh': 'shell',
  '.bash': 'shell',
  '.zsh': 'shell',
  '.xml': 'xml',
  '.toml': 'toml',
  '.vue': 'vue',
  '.svelte': 'svelte',
}

// ─── Tool-based prompt category detection ─────────────────────────────────

const WRITE_TOOLS = new Set(['Write', 'Edit', 'MultiEdit', 'NotebookEdit'])
const READ_TOOLS = new Set(['Read', 'Grep', 'Glob'])
const EXEC_TOOLS = new Set(['Bash'])

export function categorizeByToolUsage(
  toolCalls: Array<{ tool_name: string }>
): string {
  if (toolCalls.length === 0) return 'question'

  const hasWrite = toolCalls.some(t => WRITE_TOOLS.has(t.tool_name))
  const hasExec = toolCalls.some(t => EXEC_TOOLS.has(t.tool_name))
  const hasReadOnly = toolCalls.every(t => READ_TOOLS.has(t.tool_name))

  if (hasWrite) return 'code_change'
  if (hasReadOnly) return 'question'
  if (hasExec) return 'command'
  return 'other'
}

// ─── File extension + language extraction ─────────────────────────────────

export function extractFileExtensions(
  toolCalls: Array<{ tool_input: Record<string, unknown> }>
): string[] {
  const extensions = new Set<string>()

  for (const call of toolCalls) {
    const filePath = call.tool_input.file_path as string | undefined
      ?? call.tool_input.path as string | undefined

    if (filePath) {
      const ext = path.extname(filePath).toLowerCase()
      if (ext) extensions.add(ext)
    }
  }

  return [...extensions].sort()
}

export function detectLanguages(extensions: string[]): string[] {
  const languages = new Set<string>()

  for (const ext of extensions) {
    const lang = EXT_TO_LANGUAGE[ext]
    if (lang) languages.add(lang)
  }

  return [...languages].sort()
}
