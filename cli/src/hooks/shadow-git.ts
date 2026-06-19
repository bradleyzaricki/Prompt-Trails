import path from 'path'
import fs from 'fs'
import { simpleGit } from 'simple-git'
import type { DiffStats } from '../types/index.js'

function getGit(shadowDir: string, projectPath: string) {
  return simpleGit(projectPath).env({
    GIT_DIR: shadowDir,
    GIT_WORK_TREE: projectPath,
  })
}


export async function initShadowRepo(shadowDir: string, projectPath: string): Promise<void> {
  if (!fs.existsSync(shadowDir)) {
    fs.mkdirSync(shadowDir, { recursive: true })
  }

  const git = getGit(shadowDir, projectPath)
  const isRepo = fs.existsSync(path.join(shadowDir, 'HEAD'))

  if (!isRepo) {
    await git.init()
    await git.addConfig('user.email', 'prompt-trail@local')
    await git.addConfig('user.name', 'Prompt Trail')

    // ignore common noise
    const excludePath = path.join(shadowDir, 'info', 'exclude')
    fs.mkdirSync(path.dirname(excludePath), { recursive: true })
    fs.writeFileSync(excludePath, [
      'node_modules/',
      'dist/',
      '.env',
      '*.log',
      '.DS_Store',
    ].join('\n'))

    // make an empty initial commit so HEAD always exists
    await git.commit('init', [], { '--allow-empty': null })
  }
}

//Creates a snapshot of the codebase before prompt execution to compare against delta later 
export async function snapshotBefore(shadowDir: string, projectPath: string): Promise<void> {
  const git = getGit(shadowDir, projectPath)
  await git.add('-A')
  await git.commit('before', [], { '--allow-empty': null })
}

//Takes a snapshot after a prompt executes to compare against the previous snapshot for a diff
export async function snapshotAfter(shadowDir: string, projectPath: string): Promise<DiffStats> {
  const git = getGit(shadowDir, projectPath)
  await git.add('-A')
  await git.commit('after', [], { '--allow-empty': null })

  const diff = await git.diff(['HEAD~1', 'HEAD'])
  const diffSummary = await git.diffSummary(['HEAD~1', 'HEAD'])

  return {
    diff,
    files_changed: diffSummary.files.length,
    lines_added: diffSummary.insertions,
    lines_removed: diffSummary.deletions,
  }
}