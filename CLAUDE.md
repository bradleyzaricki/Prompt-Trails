# Prompt Trail

A CLI tool that hooks into Claude Code to record prompts, tool usage, code changes, and Claude's responses. Designed as the ingestion layer for a RAG pipeline.

## Architecture

```
User's machine                         Server (planned)
┌─────────────────────┐               ┌──────────────────────┐
│ Claude Code          │               │ ASP.NET Minimal API  │
│   ↓ hooks            │               │   - REST endpoints   │
│ prompt-trail CLI     │──── POST ────→│   - Haiku reclassify │
│   - local SQLite     │               │   - Embedding gen    │
│   - shadow git       │               │   - Vector storage   │
│   - conversation log │←── response ──│   - MCP server       │
└─────────────────────┘               └──────────────────────┘
```

### Data flow
1. Claude Code hooks (`UserPromptSubmit`, `Stop`) trigger the CLI
2. CLI creates a prompt entry from the hook payload
3. On finalization (next prompt or Stop), CLI:
   - Reads Claude's conversation log JSONL (`~/.claude/projects/{encoded-path}/{sessionId}.jsonl`) to backfill Claude's response text and the full tool call breadcrumb trail
   - Takes a shadow git diff to capture file changes
   - Categorizes the prompt by tool usage (question/code_change/command/other)
4. Planned: CLI POSTs finalized entry to ASP.NET API, API responds with enriched classification

### Key design decisions
- **Conversation logs are the primary data source** for Claude's responses and tool call accept/reject status. Hooks are triggers only.
- **PreToolUse/PostToolUse hooks are no-ops** — conversation log captures this data more reliably (PostToolUse doesn't fire in VS Code extension)
- **Shadow git** (separate GIT_DIR per project at `~/.prompt-trail/shadow/{projectId}`) provides diffs
- **Tool-based prompt classification** is the initial pass; LLM reclassification (Haiku) planned for the API
- **`tool_calls` column was removed** — all tool data lives in `prompt_responses` table now
- **Local SQLite is a buffer** — the API will have its own database (SQL Server/Postgres)
- **API response enrichment** — when CLI POSTs to the API, the response includes reclassified `prompt_category` which updates local SQLite

## Database schema (local SQLite at `~/.prompt-trail/db.sqlite`)

### projects
| Column | Type |
|---|---|
| id | INTEGER PK AUTOINCREMENT |
| name | TEXT NOT NULL |
| path | TEXT NOT NULL UNIQUE |
| description | TEXT |
| created_at | TEXT DEFAULT datetime('now') |
| updated_at | TEXT DEFAULT datetime('now') |

### sessions
| Column | Type |
|---|---|
| id | INTEGER PK AUTOINCREMENT |
| project_id | INTEGER FK→projects |
| started_at | TEXT DEFAULT datetime('now') |
| ended_at | TEXT |
| claude_session_id | TEXT NOT NULL UNIQUE |

### prompt_entries
| Column | Type | Notes |
|---|---|---|
| id | INTEGER PK AUTOINCREMENT | |
| session_id | INTEGER FK→sessions | |
| project_id | INTEGER FK→projects | |
| prompt_text | TEXT NOT NULL | User's prompt (system tags stripped) |
| claude_response | TEXT DEFAULT '' | Claude's text response (backfilled from conversation log) |
| submitted_at | TEXT NOT NULL | |
| finalized | INTEGER DEFAULT 0 | 0=in progress, 1=done |
| accepted | INTEGER DEFAULT 0 | 1=produced file changes |
| accepted_at | TEXT | |
| diff | TEXT | Unified diff from shadow git |
| files_changed | INTEGER DEFAULT 0 | |
| lines_added | INTEGER DEFAULT 0 | |
| lines_removed | INTEGER DEFAULT 0 | |
| file_extensions | TEXT DEFAULT '[]' | JSON array |
| languages | TEXT DEFAULT '[]' | JSON array |
| prompt_category | TEXT DEFAULT 'other' | question/code_change/command/other |
| prompt_uuid | TEXT DEFAULT '' | For dedup against conversation log |

### prompt_responses (breadcrumb trail)
| Column | Type | Notes |
|---|---|---|
| id | INTEGER PK AUTOINCREMENT | |
| prompt_entry_id | INTEGER FK→prompt_entries | |
| tool_name | TEXT NOT NULL | Read, Edit, Bash, AskUserQuestion, etc. |
| tool_input | TEXT DEFAULT '{}' | JSON string |
| tool_output | TEXT | JSON string (includes user answers for AskUserQuestion) |
| status | TEXT DEFAULT 'pending' | pending/accepted/rejected |
| tool_use_id | TEXT DEFAULT '' | Links to conversation log |
| created_at | TEXT DEFAULT datetime('now') | |
| resolved_at | TEXT | |

### prompt_entries_fts
FTS5 virtual table indexing `prompt_text` and `diff` for full-text search.

## Project structure

```
prompt-trail/
  CLAUDE.md
  cli/                        # TypeScript CLI (npm package)
    src/
      cli/
        index.ts              # Commander CLI entry point
        commands/
          init.ts             # Install hooks, init DB
          record.ts           # Hook handler (UserPromptSubmit, Stop)
          log.ts              # Show prompt history
          show.ts             # Show single entry detail with breadcrumb trail
          search.ts           # FTS5 search
          stats.ts            # Project statistics
          clear.ts            # Delete all entries for a project
          projects.ts         # Add/list/remove tracked projects
        utils/
          display.ts          # ANSI-colored formatters for log/show output
          metadata.ts         # Tool-based categorization, extension/language detection
      db/
        index.ts              # SQLite init, migration, getDb()
        queries.ts            # All database queries
      hooks/
        conversation-log.ts   # JSONL parser for Claude Code conversation logs
        shadow-git.ts         # Shadow git repo for diffs (simple-git)
        prompt-cache.ts       # Session cache linking hooks to prompt entries
      types/
        index.ts              # All TypeScript interfaces
    scripts/
      prompt-trail.sh         # Bridge script called by Claude Code hooks
    package.json
    tsconfig.json
  server/                     # ASP.NET Minimal API (planned)
  frontend/                   # Blazor or React app (planned)
```

## CLI commands

- `prompt-trail init` — Install hooks and initialize DB
- `prompt-trail projects add <path>` — Start tracking a project
- `prompt-trail projects list` — List tracked projects
- `prompt-trail log [--accepted] [--project-id <id>]` — Show prompt history
- `prompt-trail show <id>` — Show entry detail with breadcrumb trail
- `prompt-trail search <query> [--project-id <id>]` — Full-text search
- `prompt-trail stats` — Project statistics
- `prompt-trail clear` — Delete all entries for current project

## Building

```bash
npm run build    # Compile TypeScript
npm link         # Link globally (already done)
```

Database reset (needed after schema changes):
```bash
rm -f ~/.prompt-trail/db.sqlite ~/.prompt-trail/db.sqlite-wal ~/.prompt-trail/db.sqlite-shm
```

## Planned: ASP.NET API

The .NET backend should:
1. Receive POST calls from the CLI with finalized prompt entries + breadcrumb trails
2. Store in its own database (SQL Server or Postgres, not SQLite)
3. Run Haiku classification on prompt text for richer categories
4. Return enriched data in the POST response (category, tags) for local DB update
5. Generate embeddings for RAG (prompt text + Claude response)
6. Expose MCP server for LLM clients to query the knowledge base
7. Support multi-user via API keys
