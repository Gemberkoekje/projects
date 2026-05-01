import { WifiOff } from 'lucide-react'
import { useSignalR } from '@/lib/signalr'

/**
 * Shown below the top nav when SignalR is not delivering live updates —
 * either the connection dropped or the server heartbeat has gone silent.
 */
export function LiveUpdatesBanner() {
  const { state, liveUpdatesPaused } = useSignalR()

  if (!liveUpdatesPaused) return null

  const message =
    state === 'reconnecting'
      ? 'Reconnecting — live updates paused'
      : state === 'disconnected'
        ? 'Disconnected — live updates paused'
        : 'Live updates paused — server heartbeat not received'

  return (
    <div
      role="status"
      aria-live="polite"
      className="flex items-center gap-2 bg-status-yellow/15 border-b border-status-yellow/30 px-4 py-2 text-sm text-foreground"
    >
      <WifiOff size={14} className="shrink-0 text-status-yellow" />
      <span>{message}</span>
    </div>
  )
}
