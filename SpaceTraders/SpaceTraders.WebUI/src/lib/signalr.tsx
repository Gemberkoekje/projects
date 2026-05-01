import * as signalR from '@microsoft/signalr'
import {
  createContext,
  useContext,
  useEffect,
  useRef,
  useState,
  type ReactNode,
} from 'react'
import { useQueryClient } from '@tanstack/react-query'
import { config } from '@/config'

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

interface SignalRContextValue {
  /** Raw SignalR connection state. */
  state: ConnectionState
  /**
   * True when live updates are not flowing — either the connection is not
   * established or the server heartbeat has not been received for >30 s.
   * When true the UI shows a "live updates paused" banner.
   */
  liveUpdatesPaused: boolean
}

const SignalRContext = createContext<SignalRContextValue>({
  state: 'connecting',
  liveUpdatesPaused: true,
})

export function useSignalR() {
  return useContext(SignalRContext)
}

/** Reconnect retry delays (ms): immediate, then 2 s, 5 s, 10 s, 30 s. */
const RECONNECT_DELAYS = [0, 2_000, 5_000, 10_000, 30_000]

/** If no heartbeat is received within this window the banner is shown. */
const HEARTBEAT_TIMEOUT_MS = 30_000

/**
 * Inner component that can use `useQueryClient` because it is always rendered
 * inside `<QueryClientProvider>`.
 */
function SignalRProviderInner({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const [state, setState] = useState<ConnectionState>('connecting')
  const lastHeartbeatRef = useRef<number>(Date.now())
  const [liveUpdatesPaused, setLiveUpdatesPaused] = useState(true)

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

    connection.on('Heartbeat', () => {
      lastHeartbeatRef.current = Date.now()
    })

    connection.onreconnecting(() => {
      setState('reconnecting')
    })

    connection.onreconnected(() => {
      setState('connected')
      lastHeartbeatRef.current = Date.now()
      // Re-fetch everything currently mounted after a reconnect so stale
      // data is not shown silently.
      queryClient.invalidateQueries()
    })

    connection.onclose(() => {
      setState('disconnected')
    })

    setState('connecting')
    connection
      .start()
      .then(() => {
        setState('connected')
        lastHeartbeatRef.current = Date.now()
      })
      .catch(() => {
        setState('disconnected')
      })

    return () => {
      connection.stop()
    }
  }, [queryClient])

  // Poll every 5 s to update the "paused" banner based on heartbeat age and
  // connection state.
  useEffect(() => {
    const id = setInterval(() => {
      const heartbeatStale = Date.now() - lastHeartbeatRef.current > HEARTBEAT_TIMEOUT_MS
      setLiveUpdatesPaused(state !== 'connected' || heartbeatStale)
    }, 5_000)

    // Set initial value immediately when state changes.
    const heartbeatStale = Date.now() - lastHeartbeatRef.current > HEARTBEAT_TIMEOUT_MS
    setLiveUpdatesPaused(state !== 'connected' || heartbeatStale)

    return () => clearInterval(id)
  }, [state])

  return (
    <SignalRContext.Provider value={{ state, liveUpdatesPaused }}>
      {children}
    </SignalRContext.Provider>
  )
}

export function SignalRProvider({ children }: { children: ReactNode }) {
  return <SignalRProviderInner>{children}</SignalRProviderInner>
}
