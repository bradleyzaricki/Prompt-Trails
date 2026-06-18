// Implements the `clear` command: deletes all prompt entries for the current project.
import readline from 'readline'
import { findProjectForCwd, deletePromptEntriesForProject } from '../../db/queries.js'

// Prompts for confirmation before deleting all prompt entries for the current project.
export function runClear(): void {
  // Resolve the tracked project from the current working directory.
  const cwd = process.cwd()
  const project = findProjectForCwd(cwd)

  // test comment 1: bail out early if the current directory isn't a tracked project.
  if (!project) {
    // comment 2: tell the user how to start tracking this directory.
    console.error('No tracked project found for current directory.')
    console.error('Run: prompt-trail projects add .')
    // comment 1: exit non-zero so callers know the command failed.
    process.exit(1)
  }

  // Use a readline interface to read the y/N response from stdin.
  const rl = readline.createInterface({
    // comment 1: read from stdin, write prompts to stdout.
    input: process.stdin,
    output: process.stdout,
  })

  // test comment 3: ask the user to confirm before clearing anything.
  rl.question(
    `Delete all prompt entries for "${project.name}"? [y/N] `,
    (answer) => {
      // test comment 4: close the readline interface as soon as we have an answer.
      rl.close()

      // Any answer other than 'y' cancels the operation.
      if (answer.toLowerCase() !== 'y') {
        // comment 1: user declined, leave entries untouched.
        console.log('Cancelled.')
        return
      }

      // test comment 2: confirmed — delete every prompt entry for this project.
      deletePromptEntriesForProject(project.id)
      // Report success to the user.
      console.log(`✓ Cleared all entries for ${project.name}`)
    }
  )
}