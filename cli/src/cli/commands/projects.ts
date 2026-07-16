import fs from 'fs'
import path from 'path'
import { insertProject, listProjects, deleteProject, getProjectByPath, getProjectById, updateProjectPath } from '../../db/queries.js'


export function runProjectsList(): void {
  const projects = listProjects()

  if (projects.length === 0) {
    console.log('No projects tracked yet. Run: prompt-trail projects add <path>')
    return
  }

  for (const project of projects) {
    console.log(`[${project.id}] ${project.name} — ${project.path}`)
    console.log(`    created: ${project.created_at}`)
  }
}

export function runProjectsAdd(projectPath: string): void {
  const absolutePath = path.resolve(projectPath)

  if (!fs.existsSync(absolutePath)) {
    console.error(`Error: path does not exist: ${absolutePath}`)
    process.exit(1)
  }

  if (!fs.statSync(absolutePath).isDirectory()) {
    console.error(`Error: path is not a directory: ${absolutePath}`)
    process.exit(1)
  }

  const existing = getProjectByPath(absolutePath)
  if (existing) {
    console.error(`Error: project already tracked: ${absolutePath}`)
    process.exit(1)
  }

  const name = path.basename(absolutePath)
  const project = insertProject(name, absolutePath)
  console.log(`✓ Now tracking: ${project.name} (id: ${project.id})`)
  console.log(`  path: ${project.path}`)
}

export function runProjectsRelocate(id: string, projectPath?: string): void {
  const projectId = parseInt(id, 10)

  if (isNaN(projectId)) {
    console.error(`Error: invalid project id: ${id}`)
    process.exit(1)
  }

  const project = getProjectById(projectId)
  if (!project) {
    console.error(`Error: no project with id ${projectId}`)
    process.exit(1)
  }

  // Default to the current directory when no path is given.
  const absolutePath = path.resolve(projectPath ?? process.cwd())

  if (!fs.existsSync(absolutePath)) {
    console.error(`Error: path does not exist: ${absolutePath}`)
    process.exit(1)
  }

  if (!fs.statSync(absolutePath).isDirectory()) {
    console.error(`Error: path is not a directory: ${absolutePath}`)
    process.exit(1)
  }

  const existing = getProjectByPath(absolutePath)
  if (existing && existing.id !== projectId) {
    console.error(`Error: path already tracked by project ${existing.id} (${existing.name})`)
    process.exit(1)
  }

  updateProjectPath(projectId, absolutePath)
  console.log(`✓ Relocated project ${projectId} (${project.name})`)
  console.log(`  ${project.path} → ${absolutePath}`)
}

export function runProjectsRemove(id: string): void {
  const projectId = parseInt(id, 10)

  if (isNaN(projectId)) {
    console.error(`Error: invalid project id: ${id}`)
    process.exit(1)
  }

  deleteProject(projectId)
  console.log(`✓ Removed project ${projectId}`)
}