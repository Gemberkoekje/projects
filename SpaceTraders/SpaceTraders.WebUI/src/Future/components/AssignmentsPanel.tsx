/**
 * AssignmentsPanel – Phase 17c
 * Shows all ship assignments in a sortable table with a goal-kind filter.
 */
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipAssignmentDto, ShipGoalHistoryDto } from '@/types'
import { cn } from '@/lib/utils'

type SortKey = keyof Pick<
  ShipAssignmentDto,
  'shipSymbol' | 'goalKind' | 'sourceWaypoint' | 'destinationWaypoint' | 'fleetGoalDescription'
>

type SortDir = 'asc' | 'desc'

function formatTimestamp(iso: string | null) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(undefined, {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function formatCredits(value: number, alwaysShowSign = false) {
  const formatted = new Intl.NumberFormat('en-US', { maximumFractionDigits: 0 }).format(Math.abs(value))
  if (value < 0) return `-${formatted}`
  if (alwaysShowSign && value > 0) return `+${formatted}`
  return formatted
}

function formatDuration(totalSeconds: number) {
  if (totalSeconds <= 0) return '0m'
  const hours = Math.floor(totalSeconds / 3600)
  const minutes = Math.floor((totalSeconds % 3600) / 60)
  if (hours > 0) return `${hours}h ${minutes}m`
  return `${Math.max(1, minutes)}m`
}

function OutcomeBadge({ outcome }: { outcome: string }) {
  const isCompleted = outcome === 'Completed'
  return (
    <span
      className={cn(
        'inline-flex rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide',
        isCompleted
          ? 'bg-status-green/15 text-status-green'
          : 'bg-status-yellow/15 text-status-yellow',
      )}
    >
      {outcome}
    </span>
  )
}

function NetCreditsBadge({ value }: { value: number }) {
  return (
    <span
      className={cn(
        'inline-flex rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide',
        value > 0
          ? 'bg-status-green/15 text-status-green'
          : value < 0
            ? 'bg-destructive/15 text-destructive'
            : 'bg-muted text-muted-foreground',
      )}
    >
      {value > 0 ? 'Profit ' : value < 0 ? 'Cost ' : 'Break-even '}
      {formatCredits(value, true)} cr
    </span>
  )
}

function SortHeader({
  label,
  sortKey,
  current,
  dir,
  onSort,
}: {
  label: string
  sortKey: SortKey
  current: SortKey
  dir: SortDir
  onSort: (key: SortKey) => void
}) {
  const active = current === sortKey
  return (
    <th
      className="px-4 py-2 cursor-pointer select-none hover:text-foreground transition-colors"
      onClick={() => onSort(sortKey)}
      aria-sort={active ? (dir === 'asc' ? 'ascending' : 'descending') : 'none'}
    >
      {label}
      {active && (
        <span className="ml-1 text-xs" aria-hidden>
          {dir === 'asc' ? '▲' : '▼'}
        </span>
      )}
    </th>
  )
}

export default function AssignmentsPanel() {
  const { data: assignments = [], isLoading } = useQuery<ShipAssignmentDto[]>({
    queryKey: ['fleet-assignments'],
    queryFn: () => apiFetch('/fleet/assignments'),
    refetchInterval: 10_000,
  })

  const { data: historyByShip = {}, isLoading: isHistoryLoading } = useQuery<Record<string, ShipGoalHistoryDto[]>>({
    queryKey: ['fleet-assignment-histories', assignments.map(a => a.shipSymbol).join('|')],
    enabled: assignments.length > 0,
    queryFn: async () => {
      const entries = await Promise.all(
        assignments.map(async assignment => {
          const history = await apiFetch<ShipGoalHistoryDto[]>(
            `/fleet/activity/${encodeURIComponent(assignment.shipSymbol)}/history?limit=3`,
          )
          return [assignment.shipSymbol, history] as const
        }),
      )

      return Object.fromEntries(entries)
    },
    refetchInterval: 15_000,
  })

  const [goalKindFilter, setGoalKindFilter] = useState('')
  const [sortKey, setSortKey] = useState<SortKey>('shipSymbol')
  const [sortDir, setSortDir] = useState<SortDir>('asc')

  const goalKinds = [...new Set(assignments.map(a => a.goalKind))].sort()

  const filtered = assignments.filter(
    a => !goalKindFilter || a.goalKind === goalKindFilter,
  )

  const sorted = [...filtered].sort((a, b) => {
    const av = (a[sortKey] ?? '') as string
    const bv = (b[sortKey] ?? '') as string
    const cmp = av.localeCompare(bv)
    return sortDir === 'asc' ? cmp : -cmp
  })

  function handleSort(key: SortKey) {
    if (sortKey === key) {
      setSortDir(d => (d === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortKey(key)
      setSortDir('asc')
    }
  }

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap gap-2 items-center">
        <select
          value={goalKindFilter}
          onChange={e => setGoalKindFilter(e.target.value)}
          aria-label="Filter by goal kind"
          className="rounded-md border border-border bg-background px-3 py-1.5 text-sm focus:outline-none focus:ring-2 focus:ring-primary"
        >
          <option value="">All goal kinds</option>
          {goalKinds.map(k => (
            <option key={k} value={k}>
              {k}
            </option>
          ))}
        </select>
        <span className="text-xs text-muted-foreground ml-auto">
          {sorted.length} of {assignments.length} ship{assignments.length !== 1 ? 's' : ''}
        </span>
      </div>

      <div className="overflow-x-auto rounded-lg border border-border">
        <table className="min-w-full text-sm">
          <thead>
            <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
              <SortHeader
                label="Ship"
                sortKey="shipSymbol"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <SortHeader
                label="Current goal"
                sortKey="goalKind"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <SortHeader
                label="Route"
                sortKey="destinationWaypoint"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <SortHeader
                label="Serving"
                sortKey="fleetGoalDescription"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <th className="px-4 py-2">Recent goals</th>
            </tr>
          </thead>
          <tbody>
            {sorted.length === 0 && (
              <tr>
                <td colSpan={5} className="px-4 py-8 text-center text-muted-foreground">
                  No assignments match the current filter.
                </td>
              </tr>
            )}
            {sorted.map(a => {
              const recentHistory = historyByShip[a.shipSymbol] ?? []

              return (
                <tr
                  key={a.shipSymbol}
                  className={cn(
                    'border-b border-border last:border-0 align-top transition-colors',
                    a.goalKind === 'Idle' ? 'text-muted-foreground' : 'hover:bg-accent/30',
                  )}
                >
                  <td className="px-4 py-3 font-mono">
                    <Link
                      to={`/fleet/${a.shipSymbol}`}
                      className="text-primary hover:underline"
                    >
                      {a.shipSymbol}
                    </Link>
                  </td>
                  <td className="px-4 py-3">
                    <div className="font-medium text-foreground">{a.goalDescription}</div>
                    <div className="mt-1 text-xs text-muted-foreground">
                      Status: {a.goalKind === 'Idle' ? 'Available' : 'In progress'}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      Started: {formatTimestamp(a.assignedAt)}
                    </div>
                  </td>
                  <td className="px-4 py-3 text-xs text-muted-foreground">
                    <div>From: {a.sourceWaypoint ?? '—'}</div>
                    <div>To: {a.destinationWaypoint ?? '—'}</div>
                  </td>
                  <td className="px-4 py-3 text-xs text-muted-foreground">
                    {a.fleetGoalDescription ?? '—'}
                  </td>
                  <td className="px-4 py-3">
                    {isHistoryLoading ? (
                      <p className="text-xs text-muted-foreground">Loading recent goal history…</p>
                    ) : recentHistory.length === 0 ? (
                      <p className="text-xs text-muted-foreground">No recent completed goals yet.</p>
                    ) : (
                      <ul className="space-y-2">
                        {recentHistory.map(goal => (
                          <li key={goal.id} className="rounded-md border border-border bg-background/60 p-2">
                            <div className="flex flex-wrap items-center gap-2">
                              <OutcomeBadge outcome={goal.outcome} />
                              <span className="text-xs font-medium text-foreground">{goal.goalKind}</span>
                              <NetCreditsBadge value={goal.netCredits} />
                            </div>
                            <div className="mt-1 text-xs text-muted-foreground">
                              Started {formatTimestamp(goal.startedAt)} · Finished {formatTimestamp(goal.endedAt)}
                            </div>
                            <div className="text-xs text-muted-foreground">
                              Duration {formatDuration(goal.durationSeconds)} · Earned {formatCredits(goal.creditsEarned, true)} cr · Spent {formatCredits(goal.creditsSpent)} cr · {goal.ledgerEntryCount} ledger entr{goal.ledgerEntryCount === 1 ? 'y' : 'ies'}
                            </div>
                            {goal.reason && (
                              <div className="text-xs text-muted-foreground">Reason: {goal.reason}</div>
                            )}
                          </li>
                        ))}
                      </ul>
                    )}
                  </td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
    </div>
  )
}
