import { createContext, useContext } from 'react'

export type ConnectionState = 'connecting' | 'connected' | 'reconnecting' | 'disconnected'

export interface SignalRContextValue {
  /** Raw SignalR connection state. */
  state: ConnectionState
  /** True when live updates are not flowing because SignalR is not connected. */
  liveUpdatesPaused: boolean
}

export const SignalRContext = createContext<SignalRContextValue>({
  state: 'connecting',
  liveUpdatesPaused: true,
})

export function useSignalR() {
  return useContext(SignalRContext)
}
