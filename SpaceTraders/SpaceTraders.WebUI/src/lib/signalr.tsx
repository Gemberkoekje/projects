import { useEffect, useState, type ReactNode } from 'react'
import * as signalR from '@microsoft/signalr'
import { useQueryClient } from '@tanstack/react-query'
import { config } from '@/config'
import { SignalRContext } from '@/lib/signalr-context'
import type { ConnectionState } from '@/lib/signalr-context'

/** Reconnect retry delays (ms): immediate, then 2 s, 5 s, 10 s, 30 s. */
const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000]

/**
 * Inner component that can use `useQueryClient` because it is always rendered
 * inside `<QueryClientProvider>`.
 */
function SignalRProviderInner({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [state, setState] = useState<ConnectionState>('connecting')

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(`${config.hubBaseUrl}/dashboard`, {
        headers: { 'X-Api-Key': config.dashboardApiKey },
      })
      .withAutomaticReconnect(RECONNECT_DELAYS)
      .build()

    connection.on('ReceiveInvalidation', (kind: string) => {
      queryClient.invalidateQueries({ queryKey: [kind] })
    })

    connection.onreconnecting(() => {
      setState('reconnecting')
    })

    connection.onreconnected(() => {
      setState('connected')
      // Re-fetch everything currently mounted after a reconnect so stale
      // data is not shown silently.
      queryClient.invalidateQueries()
    })

    connection.onclose(() => {
      setState('disconnected')
    })

    connection
      .start()
      .then(() => {
        setState('connected')
      })
      .catch(() => {
        setState('disconnected')
      })

    return () => {
      connection.stop()
    }
  }, [queryClient])

  const liveUpdatesPaused = state !== 'connected'

  return (
    <SignalRContext.Provider value={{ state, liveUpdatesPaused }}>
      {children}
    </SignalRContext.Provider>
  )
}

export function SignalRProvider({ children }: { children: ReactNode }) {
  return <SignalRProviderInner>{children}</SignalRProviderInner>
}

export { useSignalR } from '@/lib/signalr-context'
