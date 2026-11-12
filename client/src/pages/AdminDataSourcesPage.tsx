import { providerLabel } from '@/features/catalog/unavailable'
import { Loader2, Lock, Plus } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { AddDataSourceDialog } from '@/features/admin/AddDataSourceDialog'
import { EncryptionKeySection } from '@/features/admin/EncryptionKeySection'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { isHub } from '@/features/hub/permissions'
import { useMe } from '@/hooks/useMe'
import { useAdminDataSources, useDataSourceManagement } from '@/hooks/useAdmin'

export function AdminDataSourcesPage() {
  const { data, isLoading, isError } = useAdminDataSources()
  const navigate = useNavigate()
  const management = useDataSourceManagement()
  const [adding, setAdding] = useState(false)
  const { data: me } = useMe()
  const hub = isHub(me)
  // Hub mode: whether Add shows depends on the caller; the key section is hub-wide, so only super admins see it.
  const canAdd = !!management.data?.enabled && (!hub || management.data.canManageConnections === true)

  if (isError) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Couldn't load the data sources — you may not have access, or they may not exist.
      </div>
    )
  }

  if (isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading data sources…
      </div>
    )
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight">Admin — Data Sources</h1>
          <p className="text-sm text-muted-foreground">Control which tables and views each data source exposes to the catalog.</p>
        </div>
        {canAdd && (
          <Button onClick={() => setAdding(true)}>
            <Plus className="size-3.5" />
            Add data source
          </Button>
        )}
      </div>

      {management.data && !management.data.enabled && !hub && (
        <p className="text-xs text-muted-foreground">
          The host app can turn on adding and editing data sources here with <code>DataSourceManagement</code>.
        </p>
      )}

      <AddDataSourceDialog open={adding} onOpenChange={setAdding} />

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
                <TableCell className="font-medium">
                  <span className="flex min-w-0 items-center gap-1.5">
                    <span className="truncate">{ds.name}</span>
                    {ds.locked && (
                      <Badge variant="secondary" className="h-4 gap-0.5 px-1 text-[9px] font-medium">
                        <Lock className="size-2.5" aria-hidden />
                        Locked
                      </Badge>
                    )}
                  </span>
                </TableCell>
                <TableCell className="text-xs text-muted-foreground">{providerLabel(ds.provider)}</TableCell>
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

      {(!hub || me?.isSuperAdmin) && <EncryptionKeySection />}
    </div>
  )
}
