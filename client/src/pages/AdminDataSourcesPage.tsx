import { Loader2 } from 'lucide-react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { useAdminDataSources } from '@/hooks/useAdmin'

export function AdminDataSourcesPage() {
  const { data, isLoading } = useAdminDataSources()
  const navigate = useNavigate()

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading data sources…
      </div>
    )
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div>
        <h1 className="text-xl font-semibold tracking-tight">Admin — Data Sources</h1>
        <p className="text-sm text-muted-foreground">Control which tables and views each data source exposes to the catalog.</p>
      </div>

      <div className="overflow-hidden rounded-lg border border-border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Name</TableHead>
              <TableHead>Provider</TableHead>
              <TableHead>Status</TableHead>
            </TableRow>
          </TableHeader>
          <TableBody>
            {(data ?? []).map((ds) => (
              <TableRow key={ds.id} className="cursor-pointer" onClick={() => navigate(`/admin/data-sources/${ds.id}`)}>
                <TableCell className="font-medium">{ds.name}</TableCell>
                <TableCell className="text-xs text-muted-foreground">{ds.provider}</TableCell>
                <TableCell>
                  <Badge variant={ds.isActive ? 'secondary' : 'outline'} className="text-xs font-normal">
                    {ds.isActive ? 'Active' : 'Inactive'}
                  </Badge>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>
      </div>
    </div>
  )
}
