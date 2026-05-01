import { useMemo, useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Route, Routes, useNavigate, useParams, useSearchParams } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import { cn } from '@/lib/utils'
import type { RunSummaryDto, ScheduledRunDto, RunDetailDto, RunCompareDto, RunKpisDto, SettingDto } from '@/types'

// ─── Helpers ──────────────────────────────────────────────────────────────────

function fmt(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

function fmtShort(n: number): string {
  const abs = Math.abs(n)
  const sign = n < 0 ? '-' : n > 0 ? '+' : ''
  if (abs >= 1_000_000) return `${sign}${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${sign}${Math.round(abs / 1_000)}k`
  return `${sign}${Math.round(abs)}`
}

function fmtDuration(startedAt: string, endedAt: string | null): string {
  const start = new Date(startedAt).getTime()
  const end = endedAt ? new Date(endedAt).getTime() : Date.now()
  const ms = end - start
  const hours = Math.floor(ms / 3_600_000)
  const minutes = Math.floor((ms % 3_600_000) / 60_000)
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${minutes}m`
}

function fmtDate(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

// ─── Credits-over-time chart ──────────────────────────────────────────────────

interface ChartPoint {
  t: number
  v: number
  label?: string
}

function CreditChart({
  points,
  annotations = [],
}: {
  points: ChartPoint[]
  annotations?: Array<{ t: number; label: string }>
}) {
  if (points.length < 2) return null

  const width = 560
  const height = 130
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

  const ticks = [vMin, (vMin + vMax) / 2, vMax]
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
          {fmtShort(v)}
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
            <text x={x + 3} y={pad.top + 10} fontSize={9} fill="currentColor" fillOpacity={0.7}>
              {a.label}
            </text>
          </g>
        )
      })}
    </svg>
  )
}

// ─── Normalised dual-series chart (compare view) ──────────────────────────────

interface NormSeries {
  points: ChartPoint[]
  color: string
  label: string
}

function NormalisedCreditChart({ seriesA, seriesB }: { seriesA: NormSeries; seriesB: NormSeries }) {
  const allSeries = [seriesA, seriesB].filter(s => s.points.length >= 2)
  if (allSeries.length === 0) return null

  const width = 560
  const height = 130
  const pad = { top: 8, right: 16, bottom: 20, left: 56 }
  const innerW = width - pad.left - pad.right
  const innerH = height - pad.top - pad.bottom

  // Normalise each series: x = fraction of its own run duration, y = credits
  const normalisedSeries = allSeries.map(s => {
    const tStart = s.points[0].t
    const tEnd = s.points[s.points.length - 1].t
    const tRange = tEnd - tStart || 1
    return {
      ...s,
      norm: s.points.map(p => ({ x: (p.t - tStart) / tRange, v: p.v })),
    }
  })

  const allVals = normalisedSeries.flatMap(s => s.norm.map(p => p.v))
  const vMin = Math.min(...allVals)
  const vMax = Math.max(...allVals)
  const vRange = vMax - vMin || 1

  const sx = (x: number) => pad.left + x * innerW
  const sy = (v: number) => pad.top + innerH - ((v - vMin) / vRange) * innerH

  const ticks = [vMin, (vMin + vMax) / 2, vMax]

  return (
    <svg
      width="100%"
      viewBox={`0 0 ${width} ${height}`}
      className="overflow-visible"
      role="img"
      aria-label="Normalised credits comparison"
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
          {fmtShort(v)}
        </text>
      ))}
      {normalisedSeries.map(s => {
        const d = s.norm
          .map((p, i) => `${i === 0 ? 'M' : 'L'}${sx(p.x).toFixed(1)},${sy(p.v).toFixed(1)}`)
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

// ─── Runs list ────────────────────────────────────────────────────────────────

function RunsListPage() {
  const navigate = useNavigate()
  const [selected, setSelected] = useState<string[]>([])

  const runsQ = useQuery<RunSummaryDto[]>({
    queryKey: ['runs'],
    queryFn: () => apiFetch('/runs/'),
    staleTime: 60_000,
  })

  const scheduledQ = useQuery<ScheduledRunDto[]>({
    queryKey: ['runs-scheduled'],
    queryFn: () => apiFetch('/runs/scheduled'),
    staleTime: 60_000,
  })

  const runs = runsQ.data ?? []
  const scheduled = scheduledQ.data ?? []
  const isLoading = runsQ.isLoading || scheduledQ.isLoading

  function toggleSelect(id: string) {
    setSelected(prev =>
      prev.includes(id) ? prev.filter(x => x !== id) : prev.length < 2 ? [...prev, id] : [prev[1], id],
    )
  }

  function handleCompare() {
    if (selected.length === 2) {
      navigate(`/runs/compare?a=${selected[0]}&b=${selected[1]}`)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-2xl font-bold">Runs</h1>
        {selected.length > 0 && (
          <div className="flex items-center gap-2">
            <span className="text-sm text-muted-foreground">
              {selected.length === 1 ? 'Select one more run to compare' : '2 runs selected'}
            </span>
            <button
              onClick={handleCompare}
              disabled={selected.length < 2}
              className={cn(
                'rounded-md px-3 py-1 text-sm font-medium',
                selected.length < 2
                  ? 'cursor-not-allowed bg-muted text-muted-foreground'
                  : 'bg-primary text-primary-foreground hover:opacity-90',
              )}
            >
              Compare
            </button>
            <button
              onClick={() => setSelected([])}
              className="text-xs text-muted-foreground hover:underline"
            >
              Clear
            </button>
          </div>
        )}
      </div>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {/* Scheduled / pending runs */}
      {scheduled.length > 0 && (
        <section aria-label="Scheduled runs">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Scheduled / Pending
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Name</th>
                  <th className="px-3 py-2">Strategy</th>
                  <th className="px-3 py-2">Activates</th>
                  <th className="px-3 py-2">Created</th>
                </tr>
              </thead>
              <tbody>
                {scheduled.map(s => (
                  <tr key={s.id} className="border-b border-border last:border-0">
                    <td className="px-3 py-2 font-medium">
                      {s.name}
                      <span className="ml-2 rounded-full bg-yellow-500/20 px-2 py-0.5 text-xs text-yellow-600 dark:text-yellow-400">
                        Pending
                      </span>
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{s.strategyLabel}</td>
                    <td className="px-3 py-2 text-muted-foreground">
                      {s.activatesOnNextRestart
                        ? 'On next restart'
                        : s.activatesAt
                          ? fmtDate(s.activatesAt)
                          : '—'}
                    </td>
                    <td className="px-3 py-2 text-muted-foreground">{fmtDate(s.createdAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </section>
      )}

      {/* All runs */}
      {!isLoading && runs.length === 0 && scheduled.length === 0 && (
        <p className="text-muted-foreground text-sm">No runs recorded yet.</p>
      )}

      {runs.length > 0 && (
        <section aria-label="All runs">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            All Runs
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2 w-8" aria-label="Select for comparison" />
                  <th className="px-3 py-2">Name</th>
                  <th className="px-3 py-2">Strategy</th>
                  <th className="px-3 py-2">Started</th>
                  <th className="px-3 py-2">Duration</th>
                  <th className="px-3 py-2 text-right">Starting Credits</th>
                  <th className="px-3 py-2 text-right">ΔCredits</th>
                </tr>
              </thead>
              <tbody>
                {runs.map(run => {
                  const delta =
                    run.endingCredits != null ? run.endingCredits - run.startingCredits : null
                  const isActive = run.endedAt === null
                  const isChecked = selected.includes(run.id)
                  return (
                    <tr key={run.id} className="border-b border-border last:border-0 hover:bg-muted/30">
                      <td className="px-3 py-2">
                        <input
                          type="checkbox"
                          aria-label={`Select ${run.name} for comparison`}
                          checked={isChecked}
                          onChange={() => toggleSelect(run.id)}
                          className="cursor-pointer"
                        />
                      </td>
                      <td className="px-3 py-2">
                        <Link
                          to={`/runs/${run.id}`}
                          className="font-medium text-primary hover:underline"
                          aria-label={`View run ${run.name}`}
                        >
                          {run.name}
                        </Link>
                        {isActive && (
                          <span className="ml-2 rounded-full bg-status-green/20 px-2 py-0.5 text-xs text-status-green">
                            Active
                          </span>
                        )}
                      </td>
                      <td className="px-3 py-2 text-muted-foreground">{run.strategyLabel}</td>
                      <td className="px-3 py-2 text-muted-foreground">{fmtDate(run.startedAt)}</td>
                      <td className="px-3 py-2 text-muted-foreground tabular-nums">
                        {fmtDuration(run.startedAt, run.endedAt)}
                      </td>
                      <td className="px-3 py-2 text-right tabular-nums">{fmt(run.startingCredits)}</td>
                      <td
                        className={cn(
                          'px-3 py-2 text-right tabular-nums font-medium',
                          delta == null
                            ? 'text-muted-foreground'
                            : delta >= 0
                              ? 'text-status-green'
                              : 'text-destructive',
                        )}
                      >
                        {delta == null ? '—' : fmtShort(delta)}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  )
}

// ─── Run detail ───────────────────────────────────────────────────────────────

function RunDetailPage() {
  const { id } = useParams<{ id: string }>()

  const detailQ = useQuery<RunDetailDto>({
    queryKey: ['run-detail', id],
    queryFn: () => apiFetch<RunDetailDto>(`/runs/${id}/summary`),
    enabled: Boolean(id),
  })

  const kpisQ = useQuery<RunKpisDto>({
    queryKey: ['run-kpis', id],
    queryFn: () => apiFetch<RunKpisDto>(`/runs/${id}/kpis`),
    enabled: Boolean(id),
  })

  const settingsQ = useQuery<SettingDto[]>({
    queryKey: ['settings-all'],
    queryFn: () => apiFetch('/settings/'),
    staleTime: 300_000,
  })

  const annotations = useMemo(() => {
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

  const detail = detailQ.data
  const kpis = kpisQ.data
  const run = detail?.run
  const highlights = detail?.creditHighlights ?? []
  const ledgerSummary = detail?.ledgerSummary ?? []

  const chartPoints = useMemo<ChartPoint[]>(
    () =>
      highlights.map(h => ({
        t: new Date(h.occurredAt).getTime(),
        v: h.credits,
        label: h.label ?? h.eventKind,
      })),
    [highlights],
  )

  const { totalIncome, totalExpenses, netPnL, sortedSummary } =
    useMemo(() => {
      const income = ledgerSummary.filter(s => s.totalAmount > 0)
      const expense = ledgerSummary.filter(s => s.totalAmount < 0)
      const ti = income.reduce((acc, s) => acc + s.totalAmount, 0)
      const te = expense.reduce((acc, s) => acc + Math.abs(s.totalAmount), 0)
      const sorted = [...ledgerSummary].sort((a, b) => b.totalAmount - a.totalAmount)
      return { totalIncome: ti, totalExpenses: te, netPnL: ti - te, sortedSummary: sorted }
    }, [ledgerSummary])

  if (detailQ.isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
        <p className="text-muted-foreground text-sm">Loading…</p>
      </div>
    )
  }

  if (!run) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
        <p className="text-muted-foreground text-sm">Run &quot;{id}&quot; not found.</p>
      </div>
    )
  }

  const delta = run.endingCredits != null ? run.endingCredits - run.startingCredits : null
  const isActive = run.endedAt === null

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-bold">{run.name}</h1>
        {isActive && (
          <span className="rounded-full bg-status-green/20 px-3 py-1 text-sm text-status-green">
            Active
          </span>
        )}
      </div>

      {/* Summary card */}
      <section aria-label="Run summary">
        <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Strategy</p>
            <p className="text-lg font-semibold truncate">{run.strategyLabel}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Started</p>
            <p className="text-sm font-medium">{fmtDate(run.startedAt)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Duration</p>
            <p className="text-lg font-semibold">{fmtDuration(run.startedAt, run.endedAt)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Starting Credits</p>
            <p className="text-lg font-semibold tabular-nums">{fmt(run.startingCredits)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">ΔCredits</p>
            <p
              className={cn(
                'text-lg font-semibold tabular-nums',
                delta == null
                  ? 'text-muted-foreground'
                  : delta >= 0
                    ? 'text-status-green'
                    : 'text-destructive',
              )}
            >
              {delta == null ? '—' : fmtShort(delta)}
            </p>
          </div>
        </div>
      </section>

      {/* Credits over time */}
      {chartPoints.length >= 2 && (
        <section aria-label="Credits over time">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Credits Over Time
          </h2>
          <div className="rounded-lg border border-border bg-background p-4">
            <CreditChart points={chartPoints} annotations={annotations} />
          </div>
        </section>
      )}

      {/* Net P&L stats */}
      {ledgerSummary.length > 0 && (
        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Total Income</p>
            <p className="text-2xl font-bold text-status-green">+{fmt(totalIncome)}</p>
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <p className="text-xs text-muted-foreground uppercase tracking-wide">Total Expenses</p>
            <p className="text-2xl font-bold text-destructive">-{fmt(totalExpenses)}</p>
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
              {fmt(netPnL)}
            </p>
          </div>
        </div>
      )}

      {/* Income & Expense by category */}
      {ledgerSummary.length > 0 && (
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
                {sortedSummary.map(s => {
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
                        {fmt(s.totalAmount)}
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

      {/* Efficiency KPIs */}
      {kpis && (
        <section aria-label="Efficiency KPIs">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Efficiency KPIs
          </h2>
          <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-4">
            <div className="rounded-lg border border-border bg-background p-4">
              <p className="text-xs text-muted-foreground uppercase tracking-wide">Credits / Hour</p>
              <p className="text-lg font-semibold tabular-nums">
                {kpis.creditsPerHour != null ? fmtShort(kpis.creditsPerHour) : '—'}
              </p>
            </div>
            <div className="rounded-lg border border-border bg-background p-4">
              <p className="text-xs text-muted-foreground uppercase tracking-wide">Credits / Ship / Hour</p>
              <p className="text-lg font-semibold tabular-nums">
                {kpis.creditsPerShipPerHour != null ? fmtShort(kpis.creditsPerShipPerHour) : '—'}
              </p>
            </div>
            <div className="rounded-lg border border-border bg-background p-4">
              <p className="text-xs text-muted-foreground uppercase tracking-wide">Credits / API Call</p>
              <p className="text-lg font-semibold tabular-nums">
                {kpis.creditsPerApiCall != null ? kpis.creditsPerApiCall.toFixed(2) : '—'}
              </p>
            </div>
            <div className="rounded-lg border border-border bg-background p-4">
              <p className="text-xs text-muted-foreground uppercase tracking-wide">Idle %</p>
              <p className="text-lg font-semibold tabular-nums">
                {kpis.idlePercent != null ? `${kpis.idlePercent.toFixed(1)}%` : '—'}
              </p>
            </div>
            <div className="rounded-lg border border-border bg-background p-4">
              <p className="text-xs text-muted-foreground uppercase tracking-wide">Fuel Cost / Credit Earned</p>
              <p className="text-lg font-semibold tabular-nums">
                {kpis.fuelCostPerCreditEarned != null ? kpis.fuelCostPerCreditEarned.toFixed(4) : '—'}
              </p>
            </div>
          </div>
        </section>
      )}
    </div>
  )
}

// ─── Run compare ─────────────────────────────────────────────────────────────

const SERIES_COLORS = ['#6366f1', '#f59e0b'] as const

function RunStatCard({ label, value, color }: { label: string; value: string; color?: string }) {
  return (
    <div className="rounded-lg border border-border bg-background p-4">
      <p className="text-xs text-muted-foreground uppercase tracking-wide">{label}</p>
      <p className={cn('text-lg font-semibold tabular-nums truncate', color)}>{value}</p>
    </div>
  )
}

function RunCompareSide({ detail, color }: { detail: RunDetailDto; color: string }) {
  const run = detail.run
  const delta = run.endingCredits != null ? run.endingCredits - run.startingCredits : null
  const isActive = run.endedAt === null

  const { totalIncome, netPnL } = useMemo(() => {
    const income = detail.ledgerSummary.filter(s => s.totalAmount > 0)
    const expense = detail.ledgerSummary.filter(s => s.totalAmount < 0)
    const ti = income.reduce((acc, s) => acc + s.totalAmount, 0)
    const te = expense.reduce((acc, s) => acc + Math.abs(s.totalAmount), 0)
    return { totalIncome: ti, netPnL: ti - te }
  }, [detail.ledgerSummary])

  return (
    <div className="flex flex-col gap-3">
      <div className="flex items-center gap-2">
        <span className="inline-block h-3 w-3 rounded-full flex-shrink-0" style={{ background: color }} />
        <h2 className="text-lg font-bold truncate">{run.name}</h2>
        {isActive && (
          <span className="rounded-full bg-status-green/20 px-2 py-0.5 text-xs text-status-green flex-shrink-0">
            Active
          </span>
        )}
      </div>
      <div className="grid grid-cols-2 gap-2">
        <RunStatCard label="Strategy" value={run.strategyLabel} />
        <RunStatCard label="Duration" value={fmtDuration(run.startedAt, run.endedAt)} />
        <RunStatCard label="Starting Credits" value={fmt(run.startingCredits)} />
        <RunStatCard
          label="ΔCredits"
          value={delta == null ? '—' : fmtShort(delta)}
          color={delta == null ? undefined : delta >= 0 ? 'text-status-green' : 'text-destructive'}
        />
        {detail.ledgerSummary.length > 0 && (
          <>
            <RunStatCard label="Total Income" value={`+${fmt(totalIncome)}`} color="text-status-green" />
            <RunStatCard label="Net P&L" value={`${netPnL >= 0 ? '+' : ''}${fmt(netPnL)}`} color={netPnL >= 0 ? 'text-status-green' : 'text-destructive'} />
          </>
        )}
      </div>
    </div>
  )
}

function RunComparePage() {
  const [params] = useSearchParams()
  const a = params.get('a')
  const b = params.get('b')

  const compareQ = useQuery<RunCompareDto>({
    queryKey: ['runs-compare', a, b],
    queryFn: () => apiFetch<RunCompareDto>(`/runs/compare?a=${a}&b=${b}`),
    enabled: Boolean(a && b),
  })

  const data = compareQ.data

  const chartSeriesA = useMemo<NormSeries | null>(() => {
    if (!data) return null
    const pts = data.runA.creditHighlights.map(h => ({
      t: new Date(h.occurredAt).getTime(),
      v: h.credits,
      label: h.label ?? h.eventKind,
    }))
    return { points: pts, color: SERIES_COLORS[0], label: data.runA.run.name }
  }, [data])

  const chartSeriesB = useMemo<NormSeries | null>(() => {
    if (!data) return null
    const pts = data.runB.creditHighlights.map(h => ({
      t: new Date(h.occurredAt).getTime(),
      v: h.credits,
      label: h.label ?? h.eventKind,
    }))
    return { points: pts, color: SERIES_COLORS[1], label: data.runB.run.name }
  }, [data])

  // Merge ledger categories from both runs for comparison table
  const mergedCategories = useMemo(() => {
    if (!data) return []
    const cats = new Set([
      ...data.runA.ledgerSummary.map(s => s.category),
      ...data.runB.ledgerSummary.map(s => s.category),
    ])
    return [...cats].sort().map(cat => ({
      category: cat,
      amountA: data.runA.ledgerSummary.find(s => s.category === cat)?.totalAmount ?? 0,
      amountB: data.runB.ledgerSummary.find(s => s.category === cat)?.totalAmount ?? 0,
    }))
  }, [data])

  if (!a || !b) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
        <p className="text-muted-foreground text-sm">
          Missing run IDs. Select two runs from the list to compare.
        </p>
      </div>
    )
  }

  if (compareQ.isLoading) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
        <p className="text-muted-foreground text-sm">Loading…</p>
      </div>
    )
  }

  if (!data) {
    return (
      <div className="flex flex-col gap-4">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
        <p className="text-muted-foreground text-sm">One or both runs could not be found.</p>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center gap-3">
        <Link to="/runs" className="text-sm text-muted-foreground hover:underline">
          ← Runs
        </Link>
      </div>

      <h1 className="text-2xl font-bold">Compare Runs</h1>

      {/* Side-by-side headers */}
      <section aria-label="Run comparison headers">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
          <RunCompareSide detail={data.runA} color={SERIES_COLORS[0]} />
          <RunCompareSide detail={data.runB} color={SERIES_COLORS[1]} />
        </div>
      </section>

      {/* Overlaid normalised credits chart */}
      {chartSeriesA && chartSeriesB && (chartSeriesA.points.length >= 2 || chartSeriesB.points.length >= 2) && (
        <section aria-label="Normalised credits comparison">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Credits Over Time (normalised)
          </h2>
          <div className="flex gap-4 mb-2">
            {[chartSeriesA, chartSeriesB].map(s => (
              <div key={s.label} className="flex items-center gap-1.5 text-xs text-muted-foreground">
                <span className="inline-block h-2.5 w-5 rounded-sm" style={{ background: s.color }} />
                {s.label}
              </div>
            ))}
          </div>
          <div className="rounded-lg border border-border bg-background p-4">
            <NormalisedCreditChart seriesA={chartSeriesA} seriesB={chartSeriesB} />
          </div>
        </section>
      )}

      {/* Per-category comparison table */}
      {mergedCategories.length > 0 && (
        <section aria-label="Category comparison">
          <h2 className="text-sm font-semibold mb-2 text-muted-foreground uppercase tracking-wide">
            Income & Expenses by Category
          </h2>
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-3 py-2">Category</th>
                  <th className="px-3 py-2 text-right">
                    <span className="inline-flex items-center gap-1">
                      <span className="inline-block h-2 w-2 rounded-full" style={{ background: SERIES_COLORS[0] }} />
                      {data.runA.run.name}
                    </span>
                  </th>
                  <th className="px-3 py-2 text-right">
                    <span className="inline-flex items-center gap-1">
                      <span className="inline-block h-2 w-2 rounded-full" style={{ background: SERIES_COLORS[1] }} />
                      {data.runB.run.name}
                    </span>
                  </th>
                  <th className="px-3 py-2 text-right">Δ (B − A)</th>
                </tr>
              </thead>
              <tbody>
                {mergedCategories.map(row => {
                  const diff = row.amountB - row.amountA
                  return (
                    <tr key={row.category} className="border-b border-border last:border-0">
                      <td className="px-3 py-2">{row.category}</td>
                      <td className={cn('px-3 py-2 text-right tabular-nums', row.amountA >= 0 ? 'text-status-green' : 'text-destructive')}>
                        {row.amountA >= 0 ? '+' : ''}{fmt(row.amountA)}
                      </td>
                      <td className={cn('px-3 py-2 text-right tabular-nums', row.amountB >= 0 ? 'text-status-green' : 'text-destructive')}>
                        {row.amountB >= 0 ? '+' : ''}{fmt(row.amountB)}
                      </td>
                      <td className={cn('px-3 py-2 text-right tabular-nums font-medium', diff === 0 ? 'text-muted-foreground' : diff > 0 ? 'text-status-green' : 'text-destructive')}>
                        {diff === 0 ? '—' : `${diff > 0 ? '+' : ''}${fmt(diff)}`}
                      </td>
                    </tr>
                  )
                })}
              </tbody>
            </table>
          </div>
        </section>
      )}
    </div>
  )
}

export default function RunsPage() {
  return (
    <Routes>
      <Route path="/" element={<RunsListPage />} />
      <Route path="/compare" element={<RunComparePage />} />
      <Route path="/:id" element={<RunDetailPage />} />
    </Routes>
  )
}
