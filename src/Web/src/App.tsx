import { useCallback, useEffect, useState } from 'react'
import { api, type Application, type Stage } from './lib/api'
import { useLiveMatches } from './lib/useLiveMatches'
import { LiveFeed } from './components/LiveFeed'
import { Pipeline } from './components/Pipeline'
import { PostingsTable } from './components/PostingsTable'
import { ProfileForm } from './components/ProfileForm'

type Tab = 'radar' | 'pipeline' | 'profile'

export function App() {
  const [tab, setTab] = useState<Tab>('radar')
  const [applications, setApplications] = useState<Application[]>([])
  const [refreshKey, setRefreshKey] = useState(0)
  const [toast, setToast] = useState<string | null>(null)
  const { matches, status } = useLiveMatches()

  const loadApplications = useCallback(() => {
    api.applications().then(setApplications).catch((e: Error) => setToast(e.message))
  }, [])

  useEffect(loadApplications, [loadApplications])

  // A new live match means the table is stale too.
  useEffect(() => {
    if (matches.length > 0) setRefreshKey((k) => k + 1)
  }, [matches.length])

  const track = async (postingId: string) => {
    try {
      await api.track(postingId)
      setToast('Added to your pipeline as Saved.')
      loadApplications()
    } catch (e) {
      setToast((e as Error).message)
    }
  }

  const move = async (id: string, stage: Stage) => {
    try {
      await api.move(id, stage)
      loadApplications()
    } catch (e) {
      setToast((e as Error).message)
    }
  }

  return (
    <div className="app">
      <header className="top">
        <h1>
          <span className="dot" aria-hidden /> Job Radar
        </h1>
        <nav aria-label="Sections">
          {(['radar', 'pipeline', 'profile'] as Tab[]).map((t) => (
            <button key={t} className={tab === t ? 'tab active' : 'tab'} onClick={() => setTab(t)} aria-current={tab === t}>
              {t === 'radar' ? 'Radar' : t === 'pipeline' ? `Pipeline (${applications.length})` : 'Profile'}
            </button>
          ))}
        </nav>
      </header>
      {toast && (
        <div className="toast" role="status" onClick={() => setToast(null)}>
          {toast}
        </div>
      )}
      <main>
        {tab === 'radar' && (
          <div className="radar">
            <LiveFeed live={matches} status={status} />
            <PostingsTable
              refreshKey={refreshKey}
              trackedPostingIds={new Set(applications.map((a) => a.postingId))}
              onTrack={track}
            />
          </div>
        )}
        {tab === 'pipeline' && <Pipeline applications={applications} onMove={move} />}
        {tab === 'profile' && <ProfileForm onSaved={() => setRefreshKey((k) => k + 1)} />}
      </main>
      <footer className="muted small">
        Postings come from the public APIs of Greenhouse, Lever, Ashby, Workable, Remotive and Arbeitnow. Each links back to its
        source; apply on the employer's page.
      </footer>
    </div>
  )
}
