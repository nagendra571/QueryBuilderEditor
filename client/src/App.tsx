import { Route, Routes } from 'react-router-dom'
import { AppShell } from '@/components/layout/AppShell'
import { AdminDataSourcePolicyPage } from '@/pages/AdminDataSourcePolicyPage'
import { AdminDataSourcesPage } from '@/pages/AdminDataSourcesPage'
import { DashboardListPage } from '@/pages/DashboardListPage'
import { DashboardPage } from '@/pages/DashboardPage'
import { QueryEditorPage } from '@/pages/QueryEditorPage'
import { QueryListPage } from '@/pages/QueryListPage'

export default function App() {
  return (
    <AppShell>
      <Routes>
        <Route path="/" element={<QueryListPage />} />
        <Route path="/admin" element={<AdminDataSourcesPage />} />
        <Route path="/admin/data-sources/:id" element={<AdminDataSourcePolicyPage />} />
        <Route path="/dashboards" element={<DashboardListPage />} />
        <Route path="/dashboards/:id" element={<DashboardPage />} />
        <Route path="/queries/new" element={<QueryEditorPage />} />
        <Route path="/queries/:id" element={<QueryEditorPage />} />
      </Routes>
    </AppShell>
  )
}
