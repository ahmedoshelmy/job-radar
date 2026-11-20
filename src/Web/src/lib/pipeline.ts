import type { Application, Stage } from './api'

/** Board columns, left to right. Rejected and Withdrawn share the closed column. */
export const COLUMNS: { key: string; title: string; stages: Stage[] }[] = [
  { key: 'saved', title: 'Saved', stages: ['Saved'] },
  { key: 'applied', title: 'Applied', stages: ['Applied'] },
  { key: 'interview', title: 'Interview', stages: ['Interview'] },
  { key: 'offer', title: 'Offer', stages: ['Offer'] },
  { key: 'closed', title: 'Closed', stages: ['Rejected', 'Withdrawn'] },
]

export function groupByColumn(applications: Application[]): Record<string, Application[]> {
  const groups: Record<string, Application[]> = Object.fromEntries(COLUMNS.map((c) => [c.key, []]))
  for (const application of applications) {
    const column = COLUMNS.find((c) => c.stages.includes(application.stage))
    if (column) groups[column.key].push(application)
  }
  for (const key of Object.keys(groups)) {
    groups[key].sort((a, b) => b.updatedAt.localeCompare(a.updatedAt))
  }
  return groups
}

/** Mirrors the server's rule for the UI; the server stays the authority and answers 409 otherwise. */
export function canDrop(application: Application, stage: Stage): boolean {
  return application.allowedNext.includes(stage)
}

/** The stage a drop onto a column means: the first of its stages the application may move to. */
export function dropTarget(application: Application, columnKey: string): Stage | null {
  const column = COLUMNS.find((c) => c.key === columnKey)
  return column?.stages.find((s) => canDrop(application, s)) ?? null
}

export function daysInStage(application: Application, now: Date): number {
  const since = application.history.at(-1)?.at ?? application.updatedAt
  return Math.max(0, Math.floor((now.getTime() - new Date(since).getTime()) / 86_400_000))
}
