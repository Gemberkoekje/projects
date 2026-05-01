import { useState, useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import type {
  RunSummaryDto,
  CreditSampleDto,
  LedgerSummaryDto,
  LedgerEntryDto,
  RunCreditHighlightDto,
  TradeRouteDto,
  SettingDto,
} from '@/types'
import { cn } from '@/lib/utils'

function formatCredits(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

function formatCreditsShort(n: number): string {
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : ''
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${sign}${Math.round(abs / 1_000)}k`
  return `${sign}${Math.round(abs)}`
}

// ─── Line chart ──────────────────────────────────────────────────────────────

interface ChartPoint {
  t: number
  v: number
  label?: string
}

interface AnnotationMark {
  t: number
  label: string
}

function LineChart({
  points,
  width = 560,
  height = 130,
  annotations = [],
}: {
  points: ChartPoint[]
  width?: number
  height?: number
  annotations?: AnnotationMark[]
}) {
  if (points.length < 2) return null

  const pad = { top: 8, right: 16, bottom: 20, left: 56 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom

  const tMin = points[0].t
  const tMax = points[points.length - 1].t
  const vMin = Math.min(...points.map(p => p.v))
  const vMax = Math.max(...points.map(p => p.v))
  const vRange = vMax - vMin || 1
  const tRange = tMax - tMin || 1

  const sx = (t: number) => pad.left + ((t - tMin) / tRange) * innerW
  const sy = (v: number) => pad.top + innerH - ((v - vMin) / vRange) * innerH

  const pathD = points
    .map((p, i) => `${i === 0 ? 'M' : 'L'}${sx(p.t).toFixed(1)},${sy(p.v).toFixed(1)}`)
    .join(' ')

  const tickCount = 3
  const ticks = Array.from(
    { length: tickCount },
    (_, i) => vMin + (vRange * i) / (tickCount - 1),
  )

  const annotated = points.filter(p => p.label)

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${width} ${height}`}
      className="overflow-visible text-primary"
      role="img"
      aria-label="Credits over time"
    >
      {ticks.map((v, i) => (
        <line
          key={i}
          x1={pad.left}
          y1={sy(v)}
          x2={pad.left + innerW}
          y2={sy(v)}
          stroke="currentColor"
          strokeOpacity={0.1}
          strokeDasharray="4 3"
        />
      ))}
      {ticks.map((v, i) => (
        <text
          key={i}
          x={pad.left - 6}
          y={sy(v)}
          textAnchor="end"
          fontSize={10}
          fill="currentColor"
          fillOpacity={0.5}
          dominantBaseline="middle"
        >
          {formatCreditsShort(v)}
        </text>
      ))}
      <path
        d={pathD}
        fill="none"
        stroke="currentColor"
        strokeWidth={2}
        strokeLinejoin="round"
        strokeLinecap="round"
      />
      {annotated.map((p, i) => (
        <circle key={i} cx={sx(p.t)} cy={sy(p.v)} r={3} fill="currentColor" />
      ))}
      {annotations.map((a, i) => {
        const x = sx(a.t)
        if (x < pad.left || x > pad.left + innerW) return null
        return (
          <g key={i}>
            <line
              x1={x}
              y1={pad.top}
              x2={x}
              y2={pad.top + innerH}
              stroke="currentColor"
              strokeOpacity={0.5}
              strokeDasharray="3 2"
              strokeWidth={1}
            />
            <text
              x={x + 3}
              y={pad.top + 10}
              fontSize={9}
              fill="currentColor"
              fillOpacity={0.7}
            >
              {a.label}
            </text>
          </g>
        )
      })}
    </svg>
  )
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function FinancePage() {
  const [selectedRunId, setSelectedRunId] = useState<string | null>(null)

  const runsQ = useQuery<RunSummaryDto[]>({
    queryKey: ['runs'],
    queryFn: () => apiFetch('/runs/'),
    staleTime: 60_000,
  })

  const runs = runsQ.data ?? []
  const activeRun = runs.find(r => r.endedAt === null) ?? null

  const isActiveRun = selectedRunId === null || selectedRunId === (activeRun?.id ?? '')
  const effectiveRunId = selectedRunId ?? activeRun?.id ?? null

  // Credits history (active run)
  const creditsHistoryQ = useQuery<CreditSampleDto[]>({
    queryKey: ['credits-history'],
    queryFn: () => apiFetch('/finance/credits-history'),
    enabled: isActiveRun,
    refetchInterval: 60_000,
  })

  // Run highlights (historical runs)
  const runHighlightsQ = useQuery<RunCreditHighlightDto[]>({
    queryKey: ['run-highlights', effectiveRunId],
    queryFn: () => apiFetch(`/finance/run-highlights?runId=${effectiveRunId}`),
    enabled: !isActiveRun && Boolean(effectiveRunId),
  })

  // Ledger summary (income/expense by category)
  const summaryQ = useQuery<LedgerSummaryDto[]>({
    queryKey: ['finance-summary', effectiveRunId],
    queryFn: () =>
      apiFetch(effectiveRunId ? `/finance/summary?runId=${effectiveRunId}` : '/finance/summary'),
    refetchInterval: isActiveRun ? 60_000 : undefined,
  })

  // Raw ledger entries for per-ship and per-good aggregation
  const ledgerQ = useQuery<LedgerEntryDto[]>({
    queryKey: ['finance-ledger', effectiveRunId],
    queryFn: () =>
      apiFetch(
        effectiveRunId
          ? `/finance/ledger?runId=${effectiveRunId}&limit=5000`
          : '/finance/ledger?limit=5000',
      ),
    refetchInterval: isActiveRun ? 120_000 : undefined,
  })

  // Trade routes heatmap
  const tradeRoutesQ = useQuery<TradeRouteDto[]>({
    queryKey: ['trade-routes', effectiveRunId],
    queryFn: () =>
      apiFetch(effectiveRunId ? `/finance/trade-routes?runId=${effectiveRunId}` : '/finance/trade-routes'),
    refetchInterval: isActiveRun ? 120_000 : undefined,
  })

  // Annotation markers from settings
  const settingsQ = useQuery<SettingDto[]>({
    queryKey: ['settings-all'],
    queryFn: () => apiFetch('/settings/'),
    staleTime: 300_000,
  })

  const annotations = useMemo<AnnotationMark[]>(() => {
    if (!settingsQ.data) return []
    return settingsQ.data
      .filter(s => s.key.startsWith('Annotation.'))
      .flatMap(s => {
        try {
          const parsed = JSON.parse(s.value) as { label?: string; timestamp?: string }
          if (parsed.timestamp && parsed.label) {
            return [{ t: new Date(parsed.timestamp).getTime(), label: parsed.label }]
          }
          return []
        } catch {
          return []
        }
      })
  }, [settingsQ.data])

  const summary = summaryQ.data ?? []
  const ledgerEntries = ledgerQ.data ?? []
  const tradeRoutes = tradeRoutesQ.data ?? []

  // Chart data points
  const chartPoints = useMemo<ChartPoint[]>(() => {
    if (isActiveRun && creditsHistoryQ.data) {
      return creditsHistoryQ.data.map(s => ({
        t: new Date(s.recordedAt).getTime(),
        v: s.credits,
      }))
    }
    if (!isActiveRun && runHighlightsQ.data) {
      return runHighlightsQ.data.map(h => ({
        t: new Date(h.occurredAt).getTime(),
        v: h.credits,
        label: h.label ?? h.eventKind,
      }))
    }
    return []
  }, [isActiveRun, creditsHistoryQ.data, runHighlightsQ.data])

  // Category totals
  const incomeCategories = summary
    .filter(s => s.totalAmount > 0)
    .sort((a, b) => b.totalAmount - a.totalAmount)
  const expenseCategories = summary
    .filter(s => s.totalAmount < 0)
    .sort((a, b) => a.totalAmount - b.totalAmount)
  const totalIncome = incomeCategories.reduce((acc, s) => acc + s.totalAmount, 0)
  const totalExpenses = expenseCategories.reduce((acc, s) => acc + Math.abs(s.totalAmount), 0)
  const netPnL = totalIncome - totalExpenses

  // Per-ship P&L
  const perShip = useMemo(() => {
    const map = new Map<string, { income: number; expenses: number }>()
    for (const e of ledgerEntries) {
      const entry = map.get(e.shipSymbol) ?? { income: 0, expenses: 0 }
      if (e.amount > 0) entry.income += e.amount
      else entry.expenses += Math.abs(e.amount)
      map.set(e.shipSymbol, entry)
    }
    return Array.from(map.entries())
      .map(([ship, { income, expenses }]) => ({ ship, income, expenses, net: income - expenses }))
      .sort((a, b) => b.net - a.net)
  }, [ledgerEntries])

  // Per-good profit table
  const perGood = useMemo(() => {
    const buyMap = new Map<string, { totalCost: number; totalUnits: number }>()
    const sellMap = new Map<string, { totalRevenue: number; totalUnits: number }>()
    for (const e of ledgerEntries) {
      if (!e.goodSymbol || e.units == null) continue
      if (e.amount < 0) {
        const entry = buyMap.get(e.goodSymbol) ?? { totalCost: 0, totalUnits: 0 }
        entry.totalCost += Math.abs(e.amount)
        entry.totalUnits += e.units
        buyMap.set(e.goodSymbol, entry)
      } else {
        const entry = sellMap.get(e.goodSymbol) ?? { totalRevenue: 0, totalUnits: 0 }
        entry.totalRevenue += e.amount
        entry.totalUnits += e.units
        sellMap.set(e.goodSymbol, entry)
      }
    }
    const goods = new Set([...buyMap.keys(), ...sellMap.keys()])
    return Array.from(goods)
      .map(good => {
        const buy = buyMap.get(good) ?? { totalCost: 0, totalUnits: 0 }
        const sell = sellMap.get(good) ?? { totalRevenue: 0, totalUnits: 0 }
        const profit = sell.totalRevenue - buy.totalCost
        const avgBuy = buy.totalUnits > 0 ? Math.round(buy.totalCost / buy.totalUnits) : 0
        const avgSell = sell.totalUnits > 0 ? Math.round(sell.totalRevenue / sell.totalUnits) : 0
        return { good, unitsSold: sell.totalUnits, avgBuy, avgSell, profit }
      })
      .sort((a, b) => b.profit - a.profit)
  }, [ledgerEntries])

  const maxProfitPerHour = tradeRoutes.length > 0 ? Math.max(...tradeRoutes.map(r => r.profitPerHour)) : 1

  function profitHeatColor(profitPerHour: number): string {
    if (maxProfitPerHour <= 0) return ''
    const ratio = profitPerHour / maxProfitPerHour
    if (ratio >= 0.66) return 'text-status-green'
    if (ratio >= 0.33) return 'text-yellow-500'
    return 'text-destructive'
  }

  const isLoading = runsQ.isLoading || summaryQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      {/* Header + run selector */}
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-bold">Finance</h1>
        {runs.length > 0 && (
          <select
            aria-label="Select run"
            className="rounded border border-border bg-background px-2 py-1 text-sm"
            value={selectedRunId ?? ''}
            onChange={e => setSelectedRunId(e.target.value || null)}
          >
            <option value="">Active run</option>
            {runs
              .filter(r => r.endedAt !== null)
              .map(r => (
                <option key={r.id} value={r.id}>
                  {r.name} — {r.strategyLabel}
                </option>
              ))}
          </select>
        )}
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Credits over time */}
      {chartPoints.length >= 2 && (
        <section aria-label="Credits over time">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Credits Over Time
          </h2>
          <div className="rounded-lg border border-border bg-background p-4">
            <LineChart points={chartPoints} annotations={annotations} />
          </div>
        </section>
      )}

      {/* Summary stat cards */}
      {summary.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Total Income</p>
            <p className="text-2xl font-bold text-status-green">+{formatCredits(totalIncome)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Total Expenses</p>
            <p className="text-2xl font-bold text-destructive">-{formatCredits(totalExpenses)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Net P&L</p>
            <p
              className={cn(
                'text-2xl font-bold',
                netPnL >= 0 ? 'text-status-green' : 'text-destructive',
              )}
            >
              {netPnL >= 0 ? '+' : ''}
              {formatCredits(netPnL)}
            </p>
          </div>
        </div>
      )}

      {/* Income & Expense by category */}
      {summary.length > 0 && (
        <section aria-label="Income and expenses by category">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Income & Expenses by Category
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Category</th>
                  <th className="px-3 py-2 text-right">Amount</th>
                  <th className="px-3 py-2 text-right">Entries</th>
                  <th className="px-3 py-2 w-32">Share</th>
                </tr>
              </thead>
              <tbody>
                {[...summary].sort((a, b) => b.totalAmount - a.totalAmount).map(s => {
                  const maxAbs = Math.max(totalIncome, totalExpenses, 1)
                  const pct = Math.round((Math.abs(s.totalAmount) / maxAbs) * 100)
                  return (
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
                      <td className="px-3 py-2">
                        <div className="h-2 rounded-full bg-muted overflow-hidden">
                          <div
                            className={cn(
                              'h-full rounded-full',
                              s.totalAmount >= 0 ? 'bg-status-green' : 'bg-destructive',
                            )}
                            style={{ width: `${pct}%` }}
                          />
                        </div>
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Budget panel */}
      {expenseCategories.length > 0 && (
        <section aria-label="Budget panel">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Budget
          </h2>
          <div className="rounded-lg border border-border bg-background p-4 flex flex-col gap-3">
            {expenseCategories.map(s => {
              const pct =
                totalExpenses > 0
                  ? Math.round((Math.abs(s.totalAmount) / totalExpenses) * 100)
                  : 0
              return (
                <div key={s.category} className="flex flex-col gap-1">
                  <div className="flex justify-between text-sm">
                    <span className="font-medium">{s.category}</span>
                    <span className="text-muted-foreground tabular-nums">
                      {formatCredits(Math.abs(s.totalAmount))} ({pct}%)
                    </span>
                  </div>
                  <div className="h-2 rounded-full bg-muted overflow-hidden">
                    <div
                      className="h-full rounded-full bg-destructive/70"
                      style={{ width: `${pct}%` }}
                    />
                  </div>
                </div>
              )
            })}
            <p className="text-xs text-muted-foreground mt-1">
              Total spending: {formatCredits(totalExpenses)} credits
            </p>
          </div>
        </section>
      )}

      {/* Per-ship P&L */}
      {perShip.length > 0 && (
        <section aria-label="Per-ship P&L">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Per-Ship P&L
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Ship</th>
                  <th className="px-3 py-2 text-right">Income</th>
                  <th className="px-3 py-2 text-right">Expenses</th>
                  <th className="px-3 py-2 text-right">Net</th>
                </tr>
              </thead>
              <tbody>
                {perShip.map(row => (
                  <tr key={row.ship} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-mono">{row.ship}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-status-green">
                      +{formatCredits(row.income)}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums text-destructive">
                      -{formatCredits(row.expenses)}
                    </td>
                    <td
                      className={cn(
                        'px-3 py-2 text-right tabular-nums font-medium',
                        row.net >= 0 ? 'text-status-green' : 'text-destructive',
                      )}
                    >
                      {row.net >= 0 ? '+' : ''}
                      {formatCredits(row.net)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Per-good profit table */}
      {perGood.length > 0 && (
        <section aria-label="Per-good profit">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Per-Good Profit
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Good</th>
                  <th className="px-3 py-2 text-right">Units Sold</th>
                  <th className="px-3 py-2 text-right">Avg Buy</th>
                  <th className="px-3 py-2 text-right">Avg Sell</th>
                  <th className="px-3 py-2 text-right">Profit</th>
                </tr>
              </thead>
              <tbody>
                {perGood.map(row => (
                  <tr key={row.good} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-mono">{row.good}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{row.unitsSold}</td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {row.avgBuy > 0 ? formatCredits(row.avgBuy) : '—'}
                    </td>
                    <td className="px-3 py-2 text-right tabular-nums">
                      {row.avgSell > 0 ? formatCredits(row.avgSell) : '—'}
                    </td>
                    <td
                      className={cn(
                        'px-3 py-2 text-right tabular-nums font-medium',
                        row.profit >= 0 ? 'text-status-green' : 'text-destructive',
                      )}
                    >
                      {row.profit >= 0 ? '+' : ''}
                      {formatCredits(row.profit)}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* Trade Routes heatmap */}
      <section aria-label="Trade routes">
        <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
          Trade Routes
        </h2>
        {tradeRoutes.length === 0 ? (
          <p className="text-muted-foreground text-sm">No trade route data available.</p>
        ) : (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Good</th>
                  <th className="px-3 py-2">Buy Waypoint</th>
                  <th className="px-3 py-2">Sell Waypoint</th>
                  <th className="px-3 py-2 text-right">Units</th>
                  <th className="px-3 py-2 text-right">Total Profit</th>
                  <th className="px-3 py-2 text-right">Profit/Hour</th>
                </tr>
              </thead>
              <tbody>
                {tradeRoutes.map((row, i) => (
                  <tr key={i} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-mono">{row.good}</td>
                    <td className="px-3 py-2 font-mono text-xs">{row.buyWaypoint}</td>
                    <td className="px-3 py-2 font-mono text-xs">{row.sellWaypoint}</td>
                    <td className="px-3 py-2 text-right tabular-nums">{row.totalUnits}</td>
                    <td className="px-3 py-2 text-right tabular-nums text-status-green">
                      +{formatCredits(row.totalProfit)}
                    </td>
                    <td
                      className={cn(
                        'px-3 py-2 text-right tabular-nums font-medium',
                        profitHeatColor(row.profitPerHour),
                      )}
                    >
                      {formatCredits(Math.round(row.profitPerHour))}/h
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </div>
  )
}
