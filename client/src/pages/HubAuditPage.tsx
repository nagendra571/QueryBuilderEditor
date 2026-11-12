import { Loader2 } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { actionLabel, HUB_AUDIT_ACTIONS } from '@/features/hub/audit-filter'
import { ConsoleTabs } from '@/features/hub/ConsoleTabs'
import { useHubApps, useHubAudit } from '@/hooks/useHub'

const ALL = 'all'

export function HubAuditPage() {
  const apps = useHubApps(true)
  const [appId, setAppId] = useState(ALL)
  const [action, setAction] = useState(ALL)
  const audit = useHubAudit({ appId: appId === ALL ? undefined : appId, action: action === ALL ? undefined : action })
  const rows = audit.data?.pages.flat() ?? []

  return (
    <div className="mx-auto flex max-w-5xl flex-col gap-(--space-section) p-(--space-section)">
      <div>
        <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight">Console — Audit log</h1>
        <p className="text-sm text-muted-foreground">Changes and sign-ins across every app, newest first.</p>
      </div>

      <ConsoleTabs />

      <div className="flex flex-wrap items-center gap-3">
        <div className="flex items-center gap-2">
          <span id="audit-app-label" className="text-xs text-muted-foreground">
            App
          </span>
          <Select value={appId} onValueChange={(v) => v && setAppId(v)}>
            <SelectTrigger aria-labelledby="audit-app-label" className="h-8 w-48 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All apps</SelectItem>
              {(apps.data ?? []).map((a) => (
                <SelectItem key={a.id} value={a.id}>
                  {a.name}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="flex items-center gap-2">
          <span id="audit-action-label" className="text-xs text-muted-foreground">
            Action
          </span>
          <Select value={action} onValueChange={(v) => v && setAction(v)}>
            <SelectTrigger aria-labelledby="audit-action-label" className="h-8 w-56 text-xs">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value={ALL}>All actions</SelectItem>
              {HUB_AUDIT_ACTIONS.map((a) => (
                <SelectItem key={a} value={a}>
                  {actionLabel(a)}
                </SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
      </div>

      {audit.isError && <p className="text-sm text-destructive">Couldn&apos;t load the audit log.</p>}
      {audit.isLoading && (
        <p className="flex items-center text-sm text-muted-foreground">
          <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
        </p>
      )}

      {audit.data && (
        <div className="overflow-hidden rounded-lg border border-border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>When</TableHead>
                <TableHead>App</TableHead>
                <TableHead>Who</TableHead>
                <TableHead>Action</TableHead>
                <TableHead>What</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {rows.map((r) => (
                <TableRow key={r.id}>
                  <TableCell className="text-xs whitespace-nowrap text-muted-foreground">{new Date(r.timestampUtc).toLocaleString()}</TableCell>
                  <TableCell className="font-numeric text-xs">{r.appKey ?? 'hub'}</TableCell>
                  <TableCell className="max-w-40 truncate text-xs" title={r.actor}>
                    {r.actor}
                  </TableCell>
                  <TableCell className="text-xs whitespace-nowrap">{actionLabel(r.action)}</TableCell>
                  <TableCell className="text-xs">{r.summary}</TableCell>
                </TableRow>
              ))}
              {rows.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5} className="py-6 text-center text-sm text-muted-foreground">
                    Nothing matches.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      )}

      {audit.hasNextPage && (
        <div>
          <Button variant="outline" onClick={() => audit.fetchNextPage()} disabled={audit.isFetchingNextPage}>
            {audit.isFetchingNextPage && <Loader2 className="size-3.5 animate-spin" />}
            Load older
          </Button>
        </div>
      )}
    </div>
  )
}
