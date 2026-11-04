import { formatDistanceToNow } from 'date-fns'
import { LayoutDashboard, MoreHorizontal, Plus, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { Skeleton } from '@/components/ui/skeleton'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { ConfirmDeleteDialog } from '@/features/dashboards/ConfirmDeleteDialog'
import { DashboardNameDialog } from '@/features/dashboards/DashboardNameDialog'
import { AccessLevelBadge } from '@/features/builder/ShareDialog'
import { useCreateDashboard, useDashboards, useDeleteDashboard } from '@/hooks/useDashboards'
import type { DashboardSummaryDto } from '@/types'

export function DashboardListPage() {
  const { data: dashboards, isLoading, isError } = useDashboards()
  const [createOpen, setCreateOpen] = useState(false)
  const [deleteTarget, setDeleteTarget] = useState<DashboardSummaryDto | null>(null)
  const navigate = useNavigate()
  const create = useCreateDashboard()
  const remove = useDeleteDashboard()

  return (
    <div className="mx-auto flex max-w-6xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight">Dashboards</h1>
          <p className="text-sm text-muted-foreground">Put saved charts on one page and share it with colleagues.</p>
        </div>
        <Button onClick={() => setCreateOpen(true)}>
          <Plus className="size-4" />
          New dashboard
        </Button>
      </div>

      {isLoading && (
        <div className="flex flex-col gap-2">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-(--row-h) w-full" />
          ))}
        </div>
      )}

      {isError && <p className="text-sm text-destructive">Could not load your dashboards.</p>}

      {dashboards && dashboards.length === 0 && (
        <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border py-16 text-center">
          <LayoutDashboard className="size-8 text-muted-foreground" />
          <div>
            <p className="text-sm font-medium">No dashboards yet</p>
            <p className="text-xs text-muted-foreground">Create one, then add charts from your saved queries.</p>
          </div>
          <Button size="sm" onClick={() => setCreateOpen(true)}>
            <Plus className="size-3.5" />
            New dashboard
          </Button>
        </div>
      )}

      {dashboards && dashboards.length > 0 && (
        <div className="overflow-hidden rounded-lg border border-border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Owner</TableHead>
                <TableHead>Tiles</TableHead>
                <TableHead>Updated</TableHead>
                <TableHead className="w-8" />
              </TableRow>
            </TableHeader>
            <TableBody>
              {dashboards.map((d) => (
                <TableRow key={d.id} className="cursor-pointer" onClick={() => navigate(`/dashboards/${d.id}`)}>
                  <TableCell>
                    <span className="flex min-w-0 items-center gap-1.5 text-(length:--text-body) font-medium">
                      <span className="truncate">{d.name}</span>
                      <AccessLevelBadge level={d.accessLevel} />
                    </span>
                  </TableCell>
                  <TableCell className="text-xs text-muted-foreground">{d.isOwner ? 'You' : d.ownerName}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">{d.tileCount}</TableCell>
                  <TableCell className="text-xs text-muted-foreground">
                    {formatDistanceToNow(new Date(d.updatedAtUtc ?? d.createdAtUtc), { addSuffix: true })}
                  </TableCell>
                  <TableCell onClick={(e) => e.stopPropagation()}>
                    {d.isOwner && (
                      <DropdownMenu>
                        <DropdownMenuTrigger asChild>
                          <Button variant="ghost" size="icon" className="size-6" aria-label={`Actions for ${d.name}`}>
                            <MoreHorizontal className="size-3.5" />
                          </Button>
                        </DropdownMenuTrigger>
                        <DropdownMenuContent align="end">
                          <DropdownMenuItem variant="destructive" onSelect={() => setDeleteTarget(d)}>
                            <Trash2 className="size-3.5" />
                            Delete
                          </DropdownMenuItem>
                        </DropdownMenuContent>
                      </DropdownMenu>
                    )}
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      <DashboardNameDialog
        open={createOpen}
        onOpenChange={setCreateOpen}
        title="New dashboard"
        confirmLabel="Create"
        initialName=""
        pending={create.isPending}
        onConfirm={(name) =>
          create.mutate(
            { name },
            {
              onSuccess: (created) => {
                setCreateOpen(false)
                navigate(`/dashboards/${created.id}`, { state: { edit: true } })
              },
            },
          )
        }
      />

      <ConfirmDeleteDialog
        open={!!deleteTarget}
        onOpenChange={(open) => !open && setDeleteTarget(null)}
        name={deleteTarget?.name ?? ''}
        pending={remove.isPending}
        onConfirm={() => deleteTarget && remove.mutate(deleteTarget.id, { onSuccess: () => setDeleteTarget(null) })}
      />
    </div>
  )
}
