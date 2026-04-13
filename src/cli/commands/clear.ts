import readline from 'readline'
import { findProjectForCwd, deletePromptEntriesForProject } from '../../db/queries.js'

export function runClear(): void {
  const cwd = process.cwd()
  const project = findProjectForCwd(cwd)

  if (!project) {
    console.error('No tracked project found for current directory.')
    console.error('Run: prompt-trail projects add .')
    process.exit(1)
  }

  const rl = readline.createInterface({
    input: process.stdin,
    output: process.stdout,
  })

  rl.question(
    `Delete all prompt entries for "${project.name}"? [y/N] `,
    (answer) => {
      rl.close()

      if (answer.toLowerCase() !== 'y') {
        console.log('Cancelled.')
        return
      }

      deletePromptEntriesForProject(project.id)
      console.log(`✓ Cleared all entries for ${project.name}`)
    }
  )
}