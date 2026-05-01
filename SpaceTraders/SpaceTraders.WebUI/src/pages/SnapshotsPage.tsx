import { useState } from 'react'
import { useQuery } from '@tanstack/react-query'
import { apiFetch } from '@/lib/api-fetch'
import { config } from '@/config'
import type { StartupSnapshotListItemDto } from '@/types'

function formatTs(iso: string) {
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric',
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

export default function SnapshotsPage() {
  const [downloadingId, setDownloadingId] = useState<number | null>(null)

  const snapshotsQ = useQuery<StartupSnapshotListItemDto[]>({
    queryKey: ['startup-snapshots'],
    queryFn: () => apiFetch('/status/startup-snapshots'),
    refetchInterval: 60_000,
  })

  const snapshots = snapshotsQ.data ?? []

  const handleDownload = async (snapshot: StartupSnapshotListItemDto) => {
    setDownloadingId(snapshot.id)

    try {
      const response = await fetch(
        `${config.apiBaseUrl}/status/startup-snapshots/${snapshot.id}/download`,
        {
          headers: {
            'X-Api-Key': config.dashboardApiKey,
          },
        },
      )

      if (!response.ok) {
        throw new Error(`Download failed (${response.status})`)
      }

      const blob = await response.blob()
      const url = URL.createObjectURL(blob)
      const a = document.createElement('a')
      const capturedAt = new Date(snapshot.capturedAt)
      const ts = `${capturedAt.getUTCFullYear()}${String(capturedAt.getUTCMonth() + 1).padStart(2, '0')}${String(capturedAt.getUTCDate()).padStart(2, '0')}-${String(capturedAt.getUTCHours()).padStart(2, '0')}${String(capturedAt.getUTCMinutes()).padStart(2, '0')}${String(capturedAt.getUTCSeconds()).padStart(2, '0')}`
      const initialSuffix = snapshot.isInitialSnapshot ? '-initial' : ''
      a.href = url
      a.download = `startup-snapshot-${snapshot.id}-${ts}${initialSuffix}.json`
      document.body.append(a)
      a.click()
      a.remove()
      URL.revokeObjectURL(url)
    } finally {
      setDownloadingId(null)
    }
  }

  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Snapshots</h1>
        <span className="rounded-full bg-muted text-muted-foreground px-3 py-1 text-xs font-medium">
          Read-only exports from database snapshots
        </span>
      </div>

      <section aria-label="Startup snapshots">
        {snapshotsQ.isLoading && <p className="text-muted-foreground text-sm">Loading…</p>}

        {snapshots.length === 0 && !snapshotsQ.isLoading && (
          <p className="text-muted-foreground text-sm">No snapshots found.</p>
        )}

        {snapshots.length > 0 && (
          <div className="overflow-x-auto rounded-lg border border-border">
            <table className="min-w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/50 text-left text-xs uppercase tracking-wide text-muted-foreground">
                  <th className="px-4 py-2">Captured</th>
                  <th className="px-4 py-2">Type</th>
                  <th className="px-4 py-2">Download</th>
                </tr>
              </thead>
              <tbody>
                {snapshots.map(snapshot => (
                  <tr
                    key={snapshot.id}
                    className="border-b border-border last:border-0 hover:bg-accent/30 transition-colors"
                  >
                    <td className="px-4 py-2 text-xs tabular-nums whitespace-nowrap">
                      {formatTs(snapshot.capturedAt)}
                    </td>
                    <td className="px-4 py-2 text-xs text-muted-foreground whitespace-nowrap">
                      {snapshot.isInitialSnapshot ? 'Initial' : 'Regular'}
                    </td>
                    <td className="px-4 py-2">
                      <button
                        type="button"
                        onClick={() => void handleDownload(snapshot)}
                        disabled={downloadingId === snapshot.id}
                        className="inline-flex items-center rounded-md border border-border px-3 py-1.5 text-xs font-medium hover:bg-accent transition-colors disabled:opacity-40"
                      >
                        {downloadingId === snapshot.id ? 'Downloading…' : 'Download JSON'}
                      </button>
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
