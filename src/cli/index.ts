#!/usr/bin/env node
// comment 1
// comment 2
// comment 3
// comment 4
import { Command } from 'commander'
import { runInit } from './commands/init.js'
import { runRecord } from './commands/record.js'
import { runProjectsList, runProjectsAdd, runProjectsRemove } from './commands/projects.js'
import { runLog } from './commands/log.js'
import { runShow } from './commands/show.js'
import { runStats } from './commands/stats.js'
import { runClear } from './commands/clear.js'
import { runSearch } from './commands/search.js'

const program = new Command()

program
  .name('prompt-trail')
  .description('Track and review your Claude Code prompt history')
  .version('1.0.0')

program
  .command('init')
  .description('Install hooks and initialize the database')
  .action(runInit)

program
  .command('record')
  .description('Internal: called by the hook script')
  .action(runRecord)

program
  .command('projects')
  .description('Manage tracked projects')
  .addCommand(
    new Command('list')
      .description('List all tracked projects')
      .action(runProjectsList)
  )
  .addCommand(
    new Command('add')
      .description('Start tracking a project')
      .argument('<path>', 'path to the project directory')
      .action(runProjectsAdd)
  )
  .addCommand(
    new Command('remove')
      .description('Stop tracking a project')
      .argument('<id>', 'project id')
      .action(runProjectsRemove)
  )

program
  .command('log')
  .description('Show prompt history for the current project')
  .option('--accepted', 'only show prompts that produced file changes')
  .option('--project-id <id>', 'show history for a specific project id')
  .action((options) => runLog(options))

program
  .command('show')
  .description('Show a single prompt entry with full diff')
  .argument('<id>', 'prompt entry id')
  .action(runShow)

program
  .command('stats')
  .description('Show summary statistics for the current project')
  .action(runStats)

program
  .command('search')
  .description('Full-text search across prompts and diffs')
  .argument('<query>', 'search query')
  .option('--project-id <id>', 'search within a specific project')
  .action((query, options) => runSearch(query, options))

program
  .command('clear')
  .description('Delete all entries for the current project')
  .action(runClear)

program.parse()