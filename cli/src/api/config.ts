import fs from 'fs'
import path from 'path'
import { getPromptTrailDir } from '../db/index.js'

export interface Config {
  apiUrl: string
  token: string
}

export const DEFAULT_API_URL = 'https://localhost:2324'

function configPath(): string {
  return path.join(getPromptTrailDir(), 'config.json')
}

export function getConfig(): Config | null {
  const p = configPath()
  if (!fs.existsSync(p)) return null
  try {
    return JSON.parse(fs.readFileSync(p, 'utf-8')) as Config
  } catch {
    return null
  }
}

export function saveConfig(config: Config): void {
  const dir = getPromptTrailDir()
  fs.mkdirSync(dir, { recursive: true })
  // 0600 — the token is a credential; keep it readable only by the owner.
  fs.writeFileSync(configPath(), JSON.stringify(config, null, 2), { mode: 0o600 })
}
