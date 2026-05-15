import { useState, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import { cn } from '@/lib/utils'
import type {
  MarketWaypointDto,
  MarketFreshnessDto,
  ShipyardWaypointDto,
  ShipyardFreshnessDto
} from '@/types'

function fmt(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

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

export default function MarketsPage() {
  const [activeTab, setActiveTab] = useState<'markets' | 'shipyards'>('markets')
  const [searchMarket, setSearchMarket] = useState('')
  const [searchShipyard, setSearchShipyard] = useState('')
  const [goodFilter, setGoodFilter] = useState('')
  const [shipTypeFilter, setShipTypeFilter] = useState('')
  const [expandedMarketWaypoint, setExpandedMarketWaypoint] = useState('')
  const [expandedShipyardWaypoint, setExpandedShipyardWaypoint] = useState('')

  const marketsQ = useQuery<MarketWaypointDto[]>({
    queryKey: ['markets'],
    queryFn: () => apiFetch('/markets/waypoints'),
    staleTime: 120_000,
  })

  const marketFreshnessQ = useQuery<MarketFreshnessDto[]>({
    queryKey: ['markets-freshness'],
    queryFn: () => apiFetch('/markets/freshness'),
    staleTime: 120_000,
  })

  const shipyardsQ = useQuery<ShipyardWaypointDto[]>({
    queryKey: ['shipyards'],
    queryFn: () => apiFetch('/shipyards/waypoints'),
    staleTime: 120_000,
  })

  const shipyardFreshnessQ = useQuery<ShipyardFreshnessDto[]>({
    queryKey: ['shipyards-freshness'],
    queryFn: () => apiFetch('/shipyards/freshness'),
    staleTime: 120_000,
  })

  const marketFreshnessMap = useMemo(() => {
    const map = new Map<string, MarketFreshnessDto>()
    for (const f of marketFreshnessQ.data ?? []) map.set(f.waypointSymbol, f)
    return map
  }, [marketFreshnessQ.data])

  const shipyardFreshnessMap = useMemo(() => {
    const map = new Map<string, ShipyardFreshnessDto>()
    for (const f of shipyardFreshnessQ.data ?? []) map.set(f.waypointSymbol, f)
    return map
  }, [shipyardFreshnessQ.data])

  const markets = marketsQ.data ?? []
  const shipyards = shipyardsQ.data ?? []

  const allGoods = useMemo(() => {
    const set = new Set<string>()
    for (const m of markets) {
      for (const g of m.tradeGoods) set.add(g.symbol)
      for (const g of m.imports) set.add(g)
      for (const g of m.exports) set.add(g)
    }
    return Array.from(set).sort()
  }, [markets])

  const allShipTypes = useMemo(() => {
    const set = new Set<string>()
    for (const s of shipyards) {
      for (const type of s.shipTypes) set.add(type)
      for (const ship of s.ships) set.add(ship.type)
    }
    return Array.from(set).sort()
  }, [shipyards])

  const filteredMarkets = useMemo(() => {
    return markets.filter(m => {
      const matchSearch =
        !searchMarket ||
        m.waypointSymbol.toLowerCase().includes(searchMarket.toLowerCase()) ||
        m.systemSymbol.toLowerCase().includes(searchMarket.toLowerCase())
      const matchGood =
        !goodFilter ||
        m.tradeGoods.some(g => g.symbol === goodFilter) ||
        m.imports.includes(goodFilter) ||
        m.exports.includes(goodFilter) ||
        m.exchange.includes(goodFilter)
      return matchSearch && matchGood
    })
  }, [markets, searchMarket, goodFilter])

  const filteredShipyards = useMemo(() => {
    return shipyards.filter(s => {
      const matchSearch =
        !searchShipyard ||
        s.waypointSymbol.toLowerCase().includes(searchShipyard.toLowerCase()) ||
        s.systemSymbol.toLowerCase().includes(searchShipyard.toLowerCase())
      const matchShipType =
        !shipTypeFilter ||
        s.shipTypes.includes(shipTypeFilter) ||
        s.ships.some(ship => ship.type === shipTypeFilter)
      return matchSearch && matchShipType
    })
  }, [shipyards, searchShipyard, shipTypeFilter])

  const heatmapRows = useMemo(() => {
    if (!goodFilter) return []
    return filteredMarkets
      .map(m => {
        const tg = m.tradeGoods.find(g => g.symbol === goodFilter)
        return tg ? { waypoint: m.waypointSymbol, system: m.systemSymbol, tg } : null
      })
      .filter((r): r is NonNullable<typeof r> => r !== null)
      .sort((a, b) => a.tg.purchasePrice - b.tg.purchasePrice)
  }, [filteredMarkets, goodFilter])

  const shipPriceRows = useMemo(() => {
    if (!shipTypeFilter) return []
    return filteredShipyards
      .flatMap(s =>
        s.ships
          .filter(ship => ship.type === shipTypeFilter)
          .map(ship => ({
            waypoint: s.waypointSymbol,
            system: s.systemSymbol,
            ship,
          }))
      )
      .sort((a, b) => a.ship.purchasePrice - b.ship.purchasePrice)
  }, [filteredShipyards, shipTypeFilter])

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Markets & Shipyards</h1>
        <div className="flex gap-2">
          <button
            onClick={() => setActiveTab('markets')}
            className={cn(
              'px-4 py-2 rounded text-sm font-medium transition-colors',
              activeTab === 'markets'
                ? 'bg-primary text-primary-foreground'
                : 'bg-muted text-muted-foreground hover:bg-muted/80'
            )}
          >
            Markets
          </button>
          <button
            onClick={() => setActiveTab('shipyards')}
            className={cn(
              'px-4 py-2 rounded text-sm font-medium transition-colors',
              activeTab === 'shipyards'
                ? 'bg-primary text-primary-foreground'
                : 'bg-muted text-muted-foreground hover:bg-muted/80'
            )}
          >
            Shipyards
          </button>
        </div>
      </div>

      {activeTab === 'markets' && (
        <>
          <div className="flex flex-wrap gap-3">
            <input
              type="search"
              placeholder="Search waypoint or system…"
              aria-label="Search markets"
              className="rounded border border-border bg-background px-3 py-1.5 text-sm w-64"
              value={searchMarket}
              onChange={e => setSearchMarket(e.target.value)}
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

          {marketsQ.isLoading && <p className="text-muted-foreground text-sm">Loading markets…</p>}

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
                      <th className="px-3 py-2">Type</th>
                      <th className="px-3 py-2 text-right">Buy Price</th>
                      <th className="px-3 py-2 text-right">Sell Price</th>
                      <th className="px-3 py-2 text-right">Volume</th>
                      <th className="px-3 py-2">Supply</th>
                      <th className="px-3 py-2">Activity</th>
                    </tr>
                  </thead>
                  <tbody>
                    {heatmapRows.map(row => (
                      <tr key={row.waypoint} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono">
                          <span className="hover:underline cursor-default">{row.waypoint}</span>
                        </td>
                        <td className="px-3 py-2 text-muted-foreground">{row.system}</td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">{row.tg.type}</td>
                        <td className="px-3 py-2 text-right tabular-nums">
                          {fmt(row.tg.purchasePrice)}
                        </td>
                        <td className="px-3 py-2 text-right tabular-nums">{fmt(row.tg.sellPrice)}</td>
                        <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                          {row.tg.tradeVolume}
                        </td>
                        <td className={cn('px-3 py-2 text-xs', supplyColor(row.tg.supply))}>
                          {row.tg.supply}
                        </td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {row.tg.activity || '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {filteredMarkets.length > 0 && (
            <section aria-label="Markets list">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Markets ({filteredMarkets.length})
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
                      <th className="px-3 py-2">Exchange</th>
                      <th className="px-3 py-2 text-right">Last Scan</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredMarkets.map(m => {
                      const isExpanded = expandedMarketWaypoint === m.waypointSymbol

                      return (
                        <>
                          <tr key={m.waypointSymbol} className="border-b border-border">
                            <td className="px-3 py-2 font-mono">
                              <button
                                type="button"
                                className="text-left hover:underline"
                                aria-expanded={isExpanded}
                                onClick={() => setExpandedMarketWaypoint(isExpanded ? '' : m.waypointSymbol)}
                              >
                                {m.waypointSymbol}
                              </button>
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
                            <td className="px-3 py-2 text-xs text-muted-foreground">
                              {m.exchange.slice(0, 3).join(', ')}
                              {m.exchange.length > 3 && ` +${m.exchange.length - 3}`}
                            </td>
                            <td className="px-3 py-2 text-right text-xs tabular-nums">
                              {(() => {
                                const f = marketFreshnessMap.get(m.waypointSymbol)
                                if (!f) return <span className="text-muted-foreground">—</span>
                                const age = f.ageMinutes
                                const cls =
                                  age < 60
                                    ? 'text-status-green'
                                    : age < 360
                                      ? 'text-yellow-500'
                                      : 'text-destructive'
                                const label =
                                  age < 60
                                    ? `${age}m ago`
                                    : age < 1440
                                      ? `${Math.floor(age / 60)}h ago`
                                      : `${Math.floor(age / 1440)}d ago`
                                return <span className={cls}>{label}</span>
                              })()}
                            </td>
                          </tr>
                          {isExpanded && (
                            <tr key={`${m.waypointSymbol}-details`} className="border-b border-border last:border-0 bg-muted/20">
                              <td colSpan={7} className="px-3 py-3">
                                {m.tradeGoods.length === 0 ? (
                                  <p className="text-xs text-muted-foreground">No trade goods details available.</p>
                                ) : (
                                  <div className="overflow-x-auto rounded-md border border-border bg-background">
                                    <table className="min-w-full text-xs">
                                      <thead>
                                        <tr className="border-b border-border bg-muted/50 text-left uppercase tracking-wide text-muted-foreground">
                                          <th className="px-2 py-1.5">Good</th>
                                          <th className="px-2 py-1.5">Type</th>
                                          <th className="px-2 py-1.5 text-right">Purchase</th>
                                          <th className="px-2 py-1.5 text-right">Sell</th>
                                          <th className="px-2 py-1.5 text-right">Trade Volume</th>
                                          <th className="px-2 py-1.5">Supply</th>
                                          <th className="px-2 py-1.5">Activity</th>
                                        </tr>
                                      </thead>
                                      <tbody>
                                        {m.tradeGoods.map(g => (
                                          <tr key={g.symbol} className="border-b border-border last:border-0">
                                            <td className="px-2 py-1.5 font-mono">{g.symbol}</td>
                                            <td className="px-2 py-1.5 text-muted-foreground">{g.type}</td>
                                            <td className="px-2 py-1.5 text-right tabular-nums">{fmt(g.purchasePrice)}</td>
                                            <td className="px-2 py-1.5 text-right tabular-nums">{fmt(g.sellPrice)}</td>
                                            <td className="px-2 py-1.5 text-right tabular-nums">{g.tradeVolume}</td>
                                            <td className={cn('px-2 py-1.5', supplyColor(g.supply))}>{g.supply}</td>
                                            <td className="px-2 py-1.5 text-muted-foreground">{g.activity || '—'}</td>
                                          </tr>
                                        ))}
                                      </tbody>
                                    </table>
                                  </div>
                                )}
                              </td>
                            </tr>
                          )}
                        </>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {!marketsQ.isLoading && filteredMarkets.length === 0 && markets.length > 0 && (
            <p className="text-muted-foreground text-sm">No markets match the current filters.</p>
          )}

          {!marketsQ.isLoading && markets.length === 0 && (
            <p className="text-muted-foreground text-sm">No market data available yet.</p>
          )}
        </>
      )}

      {activeTab === 'shipyards' && (
        <>
          <div className="flex flex-wrap gap-3">
            <input
              type="search"
              placeholder="Search waypoint or system…"
              aria-label="Search shipyards"
              className="rounded border border-border bg-background px-3 py-1.5 text-sm w-64"
              value={searchShipyard}
              onChange={e => setSearchShipyard(e.target.value)}
            />
            <select
              aria-label="Filter by ship type"
              className="rounded border border-border bg-background px-2 py-1.5 text-sm"
              value={shipTypeFilter}
              onChange={e => setShipTypeFilter(e.target.value)}
            >
              <option value="">All ship types</option>
              {allShipTypes.map(t => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </select>
            {shipTypeFilter && (
              <button
                className="text-xs text-muted-foreground underline"
                onClick={() => setShipTypeFilter('')}
              >
                Clear filter
              </button>
            )}
          </div>

          {shipyardsQ.isLoading && <p className="text-muted-foreground text-sm">Loading shipyards…</p>}

          {shipTypeFilter && shipPriceRows.length > 0 && (
            <section aria-label={`Ship prices for ${shipTypeFilter}`}>
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Ship Prices — {shipTypeFilter}
              </h2>
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-3 py-2">Waypoint</th>
                      <th className="px-3 py-2">System</th>
                      <th className="px-3 py-2">Ship Name</th>
                      <th className="px-3 py-2 text-right">Price</th>
                      <th className="px-3 py-2">Supply</th>
                      <th className="px-3 py-2">Activity</th>
                    </tr>
                  </thead>
                  <tbody>
                    {shipPriceRows.map((row, idx) => (
                      <tr key={`${row.waypoint}-${idx}`} className="border-b border-border last:border-0">
                        <td className="px-3 py-2 font-mono">
                          <span className="cursor-default">{row.waypoint}</span>
                        </td>
                        <td className="px-3 py-2 text-muted-foreground">{row.system}</td>
                        <td className="px-3 py-2">{row.ship.name || row.ship.type}</td>
                        <td className="px-3 py-2 text-right tabular-nums font-medium">
                          {fmt(row.ship.purchasePrice)}
                        </td>
                        <td className={cn('px-3 py-2 text-xs', supplyColor(row.ship.supply))}>
                          {row.ship.supply || '—'}
                        </td>
                        <td className="px-3 py-2 text-xs text-muted-foreground">
                          {row.ship.activity || '—'}
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {filteredShipyards.length > 0 && (
            <section aria-label="Shipyards list">
              <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
                Shipyards ({filteredShipyards.length})
              </h2>
              <div className="overflow-x-auto rounded-lg border border-border">
                <table className="min-w-full text-sm">
                  <thead>
                    <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                      <th className="px-3 py-2">Waypoint</th>
                      <th className="px-3 py-2">System</th>
                      <th className="px-3 py-2 text-right">Ship Types</th>
                      <th className="px-3 py-2 text-right">Ships for Sale</th>
                      <th className="px-3 py-2">Available Ships</th>
                      <th className="px-3 py-2 text-right">Last Scan</th>
                    </tr>
                  </thead>
                  <tbody>
                    {filteredShipyards.map(s => {
                      const isExpanded = expandedShipyardWaypoint === s.waypointSymbol

                      return (
                        <>
                          <tr key={s.waypointSymbol} className="border-b border-border last:border-0">
                            <td className="px-3 py-2 font-mono">
                              <button
                                type="button"
                                className="text-left hover:underline"
                                aria-expanded={isExpanded}
                                onClick={() => setExpandedShipyardWaypoint(isExpanded ? '' : s.waypointSymbol)}
                              >
                                {s.waypointSymbol}
                              </button>
                            </td>
                            <td className="px-3 py-2 text-muted-foreground">{s.systemSymbol}</td>
                            <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                              {s.shipTypes.length}
                            </td>
                            <td className="px-3 py-2 text-right tabular-nums text-muted-foreground">
                              {s.ships.length}
                            </td>
                            <td className="px-3 py-2 text-xs">
                              {s.ships.slice(0, 3).map(ship => ship.type).join(', ')}
                              {s.ships.length > 3 && ` +${s.ships.length - 3}`}
                              {s.ships.length === 0 && s.shipTypes.slice(0, 3).join(', ')}
                              {s.ships.length === 0 && s.shipTypes.length > 3 && ` +${s.shipTypes.length - 3}`}
                            </td>
                            <td className="px-3 py-2 text-right text-xs tabular-nums">
                              {(() => {
                                const f = shipyardFreshnessMap.get(s.waypointSymbol)
                                if (!f) return <span className="text-muted-foreground">—</span>
                                const age = f.ageMinutes
                                const cls =
                                  age < 60
                                    ? 'text-status-green'
                                    : age < 360
                                      ? 'text-yellow-500'
                                      : 'text-destructive'
                                const label =
                                  age < 60
                                    ? `${age}m ago`
                                    : age < 1440
                                      ? `${Math.floor(age / 60)}h ago`
                                      : `${Math.floor(age / 1440)}d ago`
                                return <span className={cls}>{label}</span>
                              })()}
                            </td>
                          </tr>
                          {isExpanded && (
                            <tr key={`${s.waypointSymbol}-details`} className="border-b border-border last:border-0 bg-muted/20">
                              <td colSpan={6} className="px-3 py-3">
                                {s.ships.length === 0 ? (
                                  <p className="text-xs text-muted-foreground">No ship details available. The shipyard may only list ship types without full data.</p>
                                ) : (
                                  <div className="overflow-x-auto rounded-md border border-border bg-background">
                                    <table className="min-w-full text-xs">
                                      <thead>
                                        <tr className="border-b border-border bg-muted/50 text-left uppercase tracking-wide text-muted-foreground">
                                          <th className="px-2 py-1.5">Type</th>
                                          <th className="px-2 py-1.5">Name</th>
                                          <th className="px-2 py-1.5">Description</th>
                                          <th className="px-2 py-1.5 text-right">Price</th>
                                          <th className="px-2 py-1.5">Supply</th>
                                          <th className="px-2 py-1.5">Activity</th>
                                        </tr>
                                      </thead>
                                      <tbody>
                                        {s.ships.map(ship => (
                                          <tr key={ship.type} className="border-b border-border last:border-0">
                                            <td className="px-2 py-1.5 font-mono text-muted-foreground">{ship.type}</td>
                                            <td className="px-2 py-1.5 font-medium">{ship.name || '—'}</td>
                                            <td className="px-2 py-1.5 text-muted-foreground max-w-xs truncate" title={ship.description ?? undefined}>
                                              {ship.description || '—'}
                                            </td>
                                            <td className="px-2 py-1.5 text-right tabular-nums font-medium">{fmt(ship.purchasePrice)}</td>
                                            <td className={cn('px-2 py-1.5', supplyColor(ship.supply))}>
                                              {ship.supply || '—'}
                                            </td>
                                            <td className="px-2 py-1.5 text-muted-foreground">{ship.activity || '—'}</td>
                                          </tr>
                                        ))}
                                      </tbody>
                                    </table>
                                  </div>
                                )}
                              </td>
                            </tr>
                          )}
                        </>
                      )
                    })}
                  </tbody>
                </table>
              </div>
            </section>
          )}

          {!shipyardsQ.isLoading && filteredShipyards.length === 0 && shipyards.length > 0 && (
            <p className="text-muted-foreground text-sm">No shipyards match the current filters.</p>
          )}

          {!shipyardsQ.isLoading && shipyards.length === 0 && (
            <p className="text-muted-foreground text-sm">No shipyard data available yet.</p>
          )}
        </>
      )}
    </div>
  )
}
