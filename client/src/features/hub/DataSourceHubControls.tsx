import { Loader2, Lock, MoveRight } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { moveCountsSummary } from '@/features/hub/app-form'
import { useHubApps, useMoveDataSource, useSetDataSourceLock } from '@/hooks/useHub'
import { ApiError } from '@/lib/api-client'

const DEFAULT_APP_ID = '00000000-0000-0000-0000-00000000000d'

/** Super admins only, hub mode only: who may change this data source, and which app owns it. */
export function DataSourceHubControls({ dataSourceId, name, locked, appId }: { dataSourceId: string; name: string; locked: boolean; appId: string | null }) {
  const setLock = useSetDataSourceLock(dataSourceId)
  const [moving, setMoving] = useState(false)

  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border p-4" aria-labelledby="hub-controls-heading">
      <h2 id="hub-controls-heading" className="text-sm font-semibold">
        Super admin controls
      </h2>
      <div className="flex items-center gap-3">
        <Switch
          id="ds-locked"
          checked={locked}
          disabled={setLock.isPending}
          onCheckedChange={(next) => setLock.mutate(next)}
          aria-label="Locked"
        />
        <div className="min-w-0 flex-1">
          <label htmlFor="ds-locked" className="flex items-center gap-1.5 text-sm font-medium">
            <Lock className="size-3.5" aria-hidden />
            Locked
          </label>
          <p className="text-xs text-muted-foreground">
            A locked data source is read-only for app admins: only super admins change its connection, on or off, catalog, scope mappings,
            record limit and scope keys. Each change is recorded in the audit log.
          </p>
        </div>
        {setLock.isPending && <Loader2 className="size-4 animate-spin" aria-hidden />}
      </div>
      <div className="flex items-center gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium">Move to another app</p>
          <p className="text-xs text-muted-foreground">
            Only possible while no saved query, chart, dashboard tile, parameter dropdown or scope rule uses it. Its audit history stays with this app.
          </p>
        </div>
        <Button variant="outline" size="sm" disabled={!appId} onClick={() => setMoving(true)}>
          <MoveRight className="size-3.5" />
          Move…
        </Button>
      </div>

      <Dialog open={moving} onOpenChange={setMoving}>
        <DialogContent className="sm:max-w-md">
          {moving && appId && <MoveBody appId={appId} dataSourceId={dataSourceId} name={name} onClose={() => setMoving(false)} />}
        </DialogContent>
      </Dialog>
    </section>
  )
}

function MoveBody({ appId, dataSourceId, name, onClose }: { appId: string; dataSourceId: string; name: string; onClose: () => void }) {
  const navigate = useNavigate()
  const apps = useHubApps(true)
  const move = useMoveDataSource(appId, dataSourceId)
  const [target, setTarget] = useState('')
  const targets = (apps.data ?? []).filter((a) => a.isEnabled && a.id !== appId && a.id !== DEFAULT_APP_ID)

  const error = move.error
  const uses = error instanceof ApiError && error.status === 409 ? moveCountsSummary(error.counts) : []
  const message = error instanceof ApiError ? error.message : error ? 'Could not move the data source.' : null

  return (
    <>
      <DialogHeader>
        <DialogTitle>Move {name}</DialogTitle>
        <DialogDescription>
          It leaves this app and appears in the app you choose. Nothing in the target app can already use it, and the target can&apos;t
          have a data source with the same name.
        </DialogDescription>
      </DialogHeader>
      {apps.isLoading ? (
        <p className="text-sm text-muted-foreground">Loading apps…</p>
      ) : targets.length === 0 ? (
        <p className="text-sm text-muted-foreground">There is no other app turned on to move it to.</p>
      ) : (
        <Select value={target} onValueChange={(v) => v && setTarget(v)}>
          <SelectTrigger className="w-full" aria-label="Target app">
            <SelectValue placeholder="Choose an app" />
          </SelectTrigger>
          <SelectContent>
            {targets.map((a) => (
              <SelectItem key={a.id} value={a.id}>
                {a.name}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      )}
      {message && (
        <div role="alert" className="rounded-md border border-destructive/50 px-3 py-2 text-sm">
          <p>{message}</p>
          {uses.length > 0 && (
            <ul className="mt-1 list-disc pl-5 text-xs">
              {uses.map((line) => (
                <li key={line}>{line}</li>
              ))}
            </ul>
          )}
        </div>
      )}
      <DialogFooter>
        <Button variant="outline" onClick={onClose} disabled={move.isPending}>
          Cancel
        </Button>
        <Button
          disabled={!target || move.isPending}
          onClick={() =>
            move.mutate(target, {
              onSuccess: () => {
                onClose()
                navigate('/admin')
              },
            })
          }
        >
          {move.isPending && <Loader2 className="size-3.5 animate-spin" />}
          Move
        </Button>
      </DialogFooter>
    </>
  )
}
