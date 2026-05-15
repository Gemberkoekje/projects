import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type { ActivityLogDto, ShipDto } from '@/types'

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export default function ActivityPage() {
  const [shipFilter, setShipFilter] = useState('')
  const [typeFilter, setTypeFilter] = useState('')
  const [page, setPage] = useState(1)
  const pageSize = 50

  const shipsQ = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
  })

  const activityQ = useQuery<ActivityLogDto[]>({
    queryKey: ['activity', page, shipFilter],
    queryFn: () => {
      const params = new URLSearchParams({
        page: String(page),
        size: String(pageSize),
      })
      if (shipFilter) params.set('ship', shipFilter)
      return apiFetch(`/status/activity?${params.toString()}`)
    },
    refetchInterval: 15_000,
  })

  const ships = shipsQ.data ?? []
  const activity = activityQ.data ?? []

  const eventTypes = [...new Set(activity.map(a => a.eventType))].sort()

  const filtered = typeFilter
    ? activity.filter(a => a.eventType === typeFilter)
    : activity

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">Activity</h1>

      {/* Filters */}
      <div className="flex flex-wrap gap-2 items-center">
        <select
          value={shipFilter}
          onChange={e => { setShipFilter(e.target.value); setPage(1) }}
          aria-label="Filter by ship"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        >
          <option value="">All ships</option>
          {ships.map(s => (
            <option key={s.symbol} value={s.symbol}>
              {s.symbol}
            </option>
          ))}
        </select>
        <select
          value={typeFilter}
          onChange={e => setTypeFilter(e.target.value)}
          aria-label="Filter by event type"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        >
          <option value="">All event types</option>
          {eventTypes.map(t => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </select>
        <span className="text-xs text-muted-foreground ml-auto">
          {filtered.length} event{filtered.length !== 1 ? 's' : ''}
        </span>
      </div>

      {activityQ.isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Event feed */}
      <div className="rounded-lg border border-border overflow-hidden">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="px-4 py-2">Time</th>
              <th className="px-4 py-2">Ship</th>
              <th className="px-4 py-2">Type</th>
              <th className="px-4 py-2">Message</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && !activityQ.isLoading && (
              <tr>
                <td colSpan={4} className="px-4 py-8 text-center text-muted-foreground">
                  No activity to display.
                </td>
              </tr>
            )}
            {filtered.map(entry => (
              <tr
                key={entry.id}
                className="border-b border-border last:border-0 hover:bg-accent/30 transition-colors"
              >
                <td className="px-4 py-2 text-muted-foreground text-xs tabular-nums whitespace-nowrap">
                  {formatTs(entry.timestamp)}
                </td>
                <td className="px-4 py-2 font-mono text-xs whitespace-nowrap">{entry.shipSymbol}</td>
                <td className="px-4 py-2 text-xs font-medium whitespace-nowrap">{entry.eventType}</td>
                <td className="px-4 py-2 text-muted-foreground">{entry.message}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      <div className="flex items-center gap-2 text-sm">
        <button
          onClick={() => setPage(p => Math.max(1, p - 1))}
          disabled={page === 1}
          className="rounded-md border border-border px-3 py-1.5 text-sm disabled:opacity-40 hover:bg-accent transition-colors"
        >
          Previous
        </button>
        <span className="text-muted-foreground">Page {page}</span>
        <button
          onClick={() => setPage(p => p + 1)}
          disabled={(activityQ.data?.length ?? 0) < pageSize}
          className="rounded-md border border-border px-3 py-1.5 text-sm disabled:opacity-40 hover:bg-accent transition-colors"
        >
          Next
        </button>
      </div>
    </div>
  )
}
