import { findProjectForCwd, getStatsForProject } from '../../db/queries.js'

//log visual statistics about the current project to the terminal
export function runStats(): void {
  const cwd = process.cwd()
  const project = findProjectForCwd(cwd)

  if (!project) {
    console.error('No tracked project found for current directory.')
    console.error('Run: prompt-trail projects add .')
    process.exit(1)
  }

  const stats = getStatsForProject(project.id) as {
    total_prompts: number
    accepted_count: number
    total_files_changed: number
    total_lines_added: number
    total_lines_removed: number
  }

  if (!stats || stats.total_prompts === 0) {
    console.log('No prompt entries recorded yet for this project.')
    return
  }

  const acceptanceRate = stats.total_prompts > 0
    ? ((stats.accepted_count / stats.total_prompts) * 100).toFixed(1)
    : '0.0'

  console.log(`\nStats for: ${project.name}`)
  console.log(`${'─'.repeat(40)}`)
  console.log(`Total prompts:      ${stats.total_prompts}`)
  console.log(`Accepted:           ${stats.accepted_count} (${acceptanceRate}%)`)
  console.log(`Files changed:      ${stats.total_files_changed}`)
  console.log(`Lines added:        ${stats.total_lines_added}`)
  console.log(`Lines removed:      ${stats.total_lines_removed}`)
}