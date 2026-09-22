import { AlertTriangle, Loader2, X } from 'lucide-react'
import { useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Command,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import { Dialog, DialogContent, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import { useAppUsers } from '@/hooks/useDataSources'
import { useQueryShares, useRemoveShare, useShareQuery } from '@/hooks/useSavedQueries'
import type { AppUserDto, QueryAccessLevel } from '@/types'

export function ShareDialog({
  open,
  onOpenChange,
  queryId,
  dataSourceId,
  queryName,
}: {
  open: boolean
  onOpenChange: (open: boolean) => void
  queryId: string
  dataSourceId: string
  queryName: string
}) {
  const { data: appUsers, isLoading: usersLoading } = useAppUsers(dataSourceId)
  const { data: shares, isLoading: sharesLoading } = useQueryShares(queryId)
  const shareMutation = useShareQuery(queryId)
  const removeMutation = useRemoveShare(queryId)
  const [pendingLevel, setPendingLevel] = useState<QueryAccessLevel>('viewer')

  const sharedIds = new Set((shares ?? []).map((s) => s.sharedWithUserId))
  const candidates = (appUsers?.users ?? []).filter((u) => !sharedIds.has(u.id))

  function handlePick(user: AppUserDto) {
    shareMutation.mutate({ sharedWithUserId: user.id, sharedWithUserEmail: user.email ?? '', accessLevel: pendingLevel })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Share "{queryName}"</DialogTitle>
        </DialogHeader>

        {(usersLoading || sharesLoading) && (
          <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
            <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
          </div>
        )}

        {appUsers && !appUsers.available && (
          <div className="rounded-md border border-dashed border-border p-4 text-center text-xs text-muted-foreground">
            Sharing isn't set up for this data source yet. Ask your admin to add a{' '}
            <code className="rounded bg-muted px-1 py-0.5">dbo.AppUsers</code> view there (
            <code className="rounded bg-muted px-1 py-0.5">Id</code>,{' '}
            <code className="rounded bg-muted px-1 py-0.5">DisplayName</code>,{' '}
            <code className="rounded bg-muted px-1 py-0.5">Email</code>).
          </div>
        )}

        {appUsers?.available && (
          <div className="flex flex-col gap-3">
            <div className="flex items-start gap-2">
              <Command className="h-48 flex-1 rounded-md border border-input">
                <CommandInput placeholder="Search people…" />
                <CommandList>
                  <CommandEmpty>No matching people.</CommandEmpty>
                  <CommandGroup>
                    {candidates.map((user) => (
                      <CommandItem key={user.id} value={`${user.displayName} ${user.email ?? ''}`} onSelect={() => handlePick(user)}>
                        <div className="flex min-w-0 flex-col">
                          <span className="truncate">{user.displayName}</span>
                          {user.email && <span className="truncate text-xs text-muted-foreground">{user.email}</span>}
                        </div>
                      </CommandItem>
                    ))}
                  </CommandGroup>
                </CommandList>
              </Command>

              <Select value={pendingLevel} onValueChange={(v) => setPendingLevel(v as QueryAccessLevel)}>
                <SelectTrigger className="w-28 shrink-0 text-xs">
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

            <div className="flex flex-col gap-1.5">
              <span className="text-xs font-medium text-muted-foreground">
                Shared with {shares?.length ? shares.length : 'no one'}
              </span>

              {(shares ?? []).map((share) => {
                const stillListed = appUsers.users.some((u) => u.id === share.sharedWithUserId)
                return (
                  <div key={share.id} className="flex items-center gap-2 rounded-md border border-border bg-card px-2 py-1.5">
                    <div className="flex min-w-0 flex-1 flex-col">
                      <span className="truncate text-xs">{share.sharedWithUserEmail || share.sharedWithUserId}</span>
                    </div>

                    {!stillListed && (
                      <Tooltip>
                        <TooltipTrigger asChild>
                          <AlertTriangle className="size-3.5 shrink-0 text-warning" />
                        </TooltipTrigger>
                        <TooltipContent>No longer listed in AppUsers for this data source</TooltipContent>
                      </Tooltip>
                    )}

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
                )
              })}
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  )
}

export function AccessLevelBadge({ level }: { level: QueryAccessLevel }) {
  if (level === 'owner') return null
  return (
    <Badge variant="secondary" className="h-4 px-1 text-[9px] font-medium capitalize">
      {level}
    </Badge>
  )
}
