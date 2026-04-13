import { getPromptEntry } from '../../db/queries.js'
import { formatPromptEntryDetail } from '../utils/display.js'

export function runShow(id: string): void {
  const entryId = parseInt(id, 10)

  if (isNaN(entryId)) {
    console.error(`Error: invalid entry id: ${id}`)
    process.exit(1)
  }

  const entry = getPromptEntry(entryId)

  if (!entry) {
    console.error(`Error: no entry found with id: ${entryId}`)
    process.exit(1)
  }

  console.log(formatPromptEntryDetail(entry))
}