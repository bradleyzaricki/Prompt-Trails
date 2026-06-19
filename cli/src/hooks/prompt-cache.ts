import fs from 'fs'
import path from 'path'
import { getPromptTrailDir } from '../db/index.js'
import type { SessionCache, SessionCacheEntry } from '../types/index.js'

function getCachePath(): string {
  return path.join(getPromptTrailDir(), 'prompt-cache.json')
}

function readCache(): SessionCache {
  const cachePath = getCachePath()
  if (!fs.existsSync(cachePath)) return {}
  try {
    return JSON.parse(fs.readFileSync(cachePath, 'utf-8'))
  } catch {
    return {}
  }
}

function writeCache(cache: SessionCache): void {
  fs.writeFileSync(getCachePath(), JSON.stringify(cache, null, 2))
}

export function setPromptCacheEntry(claudeSessionId: string, entry: SessionCacheEntry): void {
  const cache = readCache()
  cache[claudeSessionId] = entry
  writeCache(cache)
}

export function getPromptCacheEntry(claudeSessionId: string): SessionCacheEntry | null {
  const cache = readCache()
  return cache[claudeSessionId] ?? null
}

export function clearPromptCacheEntry(claudeSessionId: string): void {
  const cache = readCache()
  delete cache[claudeSessionId]
  writeCache(cache)
}