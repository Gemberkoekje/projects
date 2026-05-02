/**
 * AssignmentsPanel – Phase 17c
 * Shows all ship assignments in a sortable table with a goal-kind filter.
 */
import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { ShipAssignmentDto } from '@/types'
import { cn } from '@/lib/utils'

type SortKey = keyof Pick<
  ShipAssignmentDto,
  'shipSymbol' | 'goalKind' | 'sourceWaypoint' | 'destinationWaypoint' | 'fleetGoalDescription'
>
type SortDir = 'asc' | 'desc'

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
      {/* Filter */}
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

      {/* Table */}
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
                label="Goal"
                sortKey="goalKind"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <SortHeader
                label="Source"
                sortKey="sourceWaypoint"
                current={sortKey}
                dir={sortDir}
                onSort={handleSort}
              />
              <SortHeader
                label="Destination"
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
            {sorted.map(a => (
              <tr
                key={a.shipSymbol}
                className={cn(
                  'border-b border-border last:border-0 transition-colors',
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
                <td className="px-4 py-3">{a.goalDescription}</td>
                <td className="px-4 py-3 text-muted-foreground">{a.sourceWaypoint ?? '—'}</td>
                <td className="px-4 py-3 text-muted-foreground">
                  {a.destinationWaypoint ?? '—'}
                </td>
                <td className="px-4 py-3 text-muted-foreground">
                  {a.fleetGoalDescription ?? '—'}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  )
}
