import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode, useEffect } from 'react'
import { createPortal } from 'react-dom'
import { createRoot } from 'react-dom/client'
import { BrowserRouter } from 'react-router-dom'
import { Toaster } from 'sonner'
import App from './App.tsx'
import { ThemeProvider } from '@/components/theme-provider'
import { TooltipProvider } from '@/components/ui/tooltip'
import './index.css'
import { queryClient } from '@/lib/query-client'
import { useDensityStore } from '@/state/density-store'

function DensityInit() {
  useEffect(() => {
    document.documentElement.setAttribute('data-density', useDensityStore.getState().density)
  }, [])
  return null
}

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <ThemeProvider>
      <QueryClientProvider client={queryClient}>
        <TooltipProvider delayDuration={300}>
          <DensityInit />
          <BrowserRouter basename="/querybuilder">
            <App />
          </BrowserRouter>
          {/* Outside #root, which is inert while the results grid is in full screen and would mute the toasts. */}
          {createPortal(<Toaster richColors position="bottom-right" />, document.body)}
        </TooltipProvider>
      </QueryClientProvider>
    </ThemeProvider>
  </StrictMode>,
)
