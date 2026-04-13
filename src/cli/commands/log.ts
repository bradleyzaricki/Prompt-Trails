import { findProjectForCwd, listPromptEntriesForProject } from '../../db/queries.js'
import { formatPromptEntry } from '../utils/display.js'
import os from 'os'

interface LogOptions {
  accepted?: boolean
  projectId?: string
}

export function runLog(options: LogOptions): void {
  const cwd = process.cwd()

  let projectId: number | null = null

  if (options.projectId) {
    projectId = parseInt(options.projectId, 10)
    if (isNaN(projectId)) {
      console.error(`Error: invalid project id: ${options.projectId}`)
      process.exit(1)
    }
  } else {
    const project = findProjectForCwd(cwd)
    if (!project) {
      console.error('No tracked project found for current directory.')
      console.error('Run: prompt-trail projects add .')
      process.exit(1)
    }
    projectId = project.id
  }

  const entries = listPromptEntriesForProject(projectId, options.accepted)

  if (entries.length === 0) {
    console.log('No prompt entries found.')
    return
  }

  for (const entry of entries) {
    console.log(formatPromptEntry(entry))
    console.log()
  }
}