import readline from 'readline'
import { getConfig, saveConfig, DEFAULT_API_URL, type Config } from '../../api/config.js'
import { createApiClient } from '../../api/client.js'

interface LoginOptions {
  url?: string
  token?: string
}

export async function runLogin(options: LoginOptions): Promise<void> {
  const apiUrl = options.url ?? getConfig()?.apiUrl ?? DEFAULT_API_URL

  let token = options.token ?? (await promptLine('Paste your Prompt Trail token (pt_...): '))
  token = token.trim()
  if (!token) {
    console.error('No token provided.')
    process.exit(1)
  }

  const config: Config = { apiUrl, token }
  const api = createApiClient(config)

  try {
    const me = await api.whoami()
    saveConfig(config)
    console.log(`✓ Logged in as ${me.name}${me.email ? ` <${me.email}>` : ''}`)
    console.log(`  API: ${apiUrl}`)
    console.log('\nNow run:  prompt-trail push')
  } catch (err) {
    console.error(`✗ Login failed: ${(err as Error).message}`)
    process.exit(1)
  }
}

export async function runWhoami(): Promise<void> {
  const config = getConfig()
  if (!config?.token) {
    console.error('Not logged in. Run: prompt-trail login')
    process.exit(1)
  }
  try {
    const me = await createApiClient(config).whoami()
    console.log(`${me.name}${me.email ? ` <${me.email}>` : ''} (id ${me.id})`)
    console.log(`API: ${config.apiUrl}`)
  } catch (err) {
    console.error(`✗ ${(err as Error).message}`)
    process.exit(1)
  }
}

function promptLine(question: string): Promise<string> {
  const rl = readline.createInterface({ input: process.stdin, output: process.stdout })
  return new Promise((resolve) =>
    rl.question(question, (answer) => {
      rl.close()
      resolve(answer)
    })
  )
}
