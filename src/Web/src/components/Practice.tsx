import { useCallback, useEffect, useState } from 'react'
import { api, type Application, type Digest, type DueQuestion } from '../lib/api'
import { GRADES } from '../lib/format'

/** SM-2 practice: one due question at a time, graded 0..5; plus this week's digest. */
export function Practice({ applications }: { applications: Application[] }) {
  const [due, setDue] = useState<DueQuestion[]>([])
  const [digest, setDigest] = useState<Digest | null>(null)
  const [revealed, setRevealed] = useState(false)
  const [notes, setNotes] = useState('')
  const [newPrompt, setNewPrompt] = useState('')
  const [target, setTarget] = useState('')

  const load = useCallback(() => {
    api.dueQuestions().then(setDue).catch(() => setDue([]))
    api.digest().then(setDigest).catch(() => setDigest(null))
  }, [])
  useEffect(load, [load])

  const current = due[0]
  const interviewing = applications.filter((a) => a.stage === 'Interview' || a.stage === 'Offer')

  const grade = async (value: number) => {
    if (!current) return
    await api.review(current.question.id, value, notes || undefined)
    setRevealed(false)
    setNotes('')
    load()
  }

  return (
    <div className="radar">
      <section className="panel" aria-labelledby="digest-title">
        <header className="panel-head">
          <h2 id="digest-title">This week</h2>
        </header>
        {digest ? (
          <>
            <p>{digest.summary}</p>
            <ul className="plain">
              {digest.topMatches.map((m) => (
                <li key={m.url}>
                  <span className="score strong">{m.score}</span>{' '}
                  <a href={m.url} target="_blank" rel="noreferrer">
                    {m.title}
                  </a>{' '}
                  <span className="muted">{m.company}</span>
                </li>
              ))}
            </ul>
          </>
        ) : (
          <p className="muted">No digest yet.</p>
        )}
      </section>
      <section className="panel" aria-labelledby="practice-title">
        <header className="panel-head">
          <h2 id="practice-title">Interview practice</h2>
          <span className="muted">{due.length} due</span>
        </header>
        {current ? (
          <div className="flashcard">
            <div className="muted small">
              {current.title} · {current.company}
            </div>
            <p className="prompt">{current.question.prompt}</p>
            {!revealed ? (
              <button onClick={() => setRevealed(true)}>I answered out loud, grade me</button>
            ) : (
              <>
                <textarea
                  aria-label="Notes for next time"
                  placeholder="Notes for next time (optional)"
                  value={notes}
                  onChange={(e) => setNotes(e.target.value)}
                />
                <div className="moves" role="group" aria-label="How did it go?">
                  {GRADES.map((g) => (
                    <button key={g.value} className="ghost small" onClick={() => void grade(g.value)} title={g.hint}>
                      {g.value} {g.label}
                    </button>
                  ))}
                </div>
              </>
            )}
          </div>
        ) : (
          <p className="empty">Nothing due. Questions appear when an application reaches Interview, or add your own below.</p>
        )}
        {interviewing.length > 0 && (
          <form
            className="row add-question"
            onSubmit={(e) => {
              e.preventDefault()
              const applicationId = target || interviewing[0].id
              if (!newPrompt.trim()) return
              void api.addQuestion(applicationId, newPrompt.trim()).then(() => {
                setNewPrompt('')
                load()
              })
            }}
          >
            <select aria-label="Application" value={target} onChange={(e) => setTarget(e.target.value)}>
              {interviewing.map((a) => (
                <option key={a.id} value={a.id}>
                  {a.title} · {a.company}
                </option>
              ))}
            </select>
            <input aria-label="New question" placeholder="Add a question to practise" value={newPrompt} onChange={(e) => setNewPrompt(e.target.value)} />
            <button type="submit">Add</button>
          </form>
        )}
      </section>
    </div>
  )
}
