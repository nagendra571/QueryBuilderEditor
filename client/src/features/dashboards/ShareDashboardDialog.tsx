import { Loader2, X } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Command, CommandEmpty, CommandGroup, CommandInput, CommandItem, CommandList } from '@/components/ui/command'
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  useDashboardSharePreview,
  useDashboardShares,
  useRemoveDashboardShare,
  useShareDashboard,
} from '@/hooks/useDashboards'
import { useAppUsers, useDataSources, usePeople } from '@/hooks/useDataSources'
import { isHub } from '@/features/hub/permissions'
import { useMe } from '@/hooks/useMe'
import type { AppUserDto, QueryAccessLevel } from '@/types'

export function ShareDashboardDialog({
  open,
  onOpenChange,
  dashboardId,
  dashboardName,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  dashboardId: string
  dashboardName: string
}) {
  const { data: dataSources } = useDataSources()
  // A dashboard spans queries, so there is no single data source; the owner picks whose user list to search.
  const [chosenSource, setChosenSource] = useState<string | null>(null)
  const dataSourceId = chosenSource ?? dataSources?.[0]?.id

  // Under the hub people come from the app's launches; the server also accepts people from the AppUsers views of the
  // data sources behind the dashboard's charts, but this list does not offer them.
  const { data: me } = useMe()
  const hub = isHub(me)
  const directory = useAppUsers(hub ? undefined : dataSourceId)
  const people = usePeople(hub)
  const appUsers = hub ? (people.data ? { available: true, users: people.data } : undefined) : directory.data
  const usersLoading = hub ? people.isLoading : directory.isLoading
  const { data: shares, isLoading: sharesLoading } = useDashboardShares(open ? dashboardId : undefined)
  const shareMutation = useShareDashboard(dashboardId)
  const removeMutation = useRemoveDashboardShare(dashboardId)

  const [pendingUser, setPendingUser] = useState<AppUserDto | null>(null)
  const [pendingLevel, setPendingLevel] = useState<QueryAccessLevel>('viewer')
  const [alsoShare, setAlsoShare] = useState(false)

  const preview = useDashboardSharePreview(dashboardId, pendingUser?.id)
  const inaccessible = preview.data?.inaccessibleQueries ?? []
  const shareable = inaccessible.filter((q) => q.canShare)
  const notShareable = inaccessible.filter((q) => !q.canShare)

  const sharedIds = new Set((shares ?? []).map((s) => s.sharedWithUserId))
  const candidates = (appUsers?.users ?? []).filter((u) => !sharedIds.has(u.id))

  function reset() {
    setPendingUser(null)
    setAlsoShare(false)
  }

  function confirmShare() {
    if (!pendingUser) return
    shareMutation.mutate(
      {
        sharedWithUserId: pendingUser.id,
        sharedWithUserEmail: pendingUser.email ?? '',
        accessLevel: pendingLevel,
        alsoShareQueryIds: alsoShare ? shareable.map((q) => q.id) : [],
      },
      { onSuccess: reset },
    )
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next) reset()
        onOpenChange(next)
      }}
    >
      <DialogContent className="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>Share &quot;{dashboardName}&quot;</DialogTitle>
          <DialogDescription>
            Sharing a dashboard does not share the data. Each person sees a chart only if they already have access to its query.
          </DialogDescription>
        </DialogHeader>

        {(usersLoading || sharesLoading) && (
          <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
            <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
          </div>
        )}

        {appUsers && !appUsers.available && (
          <div className="rounded-md border border-dashed border-border p-4 text-center text-xs text-muted-foreground">
            Sharing isn&apos;t set up for this data source yet. Ask your admin to add a{' '}
            <code className="rounded bg-muted px-1 py-0.5">dbo.AppUsers</code> view (<code className="rounded bg-muted px-1 py-0.5">Id</code>,{' '}
            <code className="rounded bg-muted px-1 py-0.5">DisplayName</code>, <code className="rounded bg-muted px-1 py-0.5">Email</code>) on SQL Server, or a{' '}
            <code className="rounded bg-muted px-1 py-0.5">public.app_users</code> view (<code className="rounded bg-muted px-1 py-0.5">id</code>,{' '}
            <code className="rounded bg-muted px-1 py-0.5">display_name</code>, <code className="rounded bg-muted px-1 py-0.5">email</code>) on PostgreSQL.
          </div>
        )}

        {!hub && dataSources && dataSources.length > 1 && (
          <div className="flex items-center gap-2">
            <Label className="shrink-0 text-xs">People from</Label>
            <Select
              value={dataSourceId}
              onValueChange={(v) => {
                setChosenSource(v)
                reset()
              }}
            >
              <SelectTrigger size="sm" className="h-8 flex-1 text-xs">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {dataSources.map((ds) => (
                  <SelectItem key={ds.id} value={ds.id} className="text-xs">
                    {ds.name}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>
        )}

        {appUsers?.available && (
          <div className="flex flex-col gap-3">
            {!pendingUser && (
              <Command className="h-44 rounded-md border border-input">
                <CommandInput placeholder="Search people…" />
                <CommandList>
                  <CommandEmpty>No matching people.</CommandEmpty>
                  <CommandGroup>
                    {candidates.map((user) => (
                      <CommandItem
                        key={user.id}
                        value={`${user.displayName} ${user.email ?? ''}`}
                        onSelect={() => {
                          setPendingUser(user)
                          setAlsoShare(false)
                        }}
                      >
                        <div className="flex min-w-0 flex-col">
                          <span className="truncate">{user.displayName}</span>
                          {user.email && <span className="truncate text-xs text-muted-foreground">{user.email}</span>}
                        </div>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </CommandList>
              </Command>
            )}

            {pendingUser && (
              <div className="flex flex-col gap-3 rounded-md border border-border p-3">
                <div className="flex items-center gap-2">
                  <div className="flex min-w-0 flex-1 flex-col">
                    <span className="truncate text-sm font-medium">{pendingUser.displayName}</span>
                    {pendingUser.email && <span className="truncate text-xs text-muted-foreground">{pendingUser.email}</span>}
                  </div>
                  <Select value={pendingLevel} onValueChange={(v) => setPendingLevel(v as QueryAccessLevel)}>
                    <SelectTrigger size="sm" className="h-8 w-24 shrink-0 text-xs" aria-label="Access level">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="viewer" className="text-xs">
                        Viewer
                      </SelectItem>
                      <SelectItem value="editor" className="text-xs">
                        Editor
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>

                {preview.isLoading && <p className="text-xs text-muted-foreground">Checking which charts they can see…</p>}

                {inaccessible.length > 0 && (
                  <div className="flex flex-col gap-2 text-xs" aria-live="polite">
                    <p>
                      {inaccessible.length === 1 ? '1 chart uses a query' : `${inaccessible.length} charts use queries`}{' '}
                      {pendingUser.displayName} can&apos;t see. They will appear as restricted tiles:
                    </p>
                    <ul className="list-disc pl-5 text-muted-foreground">
                      {inaccessible.map((q) => (
                        <li key={q.id}>
                          {q.name}
                          {!q.canShare && ' (you don’t own this query)'}
                        </li>
                      ))}
                    </ul>
                    {shareable.length > 0 && (
                      <div className="flex items-center gap-2">
                        <Checkbox
                          id="also-share"
                          checked={alsoShare}
                          onCheckedChange={(checked) => setAlsoShare(checked === true)}
                        />
                        <Label htmlFor="also-share" className="text-xs font-normal">
                          Also share these {shareable.length} {shareable.length === 1 ? 'query' : 'queries'} as Viewer
                        </Label>
                      </div>
                    )}
                    {notShareable.length > 0 && shareable.length === 0 && (
                      <p className="text-muted-foreground">Only a query&apos;s owner can share it.</p>
                    )}
                  </div>
                )}

                <div className="flex justify-end gap-2">
                  <Button variant="outline" size="sm" onClick={reset}>
                    Back
                  </Button>
                  <Button size="sm" disabled={shareMutation.isPending || preview.isLoading} onClick={confirmShare}>
                    Share dashboard
                  </Button>
                </div>
              </div>
            )}

            <div className="flex flex-col gap-1.5">
              <span className="text-xs font-medium text-muted-foreground">
                Shared with {shares?.length ? shares.length : 'no one'}
              </span>
              {(shares ?? []).map((share) => (
                <div key={share.id} className="flex items-center gap-2 rounded-md border border-border bg-card px-2 py-1.5">
                  <span className="min-w-0 flex-1 truncate text-xs">{share.sharedWithUserEmail || share.sharedWithUserId}</span>
                  <Select
                    value={share.accessLevel}
                    onValueChange={(v) =>
                      shareMutation.mutate({
                        sharedWithUserId: share.sharedWithUserId,
                        sharedWithUserEmail: share.sharedWithUserEmail,
                        accessLevel: v as QueryAccessLevel,
                      })
                    }
                  >
                    <SelectTrigger size="sm" className="h-7 w-24 shrink-0 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem value="viewer" className="text-xs">
                        Viewer
                      </SelectItem>
                      <SelectItem value="editor" className="text-xs">
                        Editor
                      </SelectItem>
                    </SelectContent>
                  </Select>
                  <Button
                    variant="ghost"
                    size="icon"
                    className="size-6 shrink-0 text-destructive hover:text-destructive"
                    onClick={() => removeMutation.mutate(share.id)}
                    aria-label={`Remove ${share.sharedWithUserEmail}`}
                  >
                    <X className="size-3.5" />
                  </Button>
                </div>
              ))}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}
