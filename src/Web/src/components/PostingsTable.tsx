import { useEffect, useState } from 'react'
import { api, type MatchExplanation, type Posting, type PostingFilter } from '../lib/api'
import { feedName, scoreBand, signed } from '../lib/format'

interface Props {
  refreshKey: number
  trackedPostingIds: Set<string>
  onTrack: (postingId: string) => Promise<void>
}

export function PostingsTable({ refreshKey, trackedPostingIds, onTrack }: Props) {
  const [filter, setFilter] = useState<PostingFilter>({ query: '', remoteOnly: false, minScore: 0 })
  const [postings, setPostings] = useState<Posting[]>([])
  const [open, setOpen] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const timer = setTimeout(() => {
      api
        .postings(filter)
        .then((rows) => {
          setPostings(rows)
          setError(null)
        })
        .catch((e: Error) => setError(e.message))
    }, 250)
    return () => clearTimeout(timer)
  }, [filter, refreshKey])

  return (
    <section className="panel" aria-labelledby="postings-title">
      <header className="panel-head">
        <h2 id="postings-title">Postings</h2>
        <span className="muted">{postings.length} shown</span>
      </header>
      <div className="filters">
        <input
          type="search"
          placeholder="Search title, company, description"
          aria-label="Search postings"
          value={filter.query}
          onChange={(e) => setFilter({ ...filter, query: e.target.value })}
        />
        <label className="check">
          <input type="checkbox" checked={filter.remoteOnly} onChange={(e) => setFilter({ ...filter, remoteOnly: e.target.checked })} />
          Remote only
        </label>
        <label className="range">
          Min score <strong>{filter.minScore}</strong>
          <input
            type="range"
            min={0}
            max={100}
            step={5}
            value={filter.minScore}
            onChange={(e) => setFilter({ ...filter, minScore: Number(e.target.value) })}
          />
        </label>
      </div>
      {error && <p className="error">Could not load postings: {error}</p>}
      <div className="table-wrap">
        <table>
          <thead>
            <tr>
              <th scope="col">Score</th>
              <th scope="col">Role</th>
              <th scope="col">Where</th>
              <th scope="col">Source</th>
              <th scope="col" aria-label="Actions" />
            </tr>
          </thead>
          <tbody>
            {postings.map((p) => (
              <PostingRow
                key={p.id}
                posting={p}
                expanded={open === p.id}
                tracked={trackedPostingIds.has(p.id)}
                onToggle={() => setOpen(open === p.id ? null : p.id)}
                onTrack={() => onTrack(p.id)}
              />
            ))}
          </tbody>
        </table>
      </div>
    </section>
  )
}

interface RowProps {
  posting: Posting
  expanded: boolean
  tracked: boolean
  onToggle: () => void
  onTrack: () => void
}

function PostingRow({ posting: p, expanded, tracked, onToggle, onTrack }: RowProps) {
  return (
    <>
      <tr>
        <td>
          <button className={`score ${scoreBand(p.score)}`} onClick={onToggle} aria-expanded={expanded} title="Why this score">
            {p.score ?? '–'}
          </button>
        </td>
        <td>
          <a href={p.url} target="_blank" rel="noreferrer" className="title">
            {p.title}
          </a>
          <div className="muted">
            {p.company}
            {p.seniority !== 'Unknown' && ` · ${p.seniority}`}
            {p.salaryText && ` · ${p.salaryText}`}
          </div>
        </td>
        <td>
          <span className={`pill pill-${p.workMode.toLowerCase()}`}>{p.workMode}</span>
          <div className="muted small">{p.location}</div>
        </td>
        <td className="small">
          via <a href={p.url} target="_blank" rel="noreferrer">{feedName(p.source)}</a>
          {p.alsoSeenIn.length > 0 && <div className="muted">also on {p.alsoSeenIn.map(feedName).join(', ')}</div>}
        </td>
        <td>
          <button className="ghost" onClick={onTrack} disabled={tracked}>
            {tracked ? 'Tracked' : 'Track'}
          </button>
        </td>
      </tr>
      {expanded && (
        <tr className="why">
          <td colSpan={5}>
            <AiSummary postingId={p.id} />
            <ul className="reasons">
              {p.reasons.map((r) => (
                <li key={r.code + r.text} className={r.points > 0 ? 'plus' : r.points < 0 ? 'minus' : 'zero'}>
                  {signed(r.points)} {r.text}
                </li>
              ))}
            </ul>
          </td>
        </tr>
      )}
    </>
  )
}

/** The optional free-AI paragraph; renders nothing while loading, when AI is off (the default) or when it fails. */
function AiSummary({ postingId }: { postingId: string }) {
  const [explanation, setExplanation] = useState<MatchExplanation | null>(null)

  useEffect(() => {
    let current = true
    api
      .explanation(postingId)
      .then((e) => current && setExplanation(e))
      .catch(() => current && setExplanation(null))
    return () => {
      current = false
    }
  }, [postingId])

  if (!explanation?.aiEnabled || !explanation.summary) return null
  return (
    <p className="ai-summary">
      <span className="muted small">AI summary · the score above comes from the rules</span>
      <br />
      {explanation.summary}
    </p>
  )
}
