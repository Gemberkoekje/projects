import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, useParams } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type {
  SystemMapResponseDto,
  WaypointDto,
  WaypointTraitDto,
  JumpConnectionDto,
  ShipDto,
} from '@/types'

// ─── Waypoint shape helpers ───────────────────────────────────────────────────

function waypointRadius(type: string): number {
  switch (type) {
    case 'GAS_GIANT': return 14
    case 'PLANET': return 9
    case 'MOON': return 5
    case 'NEBULA': return 16
    case 'GRAVITY_WELL':
    case 'ARTIFICIAL_GRAVITY_WELL': return 11
    default: return 6
  }
}

function waypointColor(type: string): string {
  switch (type) {
    case 'GAS_GIANT': return '#a855f7'
    case 'PLANET': return '#6366f1'
    case 'MOON': return '#94a3b8'
    case 'JUMP_GATE': return '#f59e0b'
    case 'ORBITAL_STATION':
    case 'FUEL_STATION': return '#14b8a6'
    case 'ASTEROID':
    case 'ASTEROID_FIELD':
    case 'ENGINEERED_ASTEROID': return '#a16207'
    case 'DEBRIS_FIELD': return '#6b7280'
    case 'NEBULA': return '#ec4899'
    case 'GRAVITY_WELL':
    case 'ARTIFICIAL_GRAVITY_WELL': return '#7c3aed'
    default: return '#cbd5e1'
  }
}

/** Returns SVG element(s) for a waypoint symbol at the given center (cx, cy). */
function WaypointIcon({
  type,
  cx,
  cy,
  onClick,
  isSelected,
  hasShips,
  isActive,
  label,
}: {
  type: string
  cx: number
  cy: number
  onClick: () => void
  isSelected: boolean
  hasShips: boolean
  isActive: boolean
  label: string
}) {
  const color = waypointColor(type)
  const selectionColor = '#f8fafc'

  if (type === 'JUMP_GATE') {
    const half = 9
    const points = `${cx},${cy - half} ${cx + half},${cy} ${cx},${cy + half} ${cx - half},${cy}`
    return (
      <g onClick={onClick} style={{ cursor: 'pointer' }} role="button" aria-label={label}>
        {isActive && (
          <polygon
            points={`${cx},${cy - half - 5} ${cx + half + 5},${cy} ${cx},${cy + half + 5} ${cx - half - 5},${cy}`}
            fill="none"
            stroke="#4ade80"
            strokeWidth={1.5}
          />
        )}
        {hasShips && (
          <circle cx={cx + half + 3} cy={cy - half - 3} r={4} fill="#f97316" />
        )}
        <polygon points={points} fill={color} stroke={isSelected ? selectionColor : 'none'} strokeWidth={isSelected ? 2 : 0} />
      </g>
    )
  }

  if (type === 'ORBITAL_STATION' || type === 'FUEL_STATION') {
    const half = type === 'ORBITAL_STATION' ? 5 : 4
    return (
      <g onClick={onClick} style={{ cursor: 'pointer' }} role="button" aria-label={label}>
        {isActive && (
          <rect
            x={cx - half - 4}
            y={cy - half - 4}
            width={(half + 4) * 2}
            height={(half + 4) * 2}
            fill="none"
            stroke="#4ade80"
            strokeWidth={1.5}
          />
        )}
        {hasShips && (
          <circle cx={cx + half + 3} cy={cy - half - 3} r={4} fill="#f97316" />
        )}
        <rect
          x={cx - half}
          y={cy - half}
          width={half * 2}
          height={half * 2}
          fill={color}
          stroke={isSelected ? selectionColor : 'none'}
          strokeWidth={isSelected ? 2 : 0}
        />
      </g>
    )
  }

  const r = waypointRadius(type)
  return (
    <g onClick={onClick} style={{ cursor: 'pointer' }} role="button" aria-label={label}>
      {isActive && (
        <circle cx={cx} cy={cy} r={r + 5} fill="none" stroke="#4ade80" strokeWidth={1.5} />
      )}
      {hasShips && (
        <circle cx={cx + r + 2} cy={cy - r - 2} r={4} fill="#f97316" />
      )}
      <circle
        cx={cx}
        cy={cy}
        r={r}
        fill={color}
        stroke={isSelected ? selectionColor : 'none'}
        strokeWidth={isSelected ? 2 : 0}
      />
    </g>
  )
}

// ─── Trait parsing ────────────────────────────────────────────────────────────

function parseTraits(traitsJson: string | null): WaypointTraitDto[] {
  if (!traitsJson) return []
  try {
    return JSON.parse(traitsJson) as WaypointTraitDto[]
  } catch {
    return []
  }
}

function parseOrbitals(orbitalsJson: string | null): string[] {
  if (!orbitalsJson) return []
  try {
    const parsed = JSON.parse(orbitalsJson) as Array<string | { symbol: string }>
    return parsed.map(o => (typeof o === 'string' ? o : o.symbol))
  } catch {
    return []
  }
}

// ─── Side panel ───────────────────────────────────────────────────────────────

function WaypointPanel({
  waypoint,
  ships,
  onClose,
}: {
  waypoint: WaypointDto
  ships: ShipDto[]
  onClose: () => void
}) {
  const traits = parseTraits(waypoint.traitsJson)
  const shipsHere = ships.filter(s => s.waypointSymbol === waypoint.symbol)
  const lastScanned = new Date(waypoint.lastObservedAt)
  const ageMs = Date.now() - lastScanned.getTime()
  const ageMinutes = Math.round(ageMs / 60_000)
  const ageLabel =
    ageMinutes < 60
      ? `${ageMinutes}m ago`
      : `${Math.round(ageMinutes / 60)}h ago`

  return (
    <aside
      className="w-72 border-l border-border bg-card p-4 overflow-y-auto flex-shrink-0"
      aria-label="Waypoint detail panel"
    >
      <div className="flex items-start justify-between mb-3">
        <div>
          <h2 className="font-semibold text-sm">{waypoint.symbol}</h2>
          <p className="text-xs text-muted-foreground">{waypoint.type}</p>
        </div>
        <button
          onClick={onClose}
          className="text-muted-foreground hover:text-foreground text-lg leading-none"
          aria-label="Close waypoint panel"
        >
          ×
        </button>
      </div>

      <dl className="text-xs space-y-1 mb-3">
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Coordinates</dt>
          <dd>({waypoint.x}, {waypoint.y})</dd>
        </div>
        <div className="flex justify-between">
          <dt className="text-muted-foreground">Last scanned</dt>
          <dd>{ageLabel}</dd>
        </div>
        {waypoint.hasMarket && (
          <div className="flex justify-between">
            <dt className="text-muted-foreground">Facilities</dt>
            <dd className="flex gap-1">
              <span className="bg-emerald-100 text-emerald-800 dark:bg-emerald-900 dark:text-emerald-200 px-1 rounded text-xs">Market</span>
              {waypoint.hasShipyard && (
                <span className="bg-sky-100 text-sky-800 dark:bg-sky-900 dark:text-sky-200 px-1 rounded text-xs">Shipyard</span>
              )}
            </dd>
          </div>
        )}
        {!waypoint.hasMarket && waypoint.hasShipyard && (
          <div className="flex justify-between">
            <dt className="text-muted-foreground">Facilities</dt>
            <dd>
              <span className="bg-sky-100 text-sky-800 dark:bg-sky-900 dark:text-sky-200 px-1 rounded text-xs">Shipyard</span>
            </dd>
          </div>
        )}
        {waypoint.isUnderConstruction && (
          <div className="flex justify-between">
            <dt className="text-muted-foreground">Status</dt>
            <dd className="text-amber-500">Under construction</dd>
          </div>
        )}
      </dl>

      {traits.length > 0 && (
        <section aria-label="Waypoint traits" className="mb-3">
          <h3 className="text-xs font-medium mb-1 text-muted-foreground uppercase tracking-wide">
            Traits
          </h3>
          <div className="flex flex-wrap gap-1">
            {traits.map(t => (
              <span
                key={t.symbol}
                title={t.description}
                className="bg-muted text-muted-foreground text-xs px-1.5 py-0.5 rounded"
              >
                {t.symbol}
              </span>
            ))}
          </div>
        </section>
      )}

      {shipsHere.length > 0 && (
        <section aria-label="Ships at waypoint" className="mb-3">
          <h3 className="text-xs font-medium mb-1 text-muted-foreground uppercase tracking-wide">
            Ships here ({shipsHere.length})
          </h3>
          <ul className="space-y-1">
            {shipsHere.map(s => (
              <li key={s.symbol} className="text-xs flex justify-between">
                <Link to={`/fleet/${s.symbol}`} className="text-primary hover:underline">
                  {s.symbol}
                </Link>
                <span className="text-muted-foreground">{s.status}</span>
              </li>
            ))}
          </ul>
        </section>
      )}

      {waypoint.hasMarket && (
        <Link
          to={`/markets/waypoints/${waypoint.symbol}`}
          className="text-xs text-primary hover:underline block"
        >
          View market →
        </Link>
      )}
    </aside>
  )
}

// ─── System map SVG ───────────────────────────────────────────────────────────

const SVG_W = 640
const SVG_H = 480
const VIEWBOX_PADDING = 40
const JUMP_LABEL_BASE_OFFSET_X = 12
const JUMP_LABEL_INCREMENT_X = 2
const JUMP_LABEL_BASE_OFFSET_Y = 12
const JUMP_LABEL_INCREMENT_Y = 10

interface SystemMapSvgProps {
  waypoints: WaypointDto[]
  ships: ShipDto[]
  showJumpConnections: boolean
  jumpConnections: JumpConnectionDto[]
  systemSymbol: string
  selectedSymbol: string | null
  onSelect: (symbol: string | null) => void
}

function SystemMapSvg({
  waypoints,
  ships,
  showJumpConnections,
  jumpConnections,
  systemSymbol,
  selectedSymbol,
  onSelect,
}: SystemMapSvgProps) {
  const viewBox = useMemo(() => {
    if (waypoints.length === 0) return { minX: -100, minY: -100, w: 200, h: 200 }
    const xs = waypoints.map(wp => wp.x)
    const ys = waypoints.map(wp => wp.y)
    const pad = VIEWBOX_PADDING
    const minX = Math.min(...xs) - pad
    const maxX = Math.max(...xs) + pad
    const minY = Math.min(...ys) - pad
    const maxY = Math.max(...ys) + pad
    return { minX, minY, w: maxX - minX, h: maxY - minY }
  }, [waypoints])

  const toSvgX = (x: number) => ((x - viewBox.minX) / viewBox.w) * SVG_W
  const toSvgY = (y: number) => SVG_H - ((y - viewBox.minY) / viewBox.h) * SVG_H

  // Ships grouped by waypointSymbol
  const shipsByWaypoint = useMemo(() => {
    const m = new Map<string, ShipDto[]>()
    for (const ship of ships) {
      if (!ship.waypointSymbol) continue
      const list = m.get(ship.waypointSymbol) ?? []
      list.push(ship)
      m.set(ship.waypointSymbol, list)
    }
    return m
  }, [ships])

  // Active waypoints = those with docked/in-orbit ships
  const activeWaypointSymbols = useMemo(() => {
    const s = new Set<string>()
    for (const [wp, wpShips] of shipsByWaypoint.entries()) {
      if (wpShips.some(sh => !sh.isInTransit)) s.add(wp)
    }
    return s
  }, [shipsByWaypoint])

  // Jump-gate connections involving this system (from / to)
  const thisSystemJumpGate = waypoints.find(wp => wp.type === 'JUMP_GATE')
  const relatedConnections = useMemo(() => {
    if (!thisSystemJumpGate) return []
    return jumpConnections.filter(
      c => c.fromSystem === systemSymbol || c.toSystem === systemSymbol,
    )
  }, [jumpConnections, systemSymbol, thisSystemJumpGate])

  if (waypoints.length === 0) {
    return (
      <p className="text-sm text-muted-foreground mt-4">
        No waypoint data available for this system.
      </p>
    )
  }

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${SVG_W} ${SVG_H}`}
      className="border border-border rounded-md bg-background"
      role="img"
      aria-label={`System map for ${systemSymbol}`}
      onClick={e => {
        // Deselect when clicking on background
        if (e.target === e.currentTarget) onSelect(null)
      }}
    >
      {/* Orbital lines: draw a faint line from parent to orbital */}
      {waypoints
        .filter(wp => wp.parentSymbol)
        .map(wp => {
          const parent = waypoints.find(p => p.symbol === wp.parentSymbol)
          if (!parent) return null
          return (
            <line
              key={`orbit-${wp.symbol}`}
              x1={toSvgX(parent.x)}
              y1={toSvgY(parent.y)}
              x2={toSvgX(wp.x)}
              y2={toSvgY(wp.y)}
              stroke="#334155"
              strokeWidth={0.5}
              strokeDasharray="3 3"
            />
          )
        })}

      {/* Jump-gate connection symbols (if toggle enabled) */}
      {showJumpConnections &&
        relatedConnections.map((c, i) => {
          const otherSystem = c.fromSystem === systemSymbol ? c.toSystem : c.fromSystem
          if (!thisSystemJumpGate) return null
          const jx = toSvgX(thisSystemJumpGate.x)
          const jy = toSvgY(thisSystemJumpGate.y)
          return (
            <text
              key={i}
              x={jx + JUMP_LABEL_BASE_OFFSET_X + i * JUMP_LABEL_INCREMENT_X}
              y={jy - JUMP_LABEL_BASE_OFFSET_Y - i * JUMP_LABEL_INCREMENT_Y}
              fontSize={8}
              fill="#f59e0b"
              aria-label={`Jump connection to ${otherSystem}`}
            >
              → {otherSystem}
            </text>
          )
        })}

      {/* Waypoint icons */}
      {waypoints.map(wp => {
        const cx = toSvgX(wp.x)
        const cy = toSvgY(wp.y)
        const hasShips = (shipsByWaypoint.get(wp.symbol)?.length ?? 0) > 0
        const isActive = activeWaypointSymbols.has(wp.symbol)
        const isSelected = wp.symbol === selectedSymbol
        return (
          <WaypointIcon
            key={wp.symbol}
            type={wp.type}
            cx={cx}
            cy={cy}
            onClick={() => onSelect(isSelected ? null : wp.symbol)}
            isSelected={isSelected}
            hasShips={hasShips}
            isActive={isActive}
            label={wp.symbol}
          />
        )
      })}

      {/* Waypoint labels for larger bodies */}
      {waypoints
        .filter(wp =>
          ['PLANET', 'GAS_GIANT', 'MOON', 'JUMP_GATE', 'ORBITAL_STATION', 'FUEL_STATION'].includes(wp.type),
        )
        .map(wp => {
          const cx = toSvgX(wp.x)
          const cy = toSvgY(wp.y)
          const r = waypointRadius(wp.type)
          return (
            <text
              key={`label-${wp.symbol}`}
              x={cx}
              y={cy + r + 10}
              fontSize={7}
              textAnchor="middle"
              fill="#94a3b8"
              style={{ pointerEvents: 'none' }}
            >
              {wp.symbol.split('-').slice(-1)[0]}
            </text>
          )
        })}
    </svg>
  )
}

// ─── Legend ───────────────────────────────────────────────────────────────────

function SystemMapLegend() {
  const items = [
    { color: '#6366f1', label: 'Planet' },
    { color: '#a855f7', label: 'Gas Giant' },
    { color: '#94a3b8', label: 'Moon' },
    { color: '#f59e0b', label: 'Jump Gate', shape: 'diamond' as const },
    { color: '#14b8a6', label: 'Station', shape: 'square' as const },
    { color: '#a16207', label: 'Asteroid' },
    { color: '#4ade80', label: 'Active (ships present)', ring: true },
    { color: '#f97316', label: 'Ships here' },
  ]
  return (
    <div className="flex flex-wrap gap-3 text-xs" role="region" aria-label="System map legend">
      {items.map(item => (
        <span key={item.label} className="flex items-center gap-1">
          <svg width="12" height="12" aria-hidden="true">
            {item.ring ? (
              <circle cx="6" cy="6" r="5" fill="none" stroke={item.color} strokeWidth="1.5" />
            ) : item.shape === 'diamond' ? (
              <polygon points="6,1 11,6 6,11 1,6" fill={item.color} />
            ) : item.shape === 'square' ? (
              <rect x="1" y="1" width="10" height="10" fill={item.color} />
            ) : (
              <circle cx="6" cy="6" r="5" fill={item.color} />
            )}
          </svg>
          {item.label}
        </span>
      ))}
    </div>
  )
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function SystemMapPage() {
  const { symbol } = useParams<{ symbol: string }>()
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null)
  const [showJumpConnections, setShowJumpConnections] = useState(false)

  const { data: mapData, isLoading } = useQuery<SystemMapResponseDto>({
    queryKey: ['system-map', symbol],
    queryFn: () => apiFetch(`/universe/systems/${symbol}/map`),
    enabled: !!symbol,
  })

  const { data: ships } = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
  })

  const { data: jumpConnections } = useQuery<JumpConnectionDto[]>({
    queryKey: ['universe-jump-connections'],
    queryFn: () => apiFetch('/universe/jump-connections'),
  })

  const systemShips = useMemo(() => {
    if (!ships || !symbol) return []
    return ships.filter(s => s.systemSymbol === symbol)
  }, [ships, symbol])

  const selectedWaypoint = useMemo(() => {
    if (!selectedSymbol || !mapData) return null
    return mapData.waypoints.find(wp => wp.symbol === selectedSymbol) ?? null
  }, [selectedSymbol, mapData])

  const hasJumpGate = mapData?.waypoints.some(wp => wp.type === 'JUMP_GATE') ?? false

  if (isLoading) {
    return (
      <div>
        <nav className="text-sm text-muted-foreground mb-2" aria-label="Breadcrumb">
          <Link to="/universe" className="hover:underline">Universe</Link>
          {' → '}
          <span>{symbol}</span>
        </nav>
        <h1 className="text-2xl font-bold mb-2">{symbol}</h1>
        <p className="text-muted-foreground">Loading…</p>
      </div>
    )
  }

  if (!mapData) {
    return (
      <div>
        <nav className="text-sm text-muted-foreground mb-2" aria-label="Breadcrumb">
          <Link to="/universe" className="hover:underline">Universe</Link>
          {' → '}
          <span>{symbol}</span>
        </nav>
        <h1 className="text-2xl font-bold mb-2">{symbol}</h1>
        <p className="text-muted-foreground">System not found.</p>
      </div>
    )
  }

  const { system, waypoints } = mapData

  return (
    <div className="space-y-4">
      {/* Breadcrumb */}
      <nav className="text-sm text-muted-foreground" aria-label="Breadcrumb">
        <Link to="/universe" className="hover:underline">Universe</Link>
        {' → '}
        <span>{system.symbol}</span>
      </nav>

      {/* Header */}
      <div className="flex items-center justify-between flex-wrap gap-2">
        <h1 className="text-2xl font-bold">{system.symbol}</h1>
        <div className="flex items-center gap-3 text-sm text-muted-foreground">
          <span>{system.type}</span>
          <span>({system.x}, {system.y})</span>
          <span>{waypoints.length} waypoint{waypoints.length !== 1 ? 's' : ''}</span>
          {systemShips.length > 0 && (
            <span className="text-orange-500">{systemShips.length} ship{systemShips.length !== 1 ? 's' : ''} present</span>
          )}
        </div>
      </div>

      {/* Controls */}
      {hasJumpGate && (
        <button
          onClick={() => setShowJumpConnections(v => !v)}
          className="text-sm border border-border rounded px-2 py-1 hover:bg-muted"
          aria-pressed={showJumpConnections}
        >
          {showJumpConnections ? 'Hide' : 'Show'} jump connections
        </button>
      )}

      <SystemMapLegend />

      {/* Map + side panel */}
      <div className="flex gap-0 border border-border rounded-md overflow-hidden">
        <div className="flex-1 min-w-0">
          <SystemMapSvg
            waypoints={waypoints}
            ships={systemShips}
            showJumpConnections={showJumpConnections}
            jumpConnections={jumpConnections ?? []}
            systemSymbol={system.symbol}
            selectedSymbol={selectedSymbol}
            onSelect={setSelectedSymbol}
          />
        </div>

        {selectedWaypoint && (
          <WaypointPanel
            waypoint={selectedWaypoint}
            ships={systemShips}
            onClose={() => setSelectedSymbol(null)}
          />
        )}
      </div>

      {/* Waypoints table */}
      <section aria-label="Waypoints list">
        <h2 className="text-lg font-semibold mb-2">Waypoints</h2>
        <div className="overflow-x-auto">
          <table className="w-full text-sm border-collapse">
            <thead>
              <tr className="border-b border-border text-left text-muted-foreground">
                <th className="py-2 pr-4 font-medium">Symbol</th>
                <th className="py-2 pr-4 font-medium">Type</th>
                <th className="py-2 pr-4 font-medium">Coords</th>
                <th className="py-2 pr-4 font-medium">Facilities</th>
                <th className="py-2 pr-4 font-medium">Traits</th>
              </tr>
            </thead>
            <tbody>
              {waypoints.map(wp => {
                const traits = parseTraits(wp.traitsJson)
                const orbitals = parseOrbitals(wp.orbitalsJson)
                return (
                  <tr
                    key={wp.symbol}
                    className={`border-b border-border hover:bg-muted/50 cursor-pointer ${selectedSymbol === wp.symbol ? 'bg-muted' : ''}`}
                    onClick={() => setSelectedSymbol(wp.symbol === selectedSymbol ? null : wp.symbol)}
                  >
                    <td className="py-1.5 pr-4 font-mono">
                      <span>{wp.symbol}</span>
                      {wp.parentSymbol && (
                        <span className="ml-1 text-xs text-muted-foreground">(orbital)</span>
                      )}
                      {orbitals.length > 0 && (
                        <span className="ml-1 text-xs text-muted-foreground">
                          +{orbitals.length} orbital{orbitals.length !== 1 ? 's' : ''}
                        </span>
                      )}
                    </td>
                    <td className="py-1.5 pr-4 text-muted-foreground">{wp.type}</td>
                    <td className="py-1.5 pr-4 text-muted-foreground">({wp.x}, {wp.y})</td>
                    <td className="py-1.5 pr-4">
                      <span className="flex gap-1">
                        {wp.hasMarket && (
                          <Link
                            to={`/markets/waypoints/${wp.symbol}`}
                            onClick={e => e.stopPropagation()}
                            className="bg-emerald-100 text-emerald-800 dark:bg-emerald-900 dark:text-emerald-200 px-1 rounded text-xs hover:underline"
                          >
                            Market
                          </Link>
                        )}
                        {wp.hasShipyard && (
                          <span className="bg-sky-100 text-sky-800 dark:bg-sky-900 dark:text-sky-200 px-1 rounded text-xs">
                            Shipyard
                          </span>
                        )}
                        {wp.isUnderConstruction && (
                          <span className="bg-amber-100 text-amber-800 dark:bg-amber-900 dark:text-amber-200 px-1 rounded text-xs">
                            Construction
                          </span>
                        )}
                      </span>
                    </td>
                    <td className="py-1.5">
                      <div className="flex flex-wrap gap-1">
                        {traits.slice(0, 3).map(t => (
                          <span
                            key={t.symbol}
                            title={t.description}
                            className="bg-muted text-muted-foreground text-xs px-1 rounded"
                          >
                            {t.symbol}
                          </span>
                        ))}
                        {traits.length > 3 && (
                          <span className="text-xs text-muted-foreground">+{traits.length - 3}</span>
                        )}
                      </div>
                    </td>
                  </tr>
                )
              })}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  )
}
