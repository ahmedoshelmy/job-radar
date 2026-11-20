import { useEffect, useState } from 'react'
import { api, type RecentMatch } from '../lib/api'
import { parseReason, relativeTime, signed } from '../lib/format'
import type { LiveStatus } from '../lib/useLiveMatches'
import type { LiveMatch } from '../lib/api'

interface Props {
  live: LiveMatch[]
  status: LiveStatus
}

interface Row {
  postingId: string
  title: string
  company: string
  url: string
  score: number
  reasons: { points: number; text: string }[]
  at: string
  fresh: boolean
}

/** Matches pushed over SignalR on top, the recent history from the API below. */
export function LiveFeed({ live, status }: Props) {
  const [recent, setRecent] = useState<RecentMatch[]>([])
  const now = new Date()

  useEffect(() => {
    api.recentMatches().then(setRecent).catch(() => setRecent([]))
  }, [])

  const seen = new Set(live.map((m) => m.postingId))
  const rows: Row[] = [
    ...live.map((m) => ({ ...m, reasons: m.reasons.map(parseReason), at: m.matchedAt, fresh: true })),
    ...recent
      .filter((m) => !seen.has(m.postingId))
      .map((m) => ({ ...m, reasons: m.reasons.filter((r) => r.points !== 0), at: m.addedAt, fresh: false })),
  ]

  return (
    <section className="panel live" aria-labelledby="live-title">
      <header className="panel-head">
        <h2 id="live-title">Live matches</h2>
        <span className={`status status-${status}`} role="status">
          {status}
        </span>
      </header>
      {rows.length === 0 ? (
        <p className="empty">No matches yet. New postings scoring at or above your threshold appear here the moment they are scored.</p>
      ) : (
        <ol className="match-list">
          {rows.map((m) => (
            <li key={m.postingId} className={m.fresh ? 'match fresh' : 'match'}>
              <div className="match-top">
                <span className="score strong">{m.score}</span>
                <div>
                  <a href={m.url} target="_blank" rel="noreferrer" className="title">
                    {m.title}
                  </a>
                  <div className="muted">
                    {m.company} · {relativeTime(m.at, now)}
                  </div>
                </div>
              </div>
              <ul className="reasons">
                {m.reasons.map((r) => (
                  <li key={r.text} className={r.points > 0 ? 'plus' : 'minus'}>
                    {signed(r.points)} {r.text}
                  </li>
                ))}
              </ul>
            </li>
          ))}
        </ol>
      )}
    </section>
  )
}
