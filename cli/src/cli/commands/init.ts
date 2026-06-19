import fs from 'fs'
import path from 'path'
import os from 'os'
import { getDb } from '../../db/index.js'

const HOOK_SCRIPT_NAME = 'prompt-trail.sh'
const CLAUDE_DIR = path.join(os.homedir(), '.claude')
const HOOKS_DIR = path.join(CLAUDE_DIR, 'hooks')
const SETTINGS_PATH = path.join(CLAUDE_DIR, 'settings.json')

export function runInit(): void {
  // 1. initialize the database
  getDb()
  console.log('✓ Database initialized')

  // 2. ensure hooks directory exists
  fs.mkdirSync(HOOKS_DIR, { recursive: true })

  // 3. copy the hook script
  const scriptSrc = path.join(
    path.dirname(new URL(import.meta.url).pathname),
    '../../../scripts/prompt-trail.sh'
  )
  const scriptDest = path.join(HOOKS_DIR, HOOK_SCRIPT_NAME)
  fs.copyFileSync(scriptSrc, scriptDest)
  fs.chmodSync(scriptDest, '755')
  console.log(`✓ Hook script installed at ${scriptDest}`)

  // 4. merge hook registrations into settings.json
  let settings: Record<string, any> = {}
  if (fs.existsSync(SETTINGS_PATH)) {
    try {
      settings = JSON.parse(fs.readFileSync(SETTINGS_PATH, 'utf-8'))
    } catch {
      settings = {}
    }
  }

  if (!settings.hooks) settings.hooks = {}

  const hookCommand = scriptDest
  const events = ['UserPromptSubmit', 'PreToolUse', 'PostToolUse', 'Stop']

  for (const event of events) {
    if (!settings.hooks[event]) settings.hooks[event] = []

    const alreadyRegistered = settings.hooks[event].some(
      (h: any) => h.hooks?.[0]?.command === hookCommand
    )

    if (!alreadyRegistered) {
      settings.hooks[event].push({
        matcher: '',
        hooks: [{ type: 'command', command: hookCommand }]
      })
    }
  }

  fs.writeFileSync(SETTINGS_PATH, JSON.stringify(settings, null, 2))
  console.log(`✓ Hooks registered in ${SETTINGS_PATH}`)
  console.log('\nPrompt Trail is ready. Now run:')
  console.log('  prompt-trail projects add .')
}