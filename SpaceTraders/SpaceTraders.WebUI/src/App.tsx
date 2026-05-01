import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter } from 'react-router'
import { queryClient } from '@/lib/query-client'
import { ThemeProvider } from '@/lib/theme'
import { SignalRProvider } from '@/lib/signalr'
import { AppShell } from '@/components/layout/AppShell'

/** Base path the app is served under — must match the Vite `base` config. */
const APP_BASE = '/spacetraders/dashboard'

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <SignalRProvider>
          <BrowserRouter basename={APP_BASE}>
            <AppShell />
          </BrowserRouter>
        </SignalRProvider>
      </ThemeProvider>
    </QueryClientProvider>
  )
}
