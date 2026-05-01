import { useMemo } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link, Route, Routes, useParams } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import { cn } from '@/lib/utils'
import type { RunSummaryDto, ScheduledRunDto, RunDetailDto } from '@/types'

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

function CreditChart({ points }: { points: ChartPoint[] }) {
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
    </svg>
  )
}

// ─── Runs list ────────────────────────────────────────────────────────────────

function RunsListPage() {
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

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">Runs</h1>

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
                  return (
                    <tr key={run.id} className="border-b border-border last:border-0 hover:bg-muted/30">
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

  const detail = detailQ.data
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
            <CreditChart points={chartPoints} />
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
    </div>
  )
}

// ─── Page root (sub-router) ───────────────────────────────────────────────────

export default function RunsPage() {
  return (
    <Routes>
      <Route path="/" element={<RunsListPage />} />
      <Route path="/:id" element={<RunDetailPage />} />
    </Routes>
  )
}
