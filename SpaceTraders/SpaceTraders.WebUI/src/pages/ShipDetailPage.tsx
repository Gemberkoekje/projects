import { useParams, Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { useState, useEffect, useMemo } from 'react'
import { apiFetch } from '@/lib/api-fetch'
import type {
  ShipDto,
  ShipTaskRecordDto,
  ActivityLogDto,
  ShipStatsResponse,
  ShipActivityDto,
  ShipAssignmentDto,
  OrchestratorGoalChainDto,
  ShipGoalHistoryDto,
} from '@/types'
import { ArrowLeft } from 'lucide-react'
import { cn } from '@/lib/utils'

function formatCredits(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatDuration(startIso: string, endIso: string | null) {
  const end = endIso ? new Date(endIso).getTime() : Date.now()
  const ms = end - new Date(startIso).getTime()
  const s = Math.floor(ms / 1_000)
  if (s < 60) return `${s}s`
  const m = Math.floor(s / 60)
  if (m < 60) return `${m}m ${s % 60}s`
  const h = Math.floor(m / 60)
  return `${h}h ${m % 60}m`
}

function ProgressBar({ value, max, label }: { value: number; max: number; label: string }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <div className="flex flex-col gap-1">
      <div className="flex justify-between text-xs text-muted-foreground">
        <span>{label}</span>
        <span className="tabular-nums">
          {value}/{max} ({pct}%)
        </span>
      </div>
      <div className="h-2 rounded-full bg-muted overflow-hidden">
        <div
          className={cn(
            'h-full rounded-full transition-all',
            pct > 75 ? 'bg-destructive' : 'bg-primary',
          )}
          style={{ width: `${pct}%` }}
        />
      </div>
    </div>
  )
}

function formatCountdown(iso: string) {
  const ms = new Date(iso).getTime() - Date.now()
  if (ms <= 0) return 'Arrived'
  const m = Math.floor(ms / 60_000)
  const s = Math.floor((ms % 60_000) / 1_000)
  if (m > 0) return `${m}m ${s}s`
  return `${s}s`
}

function Countdown({ iso }: { iso: string }) {
  const [label, setLabel] = useState(() => formatCountdown(iso))
  useEffect(() => {
    setLabel(formatCountdown(iso))
    const id = setInterval(() => setLabel(formatCountdown(iso)), 1_000)
    return () => clearInterval(id)
  }, [iso])
  return <span className="text-xs text-status-yellow tabular-nums">{label}</span>
}

export default function ShipDetailPage() {
  const { symbol } = useParams<{ symbol: string }>()

  const shipsQ = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
    refetchInterval: 15_000,
  })

  const timelineQ = useQuery<ShipTaskRecordDto[]>({
    queryKey: ['ship-timeline', symbol],
    queryFn: () => apiFetch(`/ships/${symbol}/timeline`),
    enabled: Boolean(symbol),
    refetchInterval: 30_000,
  })

  const statsQ = useQuery<ShipStatsResponse>({
    queryKey: ['ship-stats', symbol],
    queryFn: () => apiFetch(`/ships/${symbol}/stats`),
    enabled: Boolean(symbol),
    refetchInterval: 30_000,
  })

  const activityQ = useQuery<ActivityLogDto[]>({
    queryKey: ['activity', symbol],
    queryFn: () => apiFetch(`/status/activity?size=50&ship=${encodeURIComponent(symbol ?? '')}`),
    enabled: Boolean(symbol),
    refetchInterval: 30_000,
  })

  const shipActivityQ = useQuery<ShipActivityDto>({
    queryKey: ['fleet-activity', symbol],
    queryFn: () => apiFetch(`/fleet/activity/${encodeURIComponent(symbol ?? '')}`),
    enabled: Boolean(symbol),
    refetchInterval: 5_000,
  })

  const assignmentsQ = useQuery<ShipAssignmentDto[]>({
    queryKey: ['fleet-assignments'],
    queryFn: () => apiFetch('/fleet/assignments'),
    refetchInterval: 10_000,
  })

  const goalChainsQ = useQuery<OrchestratorGoalChainDto[]>({
    queryKey: ['fleet-goal-chains'],
    queryFn: () => apiFetch('/fleet/goal-chains'),
    refetchInterval: 10_000,
  })

  const goalHistoryQ = useQuery<ShipGoalHistoryDto[]>({
    queryKey: ['fleet-goal-history', symbol],
    queryFn: () => apiFetch(`/fleet/activity/${encodeURIComponent(symbol ?? '')}/history?limit=20`),
    enabled: Boolean(symbol),
    refetchInterval: 30_000,
  })

  const ship = shipsQ.data?.find(s => s.symbol === symbol)
  const timeline = timelineQ.data ?? []
  const summary = statsQ.data?.summary ?? []
  const activity = activityQ.data ?? []
  const shipActivity = shipActivityQ.data ?? null
  const shipAssignment = assignmentsQ.data?.find(a => a.shipSymbol === symbol) ?? null
  const servingChains = useMemo(
    () =>
      (goalChainsQ.data ?? []).filter(c =>
        c.resourceNeeds.some(n => n.assignedShips.includes(symbol ?? '')),
      ),
    [goalChainsQ.data, symbol],
  )
  const goalHistory = goalHistoryQ.data ?? []

  const income = summary
    .filter(s => s.totalAmount > 0)
    .reduce((acc, s) => acc + s.totalAmount, 0)
  const expenses = summary
    .filter(s => s.totalAmount < 0)
    .reduce((acc, s) => acc + Math.abs(s.totalAmount), 0)

  if (!symbol) {
    return (
      <div>
        <p className="text-muted-foreground">No ship symbol provided.</p>
      </div>
    )
  }

  if (shipsQ.isLoading) {
    return <p className="text-muted-foreground text-sm">Loading…</p>
  }

  if (!ship && !shipsQ.isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/fleet" className="inline-flex items-center gap-1 text-sm text-primary hover:underline">
          <ArrowLeft size={14} aria-hidden />
          Back to Fleet
        </Link>
        <p className="text-muted-foreground">Ship "{symbol}" not found.</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      {/* Header */}
      <div className="flex flex-col gap-2">
        <Link
          to="/fleet"
          className="inline-flex items-center gap-1 text-sm text-primary hover:underline w-fit"
        >
          <ArrowLeft size={14} aria-hidden />
          Back to Fleet
        </Link>
        <div className="flex items-center gap-3">
          <h1 className="text-2xl font-bold font-mono">{symbol}</h1>
          {ship?.isInTransit ? (
            <span className="rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
              In transit
            </span>
          ) : (
            <span className="rounded-full bg-muted text-muted-foreground px-2 py-0.5 text-xs font-medium">
              {ship?.status ?? '—'}
            </span>
          )}
        </div>
        {ship && (
          <p className="text-sm text-muted-foreground">
            {ship.systemSymbol ?? '—'}
            {ship.waypointSymbol ? ` · ${ship.waypointSymbol}` : ''}
            {ship.flightMode ? ` · ${ship.flightMode}` : ''}
          </p>
        )}
      </div>

      {/* Live Activity */}
      {shipActivity && (
        <section aria-label="Live activity">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Live Activity
          </h2>
          <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-2 max-w-2xl">
            <p className="text-sm leading-snug">{shipActivity.activityDescription}</p>
            {(shipActivity.estimatedArrival || shipActivity.cooldownExpiresAt) && (
              <div className="flex items-center gap-1 text-xs">
                <span className="text-muted-foreground">
                  {shipActivity.estimatedArrival ? 'ETA' : 'Cooldown'}:
                </span>
                <Countdown iso={(shipActivity.estimatedArrival ?? shipActivity.cooldownExpiresAt)!} />
              </div>
            )}
          </div>
        </section>
      )}

      {/* Assignment */}
      {shipAssignment && (
        <section aria-label="Assignment">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Assignment
          </h2>
          <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-1 max-w-2xl text-sm">
            <div className="flex items-center gap-2">
              <span className="inline-block rounded-full bg-primary/15 text-primary px-2 py-0.5 text-xs font-semibold uppercase tracking-wide">
                {shipAssignment.goalKind}
              </span>
              <span className="text-muted-foreground">{shipAssignment.goalDescription}</span>
            </div>
            <div className="flex flex-wrap gap-4 mt-1 text-xs text-muted-foreground">
              <span>
                Source: <span className="font-mono">{shipAssignment.sourceWaypoint ?? '—'}</span>
              </span>
              <span>
                Destination: <span className="font-mono">{shipAssignment.destinationWaypoint ?? '—'}</span>
              </span>
            </div>
            {shipAssignment.fleetGoalDescription && (
              <div className="mt-1 text-xs">
                Serving:{' '}
                <Link
                  to="/orchestration"
                  className="text-primary hover:underline"
                >
                  {shipAssignment.fleetGoalDescription}
                </Link>
              </div>
            )}
          </div>
        </section>
      )}

      {/* Serving goal chains */}
      {servingChains.length > 0 && (
        <section aria-label="Serving fleet goals">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Serving Fleet Goals
          </h2>
          <div className="flex flex-col gap-2 max-w-2xl">
            {servingChains.map(chain => (
              <div key={chain.fleetGoalId} className="rounded-lg border border-border bg-background p-4 flex flex-col gap-1 text-sm">
                <div className="flex items-center gap-2">
                  <span className="inline-block rounded-full bg-primary/15 text-primary px-2 py-0.5 text-xs font-semibold uppercase tracking-wide">
                    {chain.fleetGoalKind}
                  </span>
                  <span className="font-medium">{chain.fleetGoalDescription}</span>
                </div>
                {chain.resourceNeeds
                  .filter(n => n.assignedShips.includes(symbol ?? ''))
                  .map((need, idx) => {
                    const pct = need.unitsNeeded > 0
                      ? Math.round((need.unitsDelivered / need.unitsNeeded) * 100)
                      : 0
                    return (
                      <div key={idx} className="mt-1 flex flex-col gap-1 text-xs">
                        <div className="flex items-center justify-between gap-2">
                          <span className="rounded bg-muted px-1.5 py-0.5 font-mono font-semibold">
                            {need.tradeSymbol}
                          </span>
                          <span className="text-muted-foreground tabular-nums">
                            {need.unitsDelivered}/{need.unitsNeeded} ({pct}%)
                          </span>
                        </div>
                        <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                          <div
                            className="h-full rounded-full bg-primary transition-all"
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </div>
                    )
                  })}
              </div>
            ))}
          </div>
        </section>
      )}

      {/* Fuel & Cargo bars */}
      {ship && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 max-w-lg">
          <ProgressBar value={ship.fuelCurrent} max={ship.fuelCapacity} label="Fuel" />
          <ProgressBar value={ship.cargoCurrent} max={ship.cargoCapacity} label="Cargo" />
        </div>
      )}

      {/* Lifetime stats */}
      {summary.length > 0 && (
        <section aria-label="Lifetime stats">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Lifetime Stats
          </h2>
          <div className="grid grid-cols-2 gap-3 sm:grid-cols-4 max-w-2xl">
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Income</p>
              <p className="text-lg font-bold text-status-green">+{formatCredits(income)}</p>
            </div>
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Expenses</p>
              <p className="text-lg font-bold text-destructive">-{formatCredits(expenses)}</p>
            </div>
            <div className="rounded-lg border border-border bg-background p-3">
              <p className="text-xs text-muted-foreground">Net P&L</p>
              <p className={cn('text-lg font-bold', income - expenses >= 0 ? 'text-status-green' : 'text-destructive')}>
                {income - expenses >= 0 ? '+' : ''}
                {formatCredits(income - expenses)}
              </p>
            </div>
          </div>
          <div className="mt-3 overflow-x-auto rounded-lg border border-border max-w-2xl">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Category</th>
                  <th className="px-3 py-2 text-right">Amount</th>
                  <th className="px-3 py-2 text-right">Entries</th>
                </tr>
              </thead>
              <tbody>
                {summary.map(s => (
                  <tr key={s.category} className="border-b border-border last:border-0">
                    <td className="px-3 py-2">{s.category}</td>
                    <td
                      className={cn(
                        'px-3 py-2 text-right tabular-nums',
                        s.totalAmount >= 0 ? 'text-status-green' : 'text-destructive',
                      )}
                    >
                      {s.totalAmount >= 0 ? '+' : ''}
                      {formatCredits(s.totalAmount)}
                    </td>
                    <td className="px-3 py-2 text-right text-muted-foreground tabular-nums">
                      {s.entryCount}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Activity timeline */}
      <section aria-label="Activity timeline">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Recent Activity
        </h2>
        {activityQ.isLoading && (
          <p className="text-muted-foreground text-sm">Loading activity…</p>
        )}
        {activity.length === 0 && !activityQ.isLoading && (
          <p className="text-muted-foreground text-sm">No recent activity.</p>
        )}
        <ol className="flex flex-col gap-1">
          {activity.slice(0, 50).map(entry => (
            <li key={entry.id} className="flex gap-3 text-sm border-b border-border/50 py-1.5 last:border-0">
              <span className="text-muted-foreground text-xs tabular-nums shrink-0 pt-0.5">
                {formatTs(entry.timestamp)}
              </span>
              <span>
                <span className="font-medium">{entry.eventType}</span>
                {' — '}
                <span className="text-muted-foreground">{entry.message}</span>
              </span>
            </li>
          ))}
        </ol>
      </section>

      {/* Task timeline */}
      <section aria-label="Task timeline">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Task Timeline (last 7 days)
        </h2>
        {timelineQ.isLoading && (
          <p className="text-muted-foreground text-sm">Loading timeline…</p>
        )}
        {timeline.length === 0 && !timelineQ.isLoading && (
          <p className="text-muted-foreground text-sm">No task records for this period.</p>
        )}
        <ol className="flex flex-col gap-1">
          {timeline.slice(0, 100).map(rec => (
            <li
              key={rec.id}
              className="flex gap-3 text-sm border-b border-border/50 py-1.5 last:border-0"
            >
              <span className="text-muted-foreground text-xs tabular-nums shrink-0 pt-0.5">
                {formatTs(rec.startedAt)}
              </span>
              <div className="flex flex-col">
                <span className="font-medium">{rec.taskKind}</span>
                {rec.targetWaypoint && (
                  <span className="text-xs text-muted-foreground">{rec.targetWaypoint}</span>
                )}
              </div>
              <span className="ml-auto text-xs text-muted-foreground tabular-nums">
                {formatDuration(rec.startedAt, rec.endedAt)}
              </span>
            </li>
          ))}
        </ol>
      </section>

      {/* Goal history */}
      <section aria-label="Goal history">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Goal History
        </h2>
        {goalHistoryQ.isLoading && (
          <p className="text-muted-foreground text-sm">Loading goal history…</p>
        )}
        {goalHistory.length === 0 && !goalHistoryQ.isLoading && (
          <p className="text-muted-foreground text-sm">No goal history available.</p>
        )}
        <ol className="flex flex-col gap-1">
          {goalHistory.map(entry => {
            const completed = entry.outcome === 'Completed'
            return (
              <li
                key={entry.id}
                className="flex gap-3 text-sm border-b border-border/50 py-1.5 last:border-0 items-start"
              >
                <span className="text-muted-foreground text-xs tabular-nums shrink-0 pt-0.5">
                  {formatTs(entry.startedAt)}
                </span>
                <div className="flex flex-col gap-0.5 flex-1">
                  <div className="flex items-center gap-2">
                    <span
                      className={cn(
                        'rounded-full px-2 py-0.5 text-xs font-medium',
                        completed
                          ? 'bg-status-green/15 text-status-green'
                          : 'bg-destructive/15 text-destructive',
                      )}
                    >
                      {entry.outcome}
                    </span>
                    <span className="font-medium">{entry.goalKind}</span>
                  </div>
                  {entry.reason && (
                    <span className="text-xs text-muted-foreground">{entry.reason}</span>
                  )}
                </div>
                <span className="ml-auto text-xs text-muted-foreground tabular-nums shrink-0">
                  {formatDuration(entry.startedAt, entry.endedAt)}
                </span>
              </li>
            )
          })}
        </ol>
      </section>
    </div>
  )
}
