/**
 * ActivityPanel – Phase 17d
 * Shows real-time ship activity cards, refreshed every 5 seconds.
 * Each card has status badge, activity description, cargo/fuel bars, and a countdown timer.
 */
import { useState, useEffect } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipActivityDto } from '@/types'
import { cn } from '@/lib/utils'

function StatusBadge({ status }: { status: string }) {
  if (status === 'IN_TRANSIT') {
    return (
      <span className="rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
        In transit
      </span>
    )
  }
  if (status === 'DOCKED') {
    return (
      <span className="rounded-full bg-muted text-muted-foreground px-2 py-0.5 text-xs font-medium">
        Docked
      </span>
    )
  }
  return (
    <span className="rounded-full bg-status-green/15 text-status-green px-2 py-0.5 text-xs font-medium">
      {status || 'Unknown'}
    </span>
  )
}

function MiniBar({ value, max, warn }: { value: number; max: number; warn?: boolean }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="h-1.5 w-16 rounded-full bg-muted overflow-hidden">
        <div
          className={cn('h-full rounded-full', warn && pct > 75 ? 'bg-destructive' : 'bg-primary')}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-muted-foreground tabular-nums">
        {value}/{max}
      </span>
    </div>
  )
}

function Countdown({ iso }: { iso: string }) {
  const [label, setLabel] = useState(() => formatCountdown(iso))

  useEffect(() => {
    setLabel(formatCountdown(iso))
    const id = setInterval(() => {
      setLabel(formatCountdown(iso))
    }, 1_000)
    return () => clearInterval(id)
  }, [iso])

  return <span className="text-xs text-status-yellow tabular-nums">{label}</span>
}

function formatCountdown(iso: string) {
  const ms = new Date(iso).getTime() - Date.now()
  if (ms <= 0) return 'Arriving'
  const m = Math.floor(ms / 60_000)
  const s = Math.floor((ms % 60_000) / 1_000)
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

function ActivityCard({ ship }: { ship: ShipActivityDto }) {
  const navigate = useNavigate()
  const countdownTarget = ship.estimatedArrival ?? ship.cooldownExpiresAt ?? null

  return (
    <div
      role="button"
      tabIndex={0}
      aria-label={`View details for ${ship.shipSymbol}`}
      onClick={() => navigate(`/fleet/${ship.shipSymbol}`)}
      onKeyDown={e => {
        if (e.key === 'Enter' || e.key === ' ') navigate(`/fleet/${ship.shipSymbol}`)
      }}
      className="rounded-lg border border-border bg-background p-4 flex flex-col gap-2 cursor-pointer hover:bg-accent/30 transition-colors focus:outline-none focus:ring-2 focus:ring-primary"
    >
      <div className="flex items-center justify-between gap-2">
        <span className="font-mono font-semibold text-sm">{ship.shipSymbol}</span>
        <StatusBadge status={ship.localStatus} />
      </div>

      <p className="text-sm text-muted-foreground leading-snug">{ship.activityDescription}</p>

      <div className="flex flex-col gap-1">
        <div className="flex items-center gap-2">
          <span className="text-xs w-10 text-muted-foreground shrink-0">Cargo</span>
          <MiniBar value={ship.cargoUsed} max={ship.cargoCapacity} warn />
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs w-10 text-muted-foreground shrink-0">Fuel</span>
          <MiniBar value={ship.fuelCurrent} max={ship.fuelCapacity} />
        </div>
      </div>

      {countdownTarget && (
        <div className="flex items-center gap-1 text-xs">
          <span className="text-muted-foreground">
            {ship.estimatedArrival ? 'ETA' : 'Cooldown'}:
          </span>
          <Countdown iso={countdownTarget} />
        </div>
      )}
    </div>
  )
}

export default function ActivityPanel() {
  const { data: ships = [], isLoading } = useQuery<ShipActivityDto[]>({
    queryKey: ['fleet-activity'],
    queryFn: () => apiFetch('/fleet/activity'),
    refetchInterval: 5_000,
  })

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (ships.length === 0) {
    return <p className="text-sm text-muted-foreground">No ships reporting activity.</p>
  }

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
      {ships.map(ship => (
        <ActivityCard key={ship.shipSymbol} ship={ship} />
      ))}
    </div>
  )
}
