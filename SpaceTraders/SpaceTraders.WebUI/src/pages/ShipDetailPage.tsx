import { useParams, Link } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipDto, ShipTaskRecordDto, ActivityLogDto, ShipStatsResponse } from '@/types'
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

  const ship = shipsQ.data?.find(s => s.symbol === symbol)
  const timeline = timelineQ.data ?? []
  const summary = statsQ.data?.summary ?? []
  const activity = activityQ.data ?? []

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
    </div>
  )
}
