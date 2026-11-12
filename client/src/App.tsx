import type { ReactNode } from 'react'
import { Loader2 } from 'lucide-react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { AppShell } from '@/components/layout/AppShell'
import { ChooseAppPage } from '@/features/hub/ChooseAppPage'
import { NotAvailablePage } from '@/features/hub/NotAvailablePage'
import { canSee, homeRouteFor, isHub, needsAppChoice } from '@/features/hub/permissions'
import { useMe } from '@/hooks/useMe'
import { AdminDataSourcePolicyPage } from '@/pages/AdminDataSourcePolicyPage'
import { AdminDataSourcesPage } from '@/pages/AdminDataSourcesPage'
import { DashboardListPage } from '@/pages/DashboardListPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { QueryEditorPage } from '@/pages/QueryEditorPage'
import { QueryListPage } from '@/pages/QueryListPage'

function Loading() {
  return (
    <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
      <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
    </div>
  )
}

/**
 * Hides a page from a role that may not use it. The server enforces this too (a Viewer's API call is a 403);
 * this only stops the shell from drawing screens that would fail. Embedded mode passes everything through.
 */
function Gate({ children }: { children: ReactNode }) {
  const { data: me, isLoading } = useMe()
  const { pathname } = useLocation()
  if (isLoading) return <Loading />
  if (canSee(pathname, me)) return <>{children}</>
  return needsAppChoice(me) ? <Navigate to="/choose-app" replace /> : <NotAvailablePage />
}

/** Only exists under the hub; in embedded mode it is an unknown route, as before. */
function ChooseApp() {
  const { data: me, isLoading } = useMe()
  if (isLoading) return <Loading />
  if (!isHub(me)) return null
  return me?.isSuperAdmin ? <ChooseAppPage /> : <NotAvailablePage />
}

function SessionEnded() {
  return (
    <div className="flex h-full flex-col items-center justify-center gap-2 p-6 text-center">
      <h1 className="text-lg font-semibold">Your session has ended</h1>
      <p className="text-sm text-muted-foreground">Go back to your app and open QueryBuilder again.</p>
    </div>
  )
}

/** The hub answers 401 to everything once its session cookie is gone, /api/me included. */
function SessionGuard({ children }: { children: ReactNode }) {
  const { error } = useMe()
  const status = (error as { response?: { status?: number } } | null)?.response?.status
  return status === 401 ? <SessionEnded /> : <>{children}</>
}

function Home() {
  const { data: me, isLoading } = useMe()
  if (isLoading) return <Loading />
  const home = homeRouteFor(me)
  return home === '/' ? <QueryListPage /> : <Navigate to={home} replace />
}

export default function App() {
  return (
    <AppShell>
      <SessionGuard>
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/choose-app" element={<ChooseApp />} />
        <Route path="/admin" element={<Gate><AdminDataSourcesPage /></Gate>} />
        <Route path="/admin/data-sources/:id" element={<Gate><AdminDataSourcePolicyPage /></Gate>} />
        <Route path="/dashboards" element={<Gate><DashboardListPage /></Gate>} />
        <Route path="/dashboards/:id" element={<Gate><DashboardPage /></Gate>} />
        <Route path="/queries/new" element={<Gate><QueryEditorPage /></Gate>} />
        <Route path="/queries/:id" element={<Gate><QueryEditorPage /></Gate>} />
      </Routes>
      </SessionGuard>
    </AppShell>
  )
}
