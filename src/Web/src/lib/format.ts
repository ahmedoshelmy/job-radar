export type ScoreBand = 'strong' | 'fair' | 'weak' | 'none'

export function scoreBand(score: number | null | undefined, threshold = 60): ScoreBand {
  if (score == null) return 'none'
  if (score >= threshold) return 'strong'
  if (score >= threshold / 2) return 'fair'
  return 'weak'
}

export function signed(points: number): string {
  return points > 0 ? `+${points}` : String(points)
}

/** "+15 Remote" (the live event's text form) back into its points and words. */
export function parseReason(reason: string): { points: number; text: string } {
  const match = /^([+-]?\d+)\s+(.*)$/.exec(reason)
  return match ? { points: Number(match[1]), text: match[2] } : { points: 0, text: reason }
}

export function relativeTime(iso: string, now: Date): string {
  const minutes = Math.round((now.getTime() - new Date(iso).getTime()) / 60_000)
  if (minutes < 1) return 'just now'
  if (minutes < 60) return `${minutes} min ago`
  const hours = Math.round(minutes / 60)
  if (hours < 24) return `${hours} h ago`
  return `${Math.round(hours / 24)} d ago`
}

/** Feed names as their owners write them, for the credit line every posting shows. */
const FEED_NAMES: Record<string, string> = {
  greenhouse: 'Greenhouse',
  lever: 'Lever',
  ashby: 'Ashby',
  workable: 'Workable',
  remotive: 'Remotive',
  arbeitnow: 'Arbeitnow',
}

export function feedName(kind: string): string {
  return FEED_NAMES[kind] ?? kind
}

/** SM-2 grades with the wording shown on the practice buttons. */
export const GRADES: { value: number; label: string; hint: string }[] = [
  { value: 0, label: 'Blank', hint: 'Could not answer at all' },
  { value: 1, label: 'Wrong', hint: 'Answered, but wrong' },
  { value: 2, label: 'Almost', hint: 'Wrong, but the right answer felt familiar' },
  { value: 3, label: 'Hard', hint: 'Right, with serious effort' },
  { value: 4, label: 'Good', hint: 'Right, after some thought' },
  { value: 5, label: 'Easy', hint: 'Right, instantly' },
]
