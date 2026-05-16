import { Link, useParams } from 'react-router'
import { useQuery } from '@tanstack/react-query'
import { ArrowLeft } from 'lucide-react'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipDiagnosticsDto, ShipDto, WaypointDetailDto } from '@/types'
import { cn } from '@/lib/utils'

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

function formatCountdown(iso: string | null) {
  if (!iso) return '—'
  const ms = new Date(iso).getTime() - Date.now()
  if (ms <= 0) return 'Arrived'
  const m = Math.floor(ms / 60_000)
  const s = Math.floor((ms % 60_000) / 1_000)
  return m > 0 ? `${m}m ${s}s` : `${s}s`
}

function CheckBadge({ label, ok }: { label: string; ok: boolean }) {
  return (
    <span
      className={cn(
        'rounded-full px-2 py-0.5 text-xs font-medium',
        ok ? 'bg-status-green/15 text-status-green' : 'bg-destructive/15 text-destructive',
      )}
    >
      {label}: {ok ? 'Yes' : 'No'}
    </span>
  )
}

export default function ShipDetailPage() {
  const { symbol } = useParams<{ symbol: string }>()

  const shipsQ = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
    refetchInterval: 15_000,
  })

  const diagnosticsQ = useQuery<ShipDiagnosticsDto>({
    queryKey: ['ship-diagnostics', symbol],
    queryFn: () => apiFetch(`/status/ships/${encodeURIComponent(symbol ?? '')}/diagnostics`),
    enabled: Boolean(symbol),
    refetchInterval: 10_000,
  })

  const ship = shipsQ.data?.find(s => s.symbol === symbol)
  const diagnostics = diagnosticsQ.data ?? null

  const waypointDetailsQ = useQuery<WaypointDetailDto>({
    queryKey: ['waypoint-detail', ship?.waypointSymbol],
    queryFn: () => apiFetch(`/status/waypoints/${encodeURIComponent(ship?.waypointSymbol ?? '')}`),
    enabled: Boolean(ship?.waypointSymbol),
    staleTime: 30_000,
  })

  if (!symbol) {
    return <p className="text-sm text-muted-foreground">No ship selected.</p>
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-2">
        <Link to="/fleet" className="inline-flex items-center gap-1 text-sm text-primary hover:underline">
          <ArrowLeft size={14} aria-hidden />
          Back to Fleet
        </Link>
      </div>

      <div className="flex items-center gap-3">
        <h1 className="text-2xl font-bold font-mono">{symbol}</h1>
        {ship?.isInTransit ? (
          <span className="rounded-full bg-status-yellow/15 text-status-yellow px-2 py-0.5 text-xs font-medium">
            In transit ({formatCountdown(ship.arrivesAt)})
          </span>
        ) : (
          <span className="rounded-full bg-muted text-muted-foreground px-2 py-0.5 text-xs font-medium">
            {ship?.status ?? 'Unknown'}
          </span>
        )}
      </div>

      {shipsQ.isLoading && <p className="text-sm text-muted-foreground">Loading ship details…</p>}
      {shipsQ.isError && <p className="text-sm text-destructive">Failed to load ship details.</p>}

      {ship && (
        <section className="rounded-lg border border-border bg-background p-4 text-sm">
          <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
            <div>
              <span className="text-muted-foreground">System: </span>
              <span className="font-mono">{ship.systemSymbol ?? '—'}</span>
            </div>
            <div>
              <span className="text-muted-foreground">Waypoint: </span>
              {ship.waypointSymbol ? (
                <button
                  type="button"
                  onClick={() => waypointDetailsQ.refetch()}
                  className="font-mono text-primary hover:underline"
                >
                  {ship.waypointSymbol}
                </button>
              ) : (
                <span className="font-mono">—</span>
              )}
            </div>
            <div>
              <span className="text-muted-foreground">Flight mode: </span>
              <span className="font-mono">{ship.flightMode ?? '—'}</span>
            </div>
            <div>
              <span className="text-muted-foreground">Last synced: </span>
              <span>{formatTs(ship.lastSyncedAt)}</span>
            </div>
            <div>
              <span className="text-muted-foreground">Fuel: </span>
              <span className="tabular-nums">{ship.fuelCurrent}/{ship.fuelCapacity}</span>
            </div>
            <div>
              <span className="text-muted-foreground">Cargo: </span>
              <span className="tabular-nums">{ship.cargoCurrent}/{ship.cargoCapacity}</span>
            </div>
          </div>
        </section>
      )}

      {ship?.waypointSymbol && (
        <section aria-label="Waypoint details" className="rounded-lg border border-border bg-background p-4 text-sm">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Waypoint Details
          </h2>

          {waypointDetailsQ.isLoading && (
            <p className="text-sm text-muted-foreground">Loading waypoint details…</p>
          )}

          {waypointDetailsQ.isError && (
            <p className="text-sm text-destructive">Failed to load waypoint details.</p>
          )}

          {waypointDetailsQ.data && (
            <div className="space-y-3">
              <div className="grid grid-cols-1 gap-2 sm:grid-cols-2">
                <div>
                  <span className="text-muted-foreground">Type: </span>
                  <span className="font-mono">{waypointDetailsQ.data.type}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Coordinates: </span>
                  <span className="font-mono">({waypointDetailsQ.data.x}, {waypointDetailsQ.data.y})</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Market: </span>
                  <span>{waypointDetailsQ.data.hasMarket ? 'Yes' : 'No'}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Shipyard: </span>
                  <span>{waypointDetailsQ.data.hasShipyard ? 'Yes' : 'No'}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Under construction: </span>
                  <span>{waypointDetailsQ.data.isUnderConstruction ? 'Yes' : 'No'}</span>
                </div>
                <div>
                  <span className="text-muted-foreground">Last observed: </span>
                  <span>{formatTs(waypointDetailsQ.data.lastObservedAt)}</span>
                </div>
              </div>

              {waypointDetailsQ.data.extractableResources.length > 0 ? (
                <div>
                  <p className="text-muted-foreground mb-1">Extractable resources</p>
                  <div className="flex flex-wrap gap-1">
                    {waypointDetailsQ.data.extractableResources.map(resource => (
                      <span
                        key={resource}
                        className="rounded-full bg-muted px-2 py-0.5 text-[11px] font-mono text-muted-foreground"
                      >
                        {resource}
                      </span>
                    ))}
                  </div>
                </div>
              ) : (
                <p className="text-sm text-muted-foreground">No known extractable resources cached for this waypoint.</p>
              )}
            </div>
          )}
        </section>
      )}

      {diagnostics && (
        <section aria-label="Automation checks">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Automation Checks
          </h2>

          <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-3 max-w-4xl">
            <div className="rounded-md border border-border bg-muted/30 px-3 py-2 text-sm">
              <span className="text-muted-foreground">Contract miner selection: </span>
              <span
                className={cn(
                  'font-medium',
                  diagnostics.isEligibleForContractMinerSelection ? 'text-status-green' : 'text-destructive',
                )}
              >
                {diagnostics.contractMinerEligibilityReason}
              </span>
            </div>

            <div className="flex flex-wrap gap-2">
              <CheckBadge label="Mining capable" ok={diagnostics.hasMiningEquipment} />
              <CheckBadge label="Has cargo space" ok={diagnostics.hasCargoSpace} />
              <CheckBadge label="Has fuel capacity" ok={diagnostics.hasFuelCapacity} />
              <CheckBadge label="Occupied by assignment" ok={diagnostics.isOccupied} />
              <CheckBadge label="Eligible contract miner" ok={diagnostics.isEligibleForContractMinerSelection} />
            </div>

            <div className="grid grid-cols-1 gap-2 sm:grid-cols-2 text-sm">
              <div>
                <span className="text-muted-foreground">Assignment type: </span>
                <span className="font-mono">{diagnostics.activeAssignmentType ?? '—'}</span>
              </div>
              <div>
                <span className="text-muted-foreground">Assignment contract: </span>
                <span className="font-mono">{diagnostics.activeAssignmentContractId ?? '—'}</span>
              </div>
              <div>
                <span className="text-muted-foreground">Assignment source: </span>
                <span className="font-mono">{diagnostics.activeAssignmentSourceWaypoint ?? '—'}</span>
              </div>
              <div>
                <span className="text-muted-foreground">Assignment destination: </span>
                <span className="font-mono">{diagnostics.activeAssignmentDestinationWaypoint ?? '—'}</span>
              </div>
            </div>

            <div className="flex flex-wrap gap-1">
              {diagnostics.mountSymbols.map(mount => (
                <span
                  key={mount}
                  className="rounded-full bg-muted px-2 py-0.5 text-[11px] font-mono text-muted-foreground"
                >
                  {mount}
                </span>
              ))}
            </div>
          </div>
        </section>
      )}
    </div>
  )
}
