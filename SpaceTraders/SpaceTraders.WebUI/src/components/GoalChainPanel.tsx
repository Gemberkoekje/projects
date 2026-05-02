/**
 * GoalChainPanel – Phase 17b
 * Shows the orchestrator's active fleet goal chains, sorted by priority.
 * Each chain is rendered as a collapsible card with resource-need progress bars.
 */
import { useQuery } from '@tanstack/react-query'
import { Link } from 'react-router'
import { apiFetch } from '@/lib/api-fetch'
import type { OrchestratorGoalChainDto } from '@/types'

function GoalKindBadge({ kind }: { kind: string }) {
  return (
    <span className="inline-block rounded-full bg-primary/15 text-primary px-2 py-0.5 text-xs font-semibold uppercase tracking-wide">
      {kind}
    </span>
  )
}

export default function GoalChainPanel() {
  const { data: chains = [], isLoading } = useQuery<OrchestratorGoalChainDto[]>({
    queryKey: ['fleet-goal-chains'],
    queryFn: () => apiFetch('/fleet/goal-chains'),
    refetchInterval: 10_000,
  })

  const sorted = [...chains].sort((a, b) => a.priority - b.priority)

  if (isLoading) {
    return <p className="text-sm text-muted-foreground">Loading…</p>
  }

  if (sorted.length === 0) {
    return <p className="text-sm text-muted-foreground">No active goals.</p>
  }

  return (
    <div className="flex flex-col gap-3">
      {sorted.map(chain => (
        <details
          key={chain.fleetGoalId}
          className="rounded-lg border border-border bg-background"
          open
        >
          <summary className="flex cursor-pointer items-center gap-2 px-4 py-3 select-none">
            <GoalKindBadge kind={chain.fleetGoalKind} />
            <span className="text-sm font-medium">{chain.fleetGoalDescription}</span>
          </summary>

          {chain.resourceNeeds.length > 0 && (
            <div className="border-t border-border px-4 pb-3 pt-2 flex flex-col gap-2">
              {chain.resourceNeeds.map((need, idx) => {
                const pct =
                  need.unitsNeeded > 0
                    ? Math.round((need.unitsDelivered / need.unitsNeeded) * 100)
                    : 0
                return (
                  <div key={idx} className="flex flex-col gap-1 text-sm">
                    <div className="flex items-center justify-between gap-2">
                      <span className="rounded bg-muted px-1.5 py-0.5 text-xs font-mono font-semibold">
                        {need.tradeSymbol}
                      </span>
                      <span className="text-xs text-muted-foreground tabular-nums">
                        {need.unitsDelivered}/{need.unitsNeeded}
                      </span>
                    </div>
                    <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                      <div
                        className="h-full rounded-full bg-primary transition-all"
                        style={{ width: `${pct}%` }}
                      />
                    </div>
                    <div className="flex items-center justify-between gap-2">
                      <span className="text-xs text-muted-foreground">{need.purposeDescription}</span>
                      {need.assignedShips.length > 0 && (
                        <div className="flex flex-wrap gap-1">
                          {need.assignedShips.map(symbol => (
                            <Link
                              key={symbol}
                              to={`/fleet/${symbol}`}
                              className="rounded-full bg-accent text-accent-foreground px-2 py-0.5 text-xs font-mono hover:bg-accent/80 transition-colors"
                            >
                              {symbol}
                            </Link>
                          ))}
                        </div>
                      )}
                    </div>
                  </div>
                )
              })}
            </div>
          )}
        </details>
      ))}
    </div>
  )
}
