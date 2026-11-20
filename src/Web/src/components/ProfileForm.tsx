import { useEffect, useState } from 'react'
import { api, type Profile, type Seniority } from '../lib/api'

const LEVELS: Seniority[] = ['Intern', 'Junior', 'Mid', 'Senior', 'Lead']
const list = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean)

/** Saving rescores every posting on the server; newly qualifying ones arrive in the live feed. */
export function ProfileForm({ onSaved }: { onSaved: () => void }) {
  const [profile, setProfile] = useState<Profile | null>(null)
  const [message, setMessage] = useState<string | null>(null)

  useEffect(() => {
    api.profile().then((p) => setProfile(p.profile)).catch((e: Error) => setMessage(e.message))
  }, [])

  if (!profile) return <p className="muted">{message ?? 'Loading profile…'}</p>

  const save = async () => {
    try {
      const result = await api.saveProfile(profile)
      setMessage(`Saved. Rescored ${result.rescored} postings, ${result.announced} new matches.`)
      onSaved()
    } catch (e) {
      setMessage((e as Error).message)
    }
  }

  return (
    <form
      className="panel profile"
      onSubmit={(e) => {
        e.preventDefault()
        void save()
      }}
    >
      <label>
        Headline
        <input value={profile.headline} onChange={(e) => setProfile({ ...profile, headline: e.target.value })} />
      </label>
      <label>
        Skills (comma separated)
        <input value={profile.skills.join(', ')} onChange={(e) => setProfile({ ...profile, skills: list(e.target.value) })} />
      </label>
      <fieldset>
        <legend>Levels</legend>
        {LEVELS.map((level) => (
          <label key={level} className="check">
            <input
              type="checkbox"
              checked={profile.seniorities.includes(level)}
              onChange={(e) =>
                setProfile({
                  ...profile,
                  seniorities: e.target.checked ? [...profile.seniorities, level] : profile.seniorities.filter((s) => s !== level),
                })
              }
            />
            {level}
          </label>
        ))}
      </fieldset>
      <label className="check">
        <input type="checkbox" checked={profile.remoteOnly} onChange={(e) => setProfile({ ...profile, remoteOnly: e.target.checked })} />
        Remote only
      </label>
      <label>
        Locations you can work for (comma separated)
        <input value={profile.locations.join(', ')} onChange={(e) => setProfile({ ...profile, locations: list(e.target.value) })} />
      </label>
      <div className="row">
        <label>
          Salary floor (USD / year)
          <input
            type="number"
            min={0}
            value={profile.salaryFloorUsd ?? ''}
            onChange={(e) => setProfile({ ...profile, salaryFloorUsd: e.target.value ? Number(e.target.value) : null })}
          />
        </label>
        <label>
          Mark down when asking ≥ years
          <input
            type="number"
            min={1}
            max={40}
            value={profile.maxYearsAsked ?? ''}
            onChange={(e) => setProfile({ ...profile, maxYearsAsked: e.target.value ? Number(e.target.value) : null })}
          />
        </label>
        <label>
          Notify at score
          <input
            type="number"
            min={0}
            max={100}
            value={profile.notifyThreshold}
            onChange={(e) => setProfile({ ...profile, notifyThreshold: Number(e.target.value) })}
          />
        </label>
      </div>
      <label>
        Title words to avoid (comma separated)
        <input
          value={profile.excludedTitleWords.join(', ')}
          onChange={(e) => setProfile({ ...profile, excludedTitleWords: list(e.target.value) })}
        />
      </label>
      <div className="row">
        <button type="submit">Save and rescore</button>
        {message && <span role="status">{message}</span>}
      </div>
    </form>
  )
}
