export type WorkMode = 'Unknown' | 'Remote' | 'Hybrid' | 'Onsite'
export type Seniority = 'Unknown' | 'Intern' | 'Junior' | 'Mid' | 'Senior' | 'Lead'
export type Stage = 'Saved' | 'Applied' | 'Interview' | 'Offer' | 'Rejected' | 'Withdrawn'

export interface MatchReason {
  code: string
  points: number
  text: string
}

export interface Posting {
  id: string
  title: string
  company: string
  location: string
  workMode: WorkMode
  seniority: Seniority
  url: string
  source: string
  alsoSeenIn: string[]
  salaryText: string | null
  postedAt: string | null
  firstSeenAt: string
  score: number | null
  reasons: MatchReason[]
}

/** What the hub pushes (the JobMatched integration event). */
export interface LiveMatch {
  postingId: string
  score: number
  reasons: string[]
  title: string
  company: string
  url: string
  matchedAt: string
}

export interface RecentMatch {
  postingId: string
  title: string
  company: string
  url: string
  score: number
  reasons: MatchReason[]
  addedAt: string
}

export interface Profile {
  headline: string
  skills: string[]
  seniorities: Seniority[]
  remoteOnly: boolean
  locations: string[]
  salaryFloorUsd: number | null
  maxYearsAsked: number | null
  excludedTitleWords: string[]
  notifyThreshold: number
}

export interface StageEvent {
  from: Stage | null
  to: Stage
  at: string
  note: string | null
}

export interface Application {
  id: string
  postingId: string
  title: string
  company: string
  url: string
  stage: Stage
  allowedNext: Stage[]
  createdAt: string
  updatedAt: string
  history: StageEvent[]
}

export interface PostingFilter {
  query: string
  remoteOnly: boolean
  minScore: number
}

/** RFC 7807 problem details turned into a readable error. */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    message: string,
  ) {
    super(message)
  }
}

async function call<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(path, {
    ...init,
    headers: { 'Content-Type': 'application/json', ...init?.headers },
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null
    throw new ApiError(response.status, problem?.detail ?? problem?.title ?? `HTTP ${response.status}`)
  }
  return (await response.json()) as T
}

export function postingsQuery(filter: PostingFilter): string {
  const params = new URLSearchParams()
  if (filter.query.trim()) params.set('query', filter.query.trim())
  if (filter.remoteOnly) params.set('remote', 'true')
  if (filter.minScore > 0) params.set('minScore', String(filter.minScore))
  const qs = params.toString()
  return qs ? `/api/postings?${qs}` : '/api/postings'
}

export const api = {
  postings: (filter: PostingFilter) => call<Posting[]>(postingsQuery(filter)),
  recentMatches: () => call<RecentMatch[]>('/api/matches?take=30'),
  profile: () => call<{ profile: Profile; updatedAt: string }>('/api/profile'),
  saveProfile: (profile: Profile) =>
    call<{ rescored: number; announced: number }>('/api/profile', { method: 'PUT', body: JSON.stringify(profile) }),
  applications: () => call<Application[]>('/api/applications'),
  track: (postingId: string) =>
    call<Application>('/api/applications', { method: 'POST', body: JSON.stringify({ postingId }) }),
  move: (id: string, stage: Stage) =>
    call<Application>(`/api/applications/${id}/stage`, { method: 'POST', body: JSON.stringify({ stage }) }),
}
