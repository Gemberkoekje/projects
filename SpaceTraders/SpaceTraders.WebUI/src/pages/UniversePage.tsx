import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { useNavigate } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { SystemDto, JumpConnectionDto, ShipDto } from '@/types'

// ─── Universe map ──────────────────────────────────────────────────────────────

/** Returns the CSS colour class for a system based on its visited state. */
function systemColor(system: SystemDto, isFrontier: boolean): string {
  if (system.isVisited) return '#4ade80'  // green  – visited
  if (isFrontier) return '#facc15'        // yellow – exploration frontier
  return '#6366f1'                        // indigo – known-only
}

interface MapViewBox {
  minX: number
  minY: number
  width: number
  height: number
}

function computeViewBox(systems: SystemDto[]): MapViewBox {
  if (systems.length === 0) return { minX: -100, minY: -100, width: 200, height: 200 }
  const xs = systems.map(s => s.x)
  const ys = systems.map(s => s.y)
  const pad = 50
  const minX = Math.min(...xs) - pad
  const maxX = Math.max(...xs) + pad
  const minY = Math.min(...ys) - pad
  const maxY = Math.max(...ys) + pad
  return { minX, minY, width: maxX - minX, height: maxY - minY }
}

interface UniverseMapProps {
  systems: SystemDto[]
  connections: JumpConnectionDto[]
  ships: ShipDto[]
  search: string
  onSystemClick: (symbol: string) => void
}

function UniverseMap({ systems, connections, ships, search, onSystemClick }: UniverseMapProps) {
  const vb = useMemo(() => computeViewBox(systems), [systems])

  // Build lookup: system symbol → SystemDto
  const systemMap = useMemo(
    () => new Map(systems.map(s => [s.symbol, s])),
    [systems],
  )

  // Frontier = un-visited systems adjacent to visited systems via jump gate
  const visitedSet = useMemo(
    () => new Set(systems.filter(s => s.isVisited).map(s => s.symbol)),
    [systems],
  )
  const frontierSet = useMemo(() => {
    const f = new Set<string>()
    for (const c of connections) {
      if (visitedSet.has(c.fromSystem) && !visitedSet.has(c.toSystem)) f.add(c.toSystem)
      if (visitedSet.has(c.toSystem) && !visitedSet.has(c.fromSystem)) f.add(c.fromSystem)
    }
    return f
  }, [connections, visitedSet])

  // Deduplicate connections for drawing (only draw each edge once)
  const uniqueEdges = useMemo(() => {
    const seen = new Set<string>()
    const edges: JumpConnectionDto[] = []
    for (const c of connections) {
      const key = [c.fromSystem, c.toSystem].sort().join('|')
      if (!seen.has(key)) {
        seen.add(key)
        edges.push(c)
      }
    }
    return edges
  }, [connections])

  // Ships grouped by system
  const shipsBySystem = useMemo(() => {
    const m = new Map<string, number>()
    for (const ship of ships) {
      if (!ship.systemSymbol) continue
      m.set(ship.systemSymbol, (m.get(ship.systemSymbol) ?? 0) + 1)
    }
    return m
  }, [ships])

  const lowerSearch = search.toLowerCase()
  const highlightedSymbol = lowerSearch
    ? systems.find(s => s.symbol.toLowerCase().includes(lowerSearch))?.symbol
    : undefined

  if (systems.length === 0) {
    return (
      <p className="text-sm text-muted-foreground mt-4">
        No systems in database yet.
      </p>
    )
  }

  const svgW = 800
  const svgH = 600

  const toSvgX = (x: number) => ((x - vb.minX) / vb.width) * svgW
  const toSvgY = (y: number) => svgH - ((y - vb.minY) / vb.height) * svgH

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${svgW} ${svgH}`}
      className="border border-border rounded-md bg-background overflow-visible"
      role="img"
      aria-label="Universe map"
    >
      {/* Jump-gate connection lines */}
      {uniqueEdges.map((edge, i) => {
        const from = systemMap.get(edge.fromSystem)
        const to = systemMap.get(edge.toSystem)
        if (!from || !to) return null
        return (
          <line
            key={i}
            x1={toSvgX(from.x)}
            y1={toSvgY(from.y)}
            x2={toSvgX(to.x)}
            y2={toSvgY(to.y)}
            stroke="#334155"
            strokeWidth={0.5}
            strokeOpacity={0.7}
          />
        )
      })}

      {/* System dots */}
      {systems.map(s => {
        const cx = toSvgX(s.x)
        const cy = toSvgY(s.y)
        const isFrontier = frontierSet.has(s.symbol)
        const color = systemColor(s, isFrontier)
        const isHighlighted = s.symbol === highlightedSymbol
        const r = isHighlighted ? 5 : 3
        return (
          <circle
            key={s.symbol}
            cx={cx}
            cy={cy}
            r={r}
            fill={color}
            stroke={isHighlighted ? '#fff' : 'none'}
            strokeWidth={isHighlighted ? 1.5 : 0}
            aria-label={s.symbol}
            style={{ cursor: 'pointer' }}
            onClick={() => onSystemClick(s.symbol)}
          >
            <title>{s.symbol} ({s.type}){s.isVisited ? ' — visited' : isFrontier ? ' — frontier' : ''}</title>
          </circle>
        )
      })}

      {/* Ship position dots */}
      {Array.from(shipsBySystem.entries()).map(([sym, count]) => {
        const system = systemMap.get(sym)
        if (!system) return null
        return (
          <circle
            key={`ship-${sym}`}
            cx={toSvgX(system.x) + 5}
            cy={toSvgY(system.y) - 5}
            r={3}
            fill="#f97316"
            aria-label={`${count} ship(s) at ${sym}`}
          >
            <title>{count} ship(s) at {sym}</title>
          </circle>
        )
      })}
    </svg>
  )
}

// ─── Legend ────────────────────────────────────────────────────────────────────

function Legend() {
  const items = [
    { color: '#4ade80', label: 'Visited' },
    { color: '#facc15', label: 'Frontier (adjacent to visited)' },
    { color: '#6366f1', label: 'Known only' },
    { color: '#f97316', label: 'Ships present' },
  ]
  return (
    <div className="flex flex-wrap gap-4 text-sm" role="region" aria-label="Universe map legend">
      {items.map(item => (
        <span key={item.label} className="flex items-center gap-1.5">
          <svg width="12" height="12" aria-hidden="true">
            <circle cx="6" cy="6" r="5" fill={item.color} />
          </svg>
          {item.label}
        </span>
      ))}
    </div>
  )
}

// ─── Page ──────────────────────────────────────────────────────────────────────

export default function UniversePage() {
  const [search, setSearch] = useState('')
  const navigate = useNavigate()

  const { data: systems, isLoading: loadingSystems } = useQuery<SystemDto[]>({
    queryKey: ['universe-systems'],
    queryFn: () => apiFetch('/universe/systems'),
  })

  const { data: connections } = useQuery<JumpConnectionDto[]>({
    queryKey: ['universe-jump-connections'],
    queryFn: () => apiFetch('/universe/jump-connections'),
  })

  const { data: ships } = useQuery<ShipDto[]>({
    queryKey: ['ships'],
    queryFn: () => apiFetch('/status/ships'),
  })

  const filteredSystems = useMemo(() => {
    if (!systems) return []
    if (!search) return systems
    const lower = search.toLowerCase()
    return systems.filter(s => s.symbol.toLowerCase().includes(lower))
  }, [systems, search])

  const systemCount = systems?.length ?? 0
  const visitedCount = systems?.filter(s => s.isVisited).length ?? 0

  if (loadingSystems) {
    return (
      <div>
        <h1 className="text-2xl font-bold mb-2">Universe</h1>
        <p className="text-muted-foreground">Loading…</p>
      </div>
    )
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between flex-wrap gap-2">
        <h1 className="text-2xl font-bold">Universe</h1>
        <p className="text-sm text-muted-foreground">
          {visitedCount} / {systemCount} systems visited
        </p>
      </div>

      <div className="flex items-center gap-2">
        <input
          type="text"
          placeholder="Search systems…"
          value={search}
          onChange={e => setSearch(e.target.value)}
          className="border border-input bg-background rounded-md px-3 py-1.5 text-sm w-64 focus:outline-none focus:ring-2 focus:ring-ring"
          aria-label="Search systems"
        />
        {search && (
          <span className="text-sm text-muted-foreground">
            {filteredSystems.length} result{filteredSystems.length !== 1 ? 's' : ''}
          </span>
        )}
      </div>

      <Legend />

      <UniverseMap
        systems={systems ?? []}
        connections={connections ?? []}
        ships={ships ?? []}
        search={search}
        onSystemClick={symbol => navigate(`/systems/${symbol}`)}
      />
    </div>
  )
}
