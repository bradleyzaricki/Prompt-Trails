import Database from 'better-sqlite3'
import path from 'path'
import os from 'os'
import fs from 'fs'

let db: Database.Database | null = null

export function getPromptTrailDir(): string {
  return path.join(os.homedir(), '.prompt-trail')
}

export function getShadowGitDir(projectId: number): string {
  return path.join(getPromptTrailDir(), 'shadow', String(projectId))
}

export function getDb(): Database.Database {
  if (db) return db

  const dir = getPromptTrailDir()
  fs.mkdirSync(dir, { recursive: true })

  db = new Database(path.join(dir, 'db.sqlite'))
  db.pragma('journal_mode = WAL')
  db.pragma('foreign_keys = ON')

  migrate(db)
  return db
}

function migrate(db: Database.Database): void {
  db.exec(`
    CREATE TABLE IF NOT EXISTS projects (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      name TEXT NOT NULL,
      path TEXT NOT NULL UNIQUE,
      description TEXT,
      created_at TEXT NOT NULL DEFAULT (datetime('now')),
      updated_at TEXT NOT NULL DEFAULT (datetime('now'))
    );

    CREATE TABLE IF NOT EXISTS sessions (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
      started_at TEXT NOT NULL DEFAULT (datetime('now')),
      ended_at TEXT,
      claude_session_id TEXT NOT NULL UNIQUE
    );

    CREATE TABLE IF NOT EXISTS prompt_entries (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      session_id INTEGER NOT NULL REFERENCES sessions(id) ON DELETE CASCADE,
      project_id INTEGER NOT NULL REFERENCES projects(id) ON DELETE CASCADE,
      prompt_text TEXT NOT NULL,
      claude_response TEXT NOT NULL DEFAULT '',
      submitted_at TEXT NOT NULL,
      finalized INTEGER NOT NULL DEFAULT 0,
      accepted INTEGER NOT NULL DEFAULT 0,
      accepted_at TEXT,
      diff TEXT,
      files_changed INTEGER NOT NULL DEFAULT 0,
      lines_added INTEGER NOT NULL DEFAULT 0,
      lines_removed INTEGER NOT NULL DEFAULT 0,
      file_extensions TEXT NOT NULL DEFAULT '[]',
      languages TEXT NOT NULL DEFAULT '[]',
      prompt_category TEXT NOT NULL DEFAULT 'other',
      prompt_uuid TEXT NOT NULL DEFAULT ''
    );

    CREATE TABLE IF NOT EXISTS prompt_responses (
      id INTEGER PRIMARY KEY AUTOINCREMENT,
      prompt_entry_id INTEGER NOT NULL REFERENCES prompt_entries(id) ON DELETE CASCADE,
      tool_name TEXT NOT NULL,
      tool_input TEXT NOT NULL DEFAULT '{}',
      tool_output TEXT,
      status TEXT NOT NULL DEFAULT 'pending',
      tool_use_id TEXT NOT NULL DEFAULT '',
      created_at TEXT NOT NULL DEFAULT (datetime('now')),
      resolved_at TEXT
    );

    CREATE INDEX IF NOT EXISTS idx_sessions_project_id ON sessions(project_id);
    CREATE INDEX IF NOT EXISTS idx_prompt_entries_session_id ON prompt_entries(session_id);
    CREATE INDEX IF NOT EXISTS idx_prompt_entries_project_id ON prompt_entries(project_id);
    CREATE INDEX IF NOT EXISTS idx_prompt_responses_entry_id ON prompt_responses(prompt_entry_id);

    CREATE VIRTUAL TABLE IF NOT EXISTS prompt_entries_fts USING fts5(
      prompt_text,
      diff,
      content='prompt_entries',
      content_rowid='id'
    );

    CREATE TRIGGER IF NOT EXISTS prompt_entries_ai AFTER INSERT ON prompt_entries BEGIN
      INSERT INTO prompt_entries_fts(rowid, prompt_text, diff)
      VALUES (new.id, new.prompt_text, new.diff);
    END;

    CREATE TRIGGER IF NOT EXISTS prompt_entries_au AFTER UPDATE ON prompt_entries BEGIN
      INSERT INTO prompt_entries_fts(prompt_entries_fts, rowid, prompt_text, diff)
      VALUES ('delete', old.id, old.prompt_text, old.diff);
      INSERT INTO prompt_entries_fts(rowid, prompt_text, diff)
      VALUES (new.id, new.prompt_text, new.diff);
    END;

    CREATE TRIGGER IF NOT EXISTS prompt_entries_ad AFTER DELETE ON prompt_entries BEGIN
      INSERT INTO prompt_entries_fts(prompt_entries_fts, rowid, prompt_text, diff)
      VALUES ('delete', old.id, old.prompt_text, old.diff);
    END;
  `)
}
