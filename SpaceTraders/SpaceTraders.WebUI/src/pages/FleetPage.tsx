import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipDto } from '@/types'
import { ChevronRight } from 'lucide-react'
import { cn } from '@/lib/utils'

function ProgressBar({ value, max }: { value: number; max: number }) {
  const pct = max > 0 ? Math.round((value / max) * 100) : 0
  return (
    <div className="flex items-center gap-2 text-xs">
      <div className="h-1.5 w-16 rounded-full bg-muted overflow-hidden">
        <div
          className={cn(
            'h-full rounded-full',
            pct > 75 ? 'bg-destructive' : 'bg-primary',
          )}
          style={{ width: `${pct}%` }}
        />
      </div>
      <span className="text-muted-foreground tabular-nums">
        {value}/{max}
      </span>
    </div>
  )
}

function NavStateBadge({ ship }: { ship: ShipDto }) {
  if (ship.isInTransit) {
    return (
      <span className="rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
        In transit
      </span>
    )
  }
  if (ship.status === 'DOCKED') {
    return (
      <span className="rounded-full bg-muted text-muted-foreground px-2 py-0.5 text-xs font-medium">
        Docked
      </span>
    )
  }
  return (
    <span className="rounded-full bg-status-green/15 text-status-green px-2 py-0.5 text-xs font-medium">
      {ship.status ?? 'Unknown'}
    </span>
  )
}

function formatEta(iso: string) {
  const ms = new Date(iso).getTime() - Date.now()
  if (ms <= 0) return 'Arriving'
  const m = Math.floor(ms / 60_000)
  const s = Math.floor((ms % 60_000) / 1_000)
  if (m > 0) return `ETA ${m}m ${s}s`
  return `ETA ${s}s`
}

export default function FleetPage() {
  const { data: ships = [], isLoading } = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
    refetchInterval: 15_000,
  })

  const [search, setSearch] = useState('')
  const [statusFilter, setStatusFilter] = useState<'all' | 'transit' | 'docked' | 'orbit'>('all')
  const [systemFilter, setSystemFilter] = useState('')

  const systems = [...new Set(ships.map(s => s.systemSymbol).filter(Boolean))] as string[]

  const filtered = ships.filter(s => {
    if (search && !s.symbol.toLowerCase().includes(search.toLowerCase())) return false
    if (statusFilter === 'transit' && !s.isInTransit) return false
    if (statusFilter === 'docked' && (s.isInTransit || s.status !== 'DOCKED')) return false
    if (statusFilter === 'orbit' && (s.isInTransit || s.status !== 'IN_ORBIT')) return false
    if (systemFilter && s.systemSymbol !== systemFilter) return false
    return true
  })

  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-2xl font-bold">Fleet</h1>

      {/* Filters */}
      <div className="flex flex-wrap gap-2 items-center">
        <input
          type="search"
          placeholder="Search ships…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          aria-label="Search ships"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        />
        <select
          value={statusFilter}
          onChange={e => setStatusFilter(e.target.value as typeof statusFilter)}
          aria-label="Filter by status"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        >
          <option value="all">All statuses</option>
          <option value="transit">In transit</option>
          <option value="docked">Docked</option>
          <option value="orbit">In orbit</option>
        </select>
        <select
          value={systemFilter}
          onChange={e => setSystemFilter(e.target.value)}
          aria-label="Filter by system"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        >
          <option value="">All systems</option>
          {systems.map(sys => (
            <option key={sys} value={sys}>
              {sys}
            </option>
          ))}
        </select>
        <span className="text-xs text-muted-foreground ml-auto">
          {filtered.length} of {ships.length} ship{ships.length !== 1 ? 's' : ''}
        </span>
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Table */}
      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <th className="px-4 py-2">Symbol</th>
              <th className="px-4 py-2">Location</th>
              <th className="px-4 py-2">Status</th>
              <th className="px-4 py-2">Fuel</th>
              <th className="px-4 py-2">Cargo</th>
              <th className="px-4 py-2 sr-only">Actions</th>
            </tr>
          </thead>
          <tbody>
            {filtered.length === 0 && !isLoading && (
              <tr>
                <td colSpan={6} className="px-4 py-8 text-center text-muted-foreground">
                  No ships match the current filters.
                </td>
              </tr>
            )}
            {filtered.map(ship => (
              <tr
                key={ship.symbol}
                className="border-b border-border last:border-0 hover:bg-accent/30 transition-colors"
              >
                <td className="px-4 py-3 font-mono font-medium">{ship.symbol}</td>
                <td className="px-4 py-3 text-muted-foreground">
                  <div>{ship.systemSymbol ?? '—'}</div>
                  {ship.waypointSymbol && (
                    <div className="text-xs">{ship.waypointSymbol}</div>
                  )}
                  {ship.isInTransit && ship.arrivesAt && (
                    <div className="text-xs text-status-yellow">
                      {formatEta(ship.arrivesAt)}
                    </div>
                  )}
                </td>
                <td className="px-4 py-3">
                  <NavStateBadge ship={ship} />
                </td>
                <td className="px-4 py-3">
                  <ProgressBar value={ship.fuelCurrent} max={ship.fuelCapacity} />
                </td>
                <td className="px-4 py-3">
                  <ProgressBar value={ship.cargoCurrent} max={ship.cargoCapacity} />
                </td>
                <td className="px-4 py-3">
                  <Link
                    to={`/fleet/${ship.symbol}`}
                    className="inline-flex items-center gap-1 text-xs text-primary hover:underline"
                    aria-label={`View details for ${ship.symbol}`}
                  >
                    Details
                    <ChevronRight size={12} aria-hidden />
                  </Link>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
