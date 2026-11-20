import { HubConnectionBuilder, HubConnectionState, LogLevel } from '@microsoft/signalr'
import { useEffect, useState } from 'react'
import type { LiveMatch } from './api'

export type LiveStatus = 'connecting' | 'live' | 'reconnecting' | 'offline'

/** Subscribes to /hubs/matches; newest match first, capped so a long session stays light. */
export function useLiveMatches(limit = 50): { matches: LiveMatch[]; status: LiveStatus } {
  const [matches, setMatches] = useState<LiveMatch[]>([])
  const [status, setStatus] = useState<LiveStatus>('connecting')

  useEffect(() => {
    const connection = new HubConnectionBuilder()
      .withUrl('/hubs/matches')
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build()

    connection.on('match', (match: LiveMatch) =>
      setMatches((current) => [match, ...current.filter((m) => m.postingId !== match.postingId)].slice(0, limit)),
    )
    connection.onreconnecting(() => setStatus('reconnecting'))
    connection.onreconnected(() => setStatus('live'))
    connection.onclose(() => setStatus('offline'))

    let cancelled = false
    connection
      .start()
      .then(() => !cancelled && setStatus('live'))
      .catch(() => !cancelled && setStatus('offline'))

    return () => {
      cancelled = true
      if (connection.state !== HubConnectionState.Disconnected) void connection.stop()
    }
  }, [limit])

  return { matches, status }
}
