import { describe, expect, it } from 'vitest'
import type { Application, Stage } from './api'
import { postingsQuery } from './api'
import { feedName, parseReason, relativeTime, scoreBand, signed } from './format'
import { canDrop, daysInStage, dropTarget, groupByColumn } from './pipeline'

const app = (id: string, stage: Stage, allowedNext: Stage[], updatedAt = '2026-11-20T10:00:00Z'): Application => ({
  id,
  postingId: `p-${id}`,
  title: 'Backend Engineer',
  company: 'Nilewave Labs',
  url: 'https://example.com',
  stage,
  allowedNext,
  createdAt: '2026-11-18T10:00:00Z',
  updatedAt,
  history: [{ from: null, to: stage, at: updatedAt, note: null }],
})

describe('pipeline board', () => {
  it('groups applications into columns, closed stages together, newest first', () => {
    const groups = groupByColumn([
      app('a', 'Saved', ['Applied', 'Withdrawn'], '2026-11-19T10:00:00Z'),
      app('b', 'Saved', ['Applied', 'Withdrawn'], '2026-11-20T10:00:00Z'),
      app('c', 'Rejected', []),
      app('d', 'Withdrawn', []),
    ])
    expect(groups.saved.map((a) => a.id)).toEqual(['b', 'a'])
    expect(groups.closed).toHaveLength(2)
    expect(groups.offer).toEqual([])
  })

  it('allows only the moves the server allows', () => {
    const saved = app('a', 'Saved', ['Applied', 'Withdrawn'])
    expect(canDrop(saved, 'Applied')).toBe(true)
    expect(canDrop(saved, 'Offer')).toBe(false)
    expect(dropTarget(saved, 'closed')).toBe('Withdrawn')
    expect(dropTarget(saved, 'interview')).toBeNull()
    expect(dropTarget(app('b', 'Applied', ['Interview', 'Rejected', 'Withdrawn']), 'closed')).toBe('Rejected')
  })

  it('counts whole days in the current stage', () => {
    expect(daysInStage(app('a', 'Applied', [], '2026-11-17T09:00:00Z'), new Date('2026-11-20T10:00:00Z'))).toBe(3)
  })
})

describe('formatting', () => {
  it('bands scores against the threshold', () => {
    expect(scoreBand(82)).toBe('strong')
    expect(scoreBand(45)).toBe('fair')
    expect(scoreBand(10)).toBe('weak')
    expect(scoreBand(null)).toBe('none')
  })

  it('round-trips signed reasons', () => {
    expect(signed(15)).toBe('+15')
    expect(signed(-25)).toBe('-25')
    expect(parseReason('+15 Remote')).toEqual({ points: 15, text: 'Remote' })
    expect(parseReason('-25 Limited to USA only')).toEqual({ points: -25, text: 'Limited to USA only' })
    expect(parseReason('odd')).toEqual({ points: 0, text: 'odd' })
  })

  it('formats relative times and feed credits', () => {
    const now = new Date('2026-11-20T12:00:00Z')
    expect(relativeTime('2026-11-20T11:59:40Z', now)).toBe('just now')
    expect(relativeTime('2026-11-20T11:15:00Z', now)).toBe('45 min ago')
    expect(relativeTime('2026-11-18T12:00:00Z', now)).toBe('2 d ago')
    expect(feedName('remotive')).toBe('Remotive')
  })

  it('builds the postings query from the filter', () => {
    expect(postingsQuery({ query: '', remoteOnly: false, minScore: 0 })).toBe('/api/postings')
    expect(postingsQuery({ query: ' .net ', remoteOnly: true, minScore: 60 })).toBe('/api/postings?query=.net&remote=true&minScore=60')
  })
})
