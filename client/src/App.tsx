import type { ReactNode } from 'react'
import { Loader2 } from 'lucide-react'
import { Navigate, Route, Routes, useLocation } from 'react-router-dom'
import { AppShell } from '@/components/layout/AppShell'
import { ChooseAppPage } from '@/features/hub/ChooseAppPage'
import { NotAvailablePage } from '@/features/hub/NotAvailablePage'
import { canSee, homeRouteFor, needsAppChoice } from '@/features/hub/permissions'
import { useMe } from '@/hooks/useMe'
import { AdminDataSourcePolicyPage } from '@/pages/AdminDataSourcePolicyPage'
import { AdminDataSourcesPage } from '@/pages/AdminDataSourcesPage'
import { DashboardListPage } from '@/pages/DashboardListPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { QueryEditorPage } from '@/pages/QueryEditorPage'
import { QueryListPage } from '@/pages/QueryListPage'

/**
 * Hides a page from a role that may not use it. The server enforces this too (a Viewer's API call is a 403);
 * this only stops the shell from drawing screens that would fail. Embedded mode passes everything through.
 */
function Loading() {
  return (
    <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
      <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
    </div>
  )
}

function Gate({ children }: { children: ReactNode }) {
  const { data: me, isLoading } = useMe()
  const { pathname } = useLocation()
  if (isLoading) return <Loading />
  if (canSee(pathname, me)) return <>{children}</>
  return needsAppChoice(me) ? <Navigate to="/choose-app" replace /> : <NotAvailablePage />
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
      <Routes>
        <Route path="/" element={<Home />} />
        <Route path="/choose-app" element={<Gate><ChooseAppPage /></Gate>} />
        <Route path="/admin" element={<Gate><AdminDataSourcesPage /></Gate>} />
        <Route path="/admin/data-sources/:id" element={<Gate><AdminDataSourcePolicyPage /></Gate>} />
        <Route path="/dashboards" element={<Gate><DashboardListPage /></Gate>} />
        <Route path="/dashboards/:id" element={<Gate><DashboardPage /></Gate>} />
        <Route path="/queries/new" element={<Gate><QueryEditorPage /></Gate>} />
        <Route path="/queries/:id" element={<Gate><QueryEditorPage /></Gate>} />
      </Routes>
    </AppShell>
  )
}
