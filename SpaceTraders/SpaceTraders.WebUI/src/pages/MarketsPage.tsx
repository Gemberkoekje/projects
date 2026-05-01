import { useState, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Route, Routes, useParams } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import { cn } from '@/lib/utils'
import type { MarketWaypointDto, MarketPriceSampleDto, TradeOpportunityDto } from '@/types'

// ─── Helpers ──────────────────────────────────────────────────────────────────

function fmt(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

const SUPPLY_ORDER = ['SCARCE', 'LIMITED', 'MODERATE', 'HIGH', 'ABUNDANT']

function supplyColor(supply: string | null) {
  switch (supply) {
    case 'ABUNDANT':
      return 'text-status-green'
    case 'HIGH':
      return 'text-status-green/80'
    case 'MODERATE':
      return 'text-muted-foreground'
    case 'LIMITED':
      return 'text-yellow-500'
    case 'SCARCE':
      return 'text-destructive'
    default:
      return 'text-muted-foreground'
  }
}

// ─── SVG price-over-time chart ─────────────────────────────────────────────────

interface PriceSeries {
  label: string
  color: string
  points: { t: number; v: number }[]
}

function PriceChart({
  series,
  width = 600,
  height = 140,
  ariaLabel = 'Price over time',
}: {
  series: PriceSeries[]
  width?: number
  height?: number
  ariaLabel?: string
}) {
  const allPoints = series.flatMap(s => s.points)
  if (allPoints.length < 2) return <p className="text-sm text-muted-foreground">Not enough data.</p>

  const pad = { top: 8, right: 16, bottom: 20, left: 56 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom

  const tMin = Math.min(...allPoints.map(p => p.t))
  const tMax = Math.max(...allPoints.map(p => p.t))
  const vMin = Math.min(...allPoints.map(p => p.v))
  const vMax = Math.max(...allPoints.map(p => p.v))
  const vRange = vMax - vMin || 1
  const tRange = tMax - tMin || 1

  const sx = (t: number) => pad.left + ((t - tMin) / tRange) * innerW
  const sy = (v: number) => pad.top + innerH - ((v - vMin) / vRange) * innerH

  const ticks = [vMin, (vMin + vMax) / 2, vMax]

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${width} ${height}`}
      className="overflow-visible"
      role="img"
      aria-label={ariaLabel}
    >
      {ticks.map((v, i) => (
        <g key={i}>
          <line
            x1={pad.left}
            y1={sy(v)}
            x2={pad.left + innerW}
            y2={sy(v)}
            stroke="currentColor"
            strokeOpacity={0.1}
            strokeDasharray="4 3"
          />
          <text
            x={pad.left - 6}
            y={sy(v)}
            textAnchor="end"
            fontSize={10}
            fill="currentColor"
            fillOpacity={0.5}
            dominantBaseline="middle"
          >
            {fmt(v)}
          </text>
        </g>
      ))}
      {series.map(s => {
        if (s.points.length < 2) return null
        const d = s.points
          .map((p, i) => `${i === 0 ? 'M' : 'L'}${sx(p.t).toFixed(1)},${sy(p.v).toFixed(1)}`)
          .join(' ')
        return (
          <path
            key={s.label}
            d={d}
            fill="none"
            stroke={s.color}
            strokeWidth={2}
            strokeLinejoin="round"
            strokeLinecap="round"
          />
        )
      })}
    </svg>
  )
}

// ─── Supply step chart ──────────────────────────────────────────────────────────

function SupplyStepChart({
  samples,
  width = 600,
  height = 60,
}: {
  samples: MarketPriceSampleDto[]
  width?: number
  height?: number
}) {
  const withSupply = samples.filter(s => s.supply)
  if (withSupply.length < 2) return null

  const pad = { top: 4, right: 16, bottom: 16, left: 56 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom

  const tMin = new Date(withSupply[0].observedAt).getTime()
  const tMax = new Date(withSupply[withSupply.length - 1].observedAt).getTime()
  const tRange = tMax - tMin || 1

  const sx = (iso: string) =>
    pad.left + ((new Date(iso).getTime() - tMin) / tRange) * innerW

  const sy = (supply: string | null) => {
    const idx = SUPPLY_ORDER.indexOf(supply ?? '')
    const pct = idx < 0 ? 0.5 : idx / (SUPPLY_ORDER.length - 1)
    return pad.top + innerH - pct * innerH
  }

  const pathD = withSupply
    .map((s, i) => {
      const x = sx(s.observedAt)
      const y = sy(s.supply)
      if (i === 0) return `M${x.toFixed(1)},${y.toFixed(1)}`
      const prevX = sx(withSupply[i - 1].observedAt)
      return `H${prevX.toFixed(1)} V${y.toFixed(1)} H${x.toFixed(1)}`
    })
    .join(' ')

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${width} ${height}`}
      className="overflow-visible"
      role="img"
      aria-label="Supply level over time"
    >
      {SUPPLY_ORDER.map((label, i) => {
        const pct = i / (SUPPLY_ORDER.length - 1)
        const y = pad.top + innerH - pct * innerH
        return (
          <text
            key={label}
            x={pad.left - 6}
            y={y}
            textAnchor="end"
            fontSize={8}
            fill="currentColor"
            fillOpacity={0.45}
            dominantBaseline="middle"
          >
            {label}
          </text>
        )
      })}
      <path
        d={pathD}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinejoin="miter"
        strokeLinecap="square"
        className="text-primary"
      />
    </svg>
  )
}

// ─── Chart colours (module-level constant — safe to omit from useMemo deps) ───

/** Hex colours for the up-to-5 waypoint series in the price chart. */
const CHART_COLORS = ['#3b82f6', '#22c55e', '#f59e0b', '#ec4899', '#8b5cf6']

/** Returns a semi-transparent (60 %) version of a #rrggbb hex colour. */
function withAlpha(hex: string, alpha: number): string {
  const r = parseInt(hex.slice(1, 3), 16)
  const g = parseInt(hex.slice(3, 5), 16)
  const b = parseInt(hex.slice(5, 7), 16)
  return `rgba(${r},${g},${b},${alpha})`
}

// ─── Good Detail page (`/markets/goods/:symbol`) ──────────────────────────────

function GoodDetailPage() {
  const { symbol } = useParams<{ symbol: string }>()

  const pricesQ = useQuery<MarketPriceSampleDto[]>({
    queryKey: ['market-good-prices', symbol],
    queryFn: () => apiFetch(`/markets/goods/${symbol}/prices`),
    staleTime: 120_000,
  })

  const bestRoutesQ = useQuery<TradeOpportunityDto | null>({
    queryKey: ['market-best-routes', symbol],
    queryFn: () =>
      apiFetch<TradeOpportunityDto>(`/markets/best-routes?cargoCapacity=100`).catch(() => null),
    staleTime: 120_000,
  })

  const samples = pricesQ.data ?? []

  // Group samples by waypoint to build series
  const waypointSeries = useMemo(() => {
    const map = new Map<string, MarketPriceSampleDto[]>()
    for (const s of samples) {
      const arr = map.get(s.waypointSymbol) ?? []
      arr.push(s)
      map.set(s.waypointSymbol, arr)
    }
    return map
  }, [samples])

  // Build buy-price and sell-price series (top 5 waypoints by data count)
  const topWaypoints = useMemo(() => {
    return Array.from(waypointSeries.entries())
      .sort((a, b) => b[1].length - a[1].length)
      .slice(0, 5)
      .map(([wp]) => wp)
  }, [waypointSeries])

  const priceSeries: PriceSeries[] = useMemo(() => {
    const result: PriceSeries[] = []
    topWaypoints.forEach((wp, i) => {
      const pts = (waypointSeries.get(wp) ?? []).map(s => ({
        t: new Date(s.observedAt).getTime(),
        v: s.purchasePrice,
      }))
      result.push({ label: `${wp} (buy)`, color: CHART_COLORS[i % CHART_COLORS.length], points: pts })
    })
    return result
  }, [topWaypoints, waypointSeries])

  const sellSeries: PriceSeries[] = useMemo(() => {
    const result: PriceSeries[] = []
    topWaypoints.forEach((wp, i) => {
      const pts = (waypointSeries.get(wp) ?? []).map(s => ({
        t: new Date(s.observedAt).getTime(),
        v: s.sellPrice,
      }))
      result.push({
        label: `${wp} (sell)`,
        // 60 % opacity variant of the same colour for the sell series
        color: withAlpha(CHART_COLORS[i % CHART_COLORS.length], 0.6),
        points: pts,
      })
    })
    return result
  }, [topWaypoints, waypointSeries])

  // Best buy→sell pairs: latest sample per waypoint, sorted by margin
  const bestPairs = useMemo(() => {
    const latest = new Map<string, MarketPriceSampleDto>()
    for (const s of samples) {
      const existing = latest.get(s.waypointSymbol)
      if (!existing || new Date(s.observedAt) > new Date(existing.observedAt)) {
        latest.set(s.waypointSymbol, s)
      }
    }
    const wps = Array.from(latest.values())
    const pairs: {
      buyWp: string
      sellWp: string
      buyPrice: number
      sellPrice: number
      margin: number
    }[] = []
    for (const buy of wps) {
      for (const sell of wps) {
        if (buy.waypointSymbol === sell.waypointSymbol) continue
        const margin = sell.sellPrice - buy.purchasePrice
        if (margin > 0) {
          pairs.push({
            buyWp: buy.waypointSymbol,
            sellWp: sell.waypointSymbol,
            buyPrice: buy.purchasePrice,
            sellPrice: sell.sellPrice,
            margin,
          })
        }
      }
    }
    return pairs.sort((a, b) => b.margin - a.margin).slice(0, 10)
  }, [samples])

  const supplySamples = useMemo(() => {
    if (topWaypoints.length === 0) return []
    return waypointSeries.get(topWaypoints[0]) ?? []
  }, [topWaypoints, waypointSeries])

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/markets" className="text-sm text-muted-foreground hover:underline">
          ← Markets
        </Link>
        <h1 className="text-2xl font-bold">{symbol}</h1>
      </div>

      {pricesQ.isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {samples.length > 0 && (
        <>
          {/* Price over time */}
          <section aria-label="Price over time">
            <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
              Price Over Time
            </h2>
            <div className="rounded-lg border border-border bg-background p-4">
              <PriceChart
                series={[...priceSeries, ...sellSeries]}
                ariaLabel={`${symbol} price over time`}
              />
              {/* Legend */}
              <div className="mt-2 flex flex-wrap gap-3">
                {topWaypoints.map((wp, i) => (
                  <span key={wp} className="flex items-center gap-1 text-xs text-muted-foreground">
                    <span
                      className="inline-block h-2 w-4 rounded-sm"
                      style={{ background: CHART_COLORS[i % CHART_COLORS.length] }}
                    />
                    {wp}
                  </span>
                ))}
              </div>
            </div>
          </section>

          {/* Supply step chart */}
          {supplySamples.some(s => s.supply) && (
            <section aria-label="Supply level over time">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Supply Level ({topWaypoints[0]})
              </h2>
              <div className="rounded-lg border border-border bg-background p-4">
                <SupplyStepChart samples={supplySamples} />
              </div>
            </section>
          )}

          {/* Best buy → sell pairs */}
          {bestPairs.length > 0 && (
            <section aria-label="Best buy-sell pairs">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Best Buy → Sell Pairs
              </h2>
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-3 py-2">Buy at</th>
                      <th className="px-3 py-2 text-right">Buy price</th>
                      <th className="px-3 py-2">Sell at</th>
                      <th className="px-3 py-2 text-right">Sell price</th>
                      <th className="px-3 py-2 text-right">Margin/unit</th>
                    </tr>
                  </thead>
                  <tbody>
                    {bestPairs.map((pair, idx) => (
                      <tr key={idx} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono">
                          <Link
                            to={`/markets/waypoints/${pair.buyWp}`}
                            className="hover:underline"
                          >
                            {pair.buyWp}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {fmt(pair.buyPrice)}
                        </td>
                        <td className="px-3 py-2 font-mono">
                          <Link
                            to={`/markets/waypoints/${pair.sellWp}`}
                            className="hover:underline"
                          >
                            {pair.sellWp}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {fmt(pair.sellPrice)}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums font-medium text-status-green">
                          +{fmt(pair.margin)}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}

      {!pricesQ.isLoading && samples.length === 0 && (
        <p className="text-muted-foreground text-sm">No price history found for {symbol}.</p>
      )}

      {/* Best route card if available */}
      {bestRoutesQ.data && (
        <section aria-label="Best route">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Best Current Route
          </h2>
          <div className="rounded-lg border border-border bg-background p-4 text-sm">
            <p>
              <span className="font-mono">{bestRoutesQ.data.buyWaypoint}</span>
              {' → '}
              <span className="font-mono">{bestRoutesQ.data.sellWaypoint}</span>
            </p>
            <p className="text-status-green">
              +{fmt(bestRoutesQ.data.profitPerUnit)} / unit
            </p>
          </div>
        </section>
      )}
    </div>
  )
}

// ─── Waypoint Detail page (`/markets/waypoints/:symbol`) ────────────────────────

function WaypointDetailPage() {
  const { symbol } = useParams<{ symbol: string }>()

  const waypointQ = useQuery<MarketWaypointDto>({
    queryKey: ['market-waypoint', symbol],
    queryFn: () => apiFetch(`/markets/waypoints/${symbol}`),
    staleTime: 120_000,
  })

  const pricesQ = useQuery<MarketPriceSampleDto[]>({
    queryKey: ['market-waypoint-prices', symbol],
    queryFn: () => apiFetch(`/markets/waypoints/${symbol}/prices`),
    staleTime: 120_000,
  })

  const waypoint = waypointQ.data
  const samples = pricesQ.data ?? []

  // Latest price per good from samples
  const latestPrices = useMemo(() => {
    const map = new Map<string, MarketPriceSampleDto>()
    for (const s of samples) {
      const existing = map.get(s.goodSymbol)
      if (!existing || new Date(s.observedAt) > new Date(existing.observedAt)) {
        map.set(s.goodSymbol, s)
      }
    }
    return Array.from(map.values()).sort((a, b) => a.goodSymbol.localeCompare(b.goodSymbol))
  }, [samples])

  const isLoading = waypointQ.isLoading || pricesQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/markets" className="text-sm text-muted-foreground hover:underline">
          ← Markets
        </Link>
        <h1 className="text-2xl font-bold">{symbol}</h1>
        {waypoint && (
          <span className="text-sm text-muted-foreground">{waypoint.systemSymbol}</span>
        )}
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {waypoint && (
        <>
          {/* Imports / Exports / Exchange */}
          <section aria-label="Imports exports exchanges">
            <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
              Trade Classification
            </h2>
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
              {[
                { label: 'Imports', items: waypoint.imports, cls: 'text-destructive' },
                { label: 'Exports', items: waypoint.exports, cls: 'text-status-green' },
                { label: 'Exchange', items: waypoint.exchange, cls: 'text-muted-foreground' },
              ].map(({ label, items, cls }) => (
                <div key={label} className="rounded-lg border border-border bg-background p-4">
                  <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground mb-2">
                    {label} ({items.length})
                  </p>
                  {items.length === 0 ? (
                    <p className="text-xs text-muted-foreground">None</p>
                  ) : (
                    <ul className="flex flex-col gap-1">
                      {items.map(g => (
                        <li key={g}>
                          <Link
                            to={`/markets/goods/${g}`}
                            className={cn('text-xs font-mono hover:underline', cls)}
                          >
                            {g}
                          </Link>
                        </li>
                      ))}
                    </ul>
                  )}
                </div>
              ))}
            </div>
          </section>

          {/* Current prices from trade goods snapshot */}
          {waypoint.tradeGoods.length > 0 && (
            <section aria-label="Current prices">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Current Prices
              </h2>
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-3 py-2">Good</th>
                      <th className="px-3 py-2">Type</th>
                      <th className="px-3 py-2 text-right">Buy</th>
                      <th className="px-3 py-2 text-right">Sell</th>
                      <th className="px-3 py-2 text-right">Volume</th>
                      <th className="px-3 py-2">Supply</th>
                    </tr>
                  </thead>
                  <tbody>
                    {waypoint.tradeGoods.map(g => (
                      <tr key={g.symbol} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono">
                          <Link
                            to={`/markets/goods/${g.symbol}`}
                            className="hover:underline"
                          >
                            {g.symbol}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-muted-foreground text-xs">{g.type}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{fmt(g.purchasePrice)}</td>
                        <td className="px-3 py-2 text-right tabular-nums">{fmt(g.sellPrice)}</td>
                        <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                          {g.tradeVolume}
                        </td>
                        <td className={cn('px-3 py-2 text-xs', supplyColor(g.supply))}>
                          {g.supply}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {/* Recent transactions from price samples */}
          {latestPrices.length > 0 && waypoint.tradeGoods.length === 0 && (
            <section aria-label="Recent price samples">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Recent Prices (from history)
              </h2>
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-3 py-2">Good</th>
                      <th className="px-3 py-2 text-right">Buy</th>
                      <th className="px-3 py-2 text-right">Sell</th>
                      <th className="px-3 py-2">Supply</th>
                      <th className="px-3 py-2">Observed</th>
                    </tr>
                  </thead>
                  <tbody>
                    {latestPrices.map(s => (
                      <tr key={s.goodSymbol} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono">
                          <Link
                            to={`/markets/goods/${s.goodSymbol}`}
                            className="hover:underline"
                          >
                            {s.goodSymbol}
                          </Link>
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {fmt(s.purchasePrice)}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">{fmt(s.sellPrice)}</td>
                        <td className={cn('px-3 py-2 text-xs', supplyColor(s.supply))}>
                          {s.supply ?? '—'}
                        </td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {new Date(s.observedAt).toLocaleString()}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}
        </>
      )}

      {!isLoading && !waypoint && (
        <p className="text-muted-foreground text-sm">Waypoint "{symbol}" not found.</p>
      )}
    </div>
  )
}

// ─── Markets list (`/markets`) ────────────────────────────────────────────────

function MarketsListPage() {
  const [search, setSearch] = useState('')
  const [goodFilter, setGoodFilter] = useState('')

  const marketsQ = useQuery<MarketWaypointDto[]>({
    queryKey: ['markets'],
    queryFn: () => apiFetch('/markets/waypoints'),
    staleTime: 120_000,
  })

  const markets = marketsQ.data ?? []

  // All goods across all markets for the heatmap selector
  const allGoods = useMemo(() => {
    const set = new Set<string>()
    for (const m of markets) {
      for (const g of m.tradeGoods) set.add(g.symbol)
      for (const g of m.imports) set.add(g)
      for (const g of m.exports) set.add(g)
    }
    return Array.from(set).sort()
  }, [markets])

  const filtered = useMemo(() => {
    return markets.filter(m => {
      const matchSearch =
        !search ||
        m.waypointSymbol.toLowerCase().includes(search.toLowerCase()) ||
        m.systemSymbol.toLowerCase().includes(search.toLowerCase())
      const matchGood =
        !goodFilter ||
        m.tradeGoods.some(g => g.symbol === goodFilter) ||
        m.imports.includes(goodFilter) ||
        m.exports.includes(goodFilter) ||
        m.exchange.includes(goodFilter)
      return matchSearch && matchGood
    })
  }, [markets, search, goodFilter])

  // Heatmap: for a selected good, show buy/sell price per waypoint
  const heatmapRows = useMemo(() => {
    if (!goodFilter) return []
    return filtered
      .map(m => {
        const tg = m.tradeGoods.find(g => g.symbol === goodFilter)
        return tg ? { waypoint: m.waypointSymbol, system: m.systemSymbol, tg } : null
      })
      .filter((r): r is NonNullable<typeof r> => r !== null)
      .sort((a, b) => a.tg.purchasePrice - b.tg.purchasePrice)
  }, [filtered, goodFilter])

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">Markets</h1>

      {/* Filters */}
      <div className="flex flex-wrap gap-3">
        <input
          type="search"
          placeholder="Search waypoint or system…"
          aria-label="Search markets"
          className="rounded border border-border bg-background px-3 py-1.5 text-sm w-64"
          value={search}
          onChange={e => setSearch(e.target.value)}
        />
        <select
          aria-label="Filter by good"
          className="rounded border border-border bg-background px-2 py-1.5 text-sm"
          value={goodFilter}
          onChange={e => setGoodFilter(e.target.value)}
        >
          <option value="">All goods</option>
          {allGoods.map(g => (
            <option key={g} value={g}>
              {g}
            </option>
          ))}
        </select>
        {goodFilter && (
          <button
            className="text-xs text-muted-foreground underline"
            onClick={() => setGoodFilter('')}
          >
            Clear filter
          </button>
        )}
      </div>

      {marketsQ.isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Heatmap for selected good */}
      {goodFilter && heatmapRows.length > 0 && (
        <section aria-label={`Price heatmap for ${goodFilter}`}>
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Price Heatmap — {goodFilter}
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Waypoint</th>
                  <th className="px-3 py-2">System</th>
                  <th className="px-3 py-2 text-right">Buy</th>
                  <th className="px-3 py-2 text-right">Sell</th>
                  <th className="px-3 py-2">Supply</th>
                </tr>
              </thead>
              <tbody>
                {heatmapRows.map(row => (
                  <tr key={row.waypoint} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-mono">
                      <Link
                        to={`/markets/waypoints/${row.waypoint}`}
                        className="hover:underline"
                      >
                        {row.waypoint}
                      </Link>
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{row.system}</td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {fmt(row.tg.purchasePrice)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">{fmt(row.tg.sellPrice)}</td>
                    <td className={cn('px-3 py-2 text-xs', supplyColor(row.tg.supply))}>
                      {row.tg.supply}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Market list */}
      {filtered.length > 0 && (
        <section aria-label="Markets list">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Waypoints with Markets ({filtered.length})
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Waypoint</th>
                  <th className="px-3 py-2">System</th>
                  <th className="px-3 py-2 text-right">Goods</th>
                  <th className="px-3 py-2">Imports</th>
                  <th className="px-3 py-2">Exports</th>
                </tr>
              </thead>
              <tbody>
                {filtered.map(m => (
                  <tr key={m.waypointSymbol} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-mono">
                      <Link
                        to={`/markets/waypoints/${m.waypointSymbol}`}
                        className="hover:underline"
                      >
                        {m.waypointSymbol}
                      </Link>
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{m.systemSymbol}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                      {m.tradeGoods.length}
                    </td>
                    <td className="px-3 py-2 text-xs text-destructive">
                      {m.imports.slice(0, 3).join(', ')}
                      {m.imports.length > 3 && ` +${m.imports.length - 3}`}
                    </td>
                    <td className="px-3 py-2 text-xs text-status-green">
                      {m.exports.slice(0, 3).join(', ')}
                      {m.exports.length > 3 && ` +${m.exports.length - 3}`}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {!marketsQ.isLoading && filtered.length === 0 && markets.length > 0 && (
        <p className="text-muted-foreground text-sm">No markets match the current filters.</p>
      )}

      {!marketsQ.isLoading && markets.length === 0 && (
        <p className="text-muted-foreground text-sm">No market data available yet.</p>
      )}
    </div>
  )
}

// ─── Root MarketsPage (owns sub-routing) ──────────────────────────────────────

export default function MarketsPage() {
  return (
    <Routes>
      <Route path="/" element={<MarketsListPage />} />
      <Route path="/goods/:symbol" element={<GoodDetailPage />} />
      <Route path="/waypoints/:symbol" element={<WaypointDetailPage />} />
    </Routes>
  )
}
