import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import { cn } from '@/lib/utils'
import type { ContractDto, ContractDeliverableDto, ContractRoiDto } from '@/types'

function formatCredits(n: number) {
  return new Intl.NumberFormat(undefined, { maximumFractionDigits: 0 }).format(n)
}

function formatRelative(iso: string | null) {
  if (!iso) return '—'
  const ms = new Date(iso).getTime() - Date.now()
  const abs = Math.abs(ms)
  const h = Math.floor(abs / 3_600_000)
  const m = Math.floor((abs % 3_600_000) / 60_000)
  if (ms < 0) {
    if (h > 0) return `${h}h ${m}m ago`
    return `${m}m ago`
  }
  if (h > 0) return `${h}h ${m}m remaining`
  return `${m}m remaining`
}

function contractStatus(c: ContractDto) {
  if (c.isFulfilled) return 'Fulfilled'
  if (!c.isAccepted) return 'Pending'
  if (c.termsDeadline && new Date(c.termsDeadline) < new Date()) return 'Expired'
  return 'Active'
}

function StatusBadge({ status }: { status: string }) {
  const cls =
    status === 'Active'
      ? 'bg-status-green/15 text-status-green'
      : status === 'Fulfilled'
        ? 'bg-primary/15 text-primary'
        : status === 'Expired'
          ? 'bg-destructive/15 text-destructive'
          : 'bg-muted text-muted-foreground'
  return (
    <span className={cn('inline-flex items-center rounded-full px-2 py-0.5 text-xs font-medium', cls)}>
      {status}
    </span>
  )
}

function ContractRoiSection({ contractId }: { contractId: string }) {
  const roiQ = useQuery<ContractRoiDto>({
    queryKey: ['contract-roi', contractId],
    queryFn: () => apiFetch(`/contracts/${contractId}/roi`),
    staleTime: 300_000,
    retry: false,
  })

  if (roiQ.isLoading) return <p className="text-xs text-muted-foreground">Loading ROI…</p>
  if (roiQ.isError || !roiQ.data) return null

  const roi = roiQ.data
  return (
    <div className="flex gap-4 text-xs text-muted-foreground border-t border-border pt-2 mt-1">
      <span>
        Payout:{' '}
        <span className="text-status-green font-medium">{formatCredits(roi.totalPayout)}</span>
      </span>
      <span>
        Fuel est.:{' '}
        <span className="text-destructive font-medium">{formatCredits(roi.estimatedFuelCost)}</span>
      </span>
      <span>
        Net ROI:{' '}
        <span className={cn('font-medium', roi.isProfitable ? 'text-status-green' : 'text-destructive')}>
          {roi.netRoi >= 0 ? '+' : ''}
          {formatCredits(roi.netRoi)}
        </span>
      </span>
    </div>
  )
}

export default function ContractsPage() {
  const contractsQ = useQuery<ContractDto[]>({
    queryKey: ['contracts-all'],
    queryFn: () => apiFetch('/status/contracts'),
    staleTime: 60_000,
  })

  const contracts = contractsQ.data ?? []
  const isLoading = contractsQ.isLoading

  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-2xl font-bold">Contracts</h1>

      {isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

      {!isLoading && contracts.length === 0 && (
        <p className="text-muted-foreground text-sm">No contracts found.</p>
      )}

      {contracts.map(c => {
        const status = contractStatus(c)
        const deliverables: ContractDeliverableDto[] = c.deliverablesJson
          ? (JSON.parse(c.deliverablesJson) as ContractDeliverableDto[])
          : []

        return (
          <div key={c.id} className="rounded-lg border border-border bg-background p-4 flex flex-col gap-3">
            {/* Header */}
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-2">
                <span className="font-mono text-sm font-medium">{c.id}</span>
                <StatusBadge status={status} />
              </div>
              <div className="flex gap-2 text-xs text-muted-foreground">
                <span>{c.type}</span>
                <span>·</span>
                <span>{c.factionSymbol}</span>
              </div>
            </div>

            {/* Deadline */}
            {c.termsDeadline && (
              <p className="text-xs text-muted-foreground">
                Deadline: {formatRelative(c.termsDeadline)}
              </p>
            )}

            {/* Deliverables */}
            {deliverables.length > 0 && (
              <div className="flex flex-col gap-2">
                {deliverables.map(d => {
                  const pct =
                    d.unitsRequired > 0
                      ? Math.round((d.unitsFulfilled / d.unitsRequired) * 100)
                      : 0
                  return (
                    <div key={d.tradeSymbol} className="flex flex-col gap-0.5">
                      <div className="flex justify-between text-xs text-muted-foreground">
                        <span>
                          {d.tradeSymbol} → {d.destinationSymbol}
                        </span>
                        <span>
                          {d.unitsFulfilled}/{d.unitsRequired} ({pct}%)
                        </span>
                      </div>
                      <div className="h-1.5 rounded-full bg-muted overflow-hidden">
                        <div
                          className="h-full rounded-full bg-primary transition-all"
                          style={{ width: `${pct}%` }}
                        />
                      </div>
                    </div>
                  )
                })}
              </div>
            )}

            {/* ROI section */}
            <ContractRoiSection contractId={c.id} />
          </div>
        )
      })}
    </div>
  )
}
