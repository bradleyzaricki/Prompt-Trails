import { getConfig } from '../../api/config.js'
import { getUnpushedEntries } from '../../db/queries.js'
import { pushEntry } from '../../api/push-entry.js'

export async function runPush(): Promise<void> {
  const config = getConfig()
  if (!config?.token) {
    console.error('Not logged in. Run: prompt-trail login')
    process.exit(1)
  }

  const entries = getUnpushedEntries()
  if (entries.length === 0) {
    console.log('Nothing to push — all finalized prompts are synced.')
    return
  }

  console.log(`Pushing ${entries.length} prompt(s) to ${config.apiUrl} ...`)

  let pushed = 0
  let deduped = 0
  let failed = 0

  for (const entry of entries) {
    try {
      const result = await pushEntry(entry)
      if (result.deduped) deduped++
      else pushed++
    } catch (err) {
      failed++
      console.error(`  ✗ prompt ${entry.id}: ${(err as Error).message}`)
    }
  }

  console.log(`✓ Pushed ${pushed}, already-synced ${deduped}, failed ${failed}`)
}
