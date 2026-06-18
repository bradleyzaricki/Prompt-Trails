import { findProjectForCwd, searchPromptEntries } from '../../db/queries.js'
import { formatPromptEntry } from '../utils/display.js'

interface SearchOptions {
  projectId?: string
}

export function runSearch(query: string, options: SearchOptions): void {
  let projectId: number | undefined

  if (options.projectId) {
    projectId = parseInt(options.projectId, 10)
  } else {
    const project = findProjectForCwd(process.cwd())
    if (project) projectId = project.id
  }

  const results = searchPromptEntries(query, projectId)

  if (results.length === 0) {
    console.log('No results found.')
    return
  }

  console.log(`Found ${results.length} result(s):\n`)
  for (const entry of results) {
    console.log(formatPromptEntry(entry))
    console.log()
  }
}
