import type { Config } from './config.js'

export interface IngestResponseItem {
  toolName: string
  toolInput: unknown
  toolOutput?: unknown
  status: string
  toolUseId: string
  resolvedAt?: string
}

export interface IngestPromptPayload {
  projectId: number
  agentSessionId: string
  promptUuid: string
  promptText: string
  assistantResponse: string
  submittedAt: string
  category: string
  diff?: string
  filesChanged: number
  linesAdded: number
  linesRemoved: number
  fileExtensions: string[]
  languages: string[]
  responses: IngestResponseItem[]
}

export interface EnrichmentSummary {
  problem?: string
  solution?: string
  terms?: string[]
  rejected?: string
  outcome?: string
  embedding_text?: string
}

export interface EnrichmentView {
  id: number
  category: string
  enrichedAt: string | null
  enriched: boolean
  summary: EnrichmentSummary | null
  embeddingText: string | null
  hasEmbedding: boolean
}

export interface ApiClient {
  whoami(): Promise<{ id: string; name: string; email?: string }>
  createProject(name: string, description?: string): Promise<{ id: number; name: string }>
  ingestPrompt(payload: IngestPromptPayload): Promise<{ id: number; deduped: boolean }>
  getEnrichment(promptId: number): Promise<EnrichmentView>
}

export function createApiClient(config: Config): ApiClient {
  const base = config.apiUrl.replace(/\/$/, '')
  allowSelfSignedForLocalhost(base)

  const headers = {
    Authorization: `Bearer ${config.token}`,
    'Content-Type': 'application/json',
  }

  async function req(pathname: string, init?: RequestInit): Promise<Response> {
    let res: Response
    try {
      res = await fetch(base + pathname, { ...init, headers })
    } catch (err) {
      throw new Error(`cannot reach ${base} — is the server running? (${(err as Error).message})`)
    }
    if (!res.ok) {
      const body = await res.text().catch(() => '')
      throw new Error(`HTTP ${res.status} ${res.statusText}${body ? ` — ${body}` : ''}`)
    }
    return res
  }

  return {
    async whoami() {
      return (await req('/api/me')).json()
    },
    async createProject(name, description) {
      return (await req('/api/projects', {
        method: 'POST',
        body: JSON.stringify({ name, description }),
      })).json()
    },
    async ingestPrompt(payload) {
      return (await req('/api/prompts', {
        method: 'POST',
        body: JSON.stringify(payload),
      })).json()
    },
    async getEnrichment(promptId) {
      return (await req(`/api/prompts/${promptId}/enrichment`)).json()
    },
  }
}

// The local dev server uses a self-signed cert. Relax TLS verification for localhost only,
// so a paste-token dev flow works without installing the cert. Production hosts stay verified.
function allowSelfSignedForLocalhost(base: string): void {
  try {
    const u = new URL(base)
    if (u.protocol === 'https:' && (u.hostname === 'localhost' || u.hostname === '127.0.0.1')) {
      process.env.NODE_TLS_REJECT_UNAUTHORIZED = '0'
    }
  } catch {
    /* ignore malformed url; fetch will surface it */
  }
}
