import { describe, it, expect } from 'vitest'
import { render, screen } from '@testing-library/react'
import { QueryClientProvider, QueryClient } from '@tanstack/react-query'
import { SignalRProvider } from '../lib/signalr'
import { useSignalR } from '../lib/signalr-context'

function StateDisplay() {
  const { state, liveUpdatesPaused } = useSignalR()
  return (
    <div>
      <span data-testid="state">{state}</span>
      <span data-testid="paused">{String(liveUpdatesPaused)}</span>
    </div>
  )
}

function Wrapper({ children }: { children: React.ReactNode }) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return <QueryClientProvider client={client}>{children}</QueryClientProvider>
}

describe('SignalRProvider', () => {
  it('renders children', () => {
    render(
      <Wrapper>
        <SignalRProvider>
          <p>hello</p>
        </SignalRProvider>
      </Wrapper>,
    )
    expect(screen.getByText('hello')).toBeInTheDocument()
  })

  it('exposes connection state via useSignalR', () => {
    render(
      <Wrapper>
        <SignalRProvider>
          <StateDisplay />
        </SignalRProvider>
      </Wrapper>,
    )
    // The mocked connection.start() resolves asynchronously; the initial state
    // is 'connecting' before the promise settles.
    const state = screen.getByTestId('state').textContent
    expect(['connecting', 'connected', 'disconnected']).toContain(state)
  })

  it('shows liveUpdatesPaused as true in the initial connecting state', () => {
    render(
      <Wrapper>
        <SignalRProvider>
          <StateDisplay />
        </SignalRProvider>
      </Wrapper>,
    )
    // On first render the state is 'connecting', so paused must be true.
    expect(screen.getByTestId('paused').textContent).toBe('true')
  })
})
