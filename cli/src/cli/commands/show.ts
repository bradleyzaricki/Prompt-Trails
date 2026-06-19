import { getPromptEntry, listResponsesForEntry } from '../../db/queries.js'
import { formatPromptEntryDetail } from '../utils/display.js'

//show details of a single prompt in the project by id
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

  const responses = listResponsesForEntry(entryId)
  console.log(formatPromptEntryDetail(entry, responses))
}