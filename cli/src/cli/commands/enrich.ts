import { getConfig } from '../../api/config.js'
import { createApiClient } from '../../api/client.js'

interface EnrichOptions {
  show?: string
}

/**
 * Inspect the server-side enrichment of one prompt — the Haiku summary, the extracted terms,
 * and whether an embedding has been written. `<id>` is the SERVER prompt id (from Swagger or the
 * ingest response), not the local SQLite id, since enrichment lives only on the server.
 */
export async function runEnrich(options: EnrichOptions): Promise<void> {
  const config = getConfig()
  if (!config?.token) {
    console.error('Not logged in. Run: prompt-trail login')
    process.exit(1)
  }

  if (!options.show) {
    console.error('Usage: prompt-trail enrich --show <server-prompt-id>')
    process.exit(1)
  }

  const promptId = Number(options.show)
  if (!Number.isInteger(promptId) || promptId <= 0) {
    console.error(`Invalid prompt id: ${options.show}`)
    process.exit(1)
  }

  const api = createApiClient(config)

  let view
  try {
    view = await api.getEnrichment(promptId)
  } catch (err) {
    console.error(`Could not fetch enrichment: ${(err as Error).message}`)
    process.exit(1)
  }

  console.log(`\nPrompt #${view.id}  ·  category: ${view.category}`)
  if (!view.enriched) {
    console.log('Status: not yet enriched (the worker will pick it up on its next pass).')
    return
  }

  console.log(`Status: enriched at ${view.enrichedAt}`)
  console.log(`Embedding: ${view.hasEmbedding ? 'present' : 'missing'}`)

  const s = view.summary
  if (s) {
    console.log('\n── Summary ──────────────────────────────────────────────')
    if (s.problem) console.log(`Problem:  ${s.problem}`)
    if (s.solution) console.log(`Solution: ${s.solution}`)
    if (s.rejected) console.log(`Rejected: ${s.rejected}`)
    if (s.outcome) console.log(`Outcome:  ${s.outcome}`)
    if (s.terms && s.terms.length) console.log(`Terms:    ${s.terms.join(', ')}`)
  }

  if (view.embeddingText) {
    console.log('\n── Embedded text (what the vector represents) ───────────')
    console.log(view.embeddingText)
  }
  console.log()
}
