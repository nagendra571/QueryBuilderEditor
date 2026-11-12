import { Loader2, Plus } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { EncryptionKeySection } from '@/features/admin/EncryptionKeySection'
import { validateAppKey, validateAppName } from '@/features/hub/app-form'
import { ConsoleTabs } from '@/features/hub/ConsoleTabs'
import { useCreateApp, useHubApps } from '@/hooks/useHub'
import { ApiError } from '@/lib/api-client'

export function HubAppsPage() {
  const apps = useHubApps(true)
  const navigate = useNavigate()
  const [creating, setCreating] = useState(false)

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h1 className="text-[1.75rem] leading-tight font-bold tracking-tight">Console — Apps</h1>
          <p className="text-sm text-muted-foreground">
            Each app hands its users to QueryBuilder with a signed launch token. Register the app, its public signing keys and the
            addresses it may send people back to.
          </p>
        </div>
        <Button onClick={() => setCreating(true)}>
          <Plus className="size-3.5" />
          New app
        </Button>
      </div>

      <ConsoleTabs />

      {apps.isError && <p className="text-sm text-destructive">Couldn&apos;t load the apps — you may not have access.</p>}
      {apps.isLoading && (
        <p className="flex items-center text-sm text-muted-foreground">
          <Loader2 className="mr-2 size-4 animate-spin" /> Loading apps…
        </p>
      )}

      {apps.data && (
        <div className="overflow-hidden rounded-lg border border-border">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Key</TableHead>
                <TableHead>Name</TableHead>
                <TableHead>Status</TableHead>
                <TableHead className="text-right">Signing keys</TableHead>
                <TableHead className="text-right">Return URLs</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              {apps.data.map((a) => (
                <TableRow key={a.id} className="cursor-pointer" onClick={() => navigate(`/hub/apps/${a.id}`)}>
                  <TableCell className="font-numeric text-xs">{a.key}</TableCell>
                  <TableCell className="font-medium">{a.name}</TableCell>
                  <TableCell>
                    <Badge variant={a.isEnabled ? 'secondary' : 'outline'} className="text-xs font-normal">
                      {a.isEnabled ? 'On' : 'Off'}
                    </Badge>
                  </TableCell>
                  <TableCell className="text-right font-numeric text-xs">{(a.keys ?? []).filter((k) => !k.revokedAtUtc).length}</TableCell>
                  <TableCell className="text-right font-numeric text-xs">{(a.returnUrls ?? []).length}</TableCell>
                </TableRow>
              ))}
              {apps.data.length === 0 && (
                <TableRow>
                  <TableCell colSpan={5} className="py-6 text-center text-sm text-muted-foreground">
                    No apps yet.
                  </TableCell>
                </TableRow>
              )}
            </TableBody>
          </Table>
        </div>
      )}

      <EncryptionKeySection hub />

      <Dialog open={creating} onOpenChange={setCreating}>
        <DialogContent className="sm:max-w-md">{creating && <CreateAppBody onClose={() => setCreating(false)} />}</DialogContent>
      </Dialog>
    </div>
  )
}

function CreateAppBody({ onClose }: { onClose: () => void }) {
  const navigate = useNavigate()
  const create = useCreateApp()
  const [key, setKey] = useState('')
  const [name, setName] = useState('')
  const [touched, setTouched] = useState(false)
  const keyProblem = key === '' ? null : validateAppKey(key)
  const nameProblem = name === '' ? null : validateAppName(name)
  const ready = validateAppKey(key) === null && validateAppName(name) === null
  const serverProblem = create.error instanceof ApiError ? create.error.message : create.error ? 'Could not create the app.' : null

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (!ready) return
    create.mutate(
      { key, name: name.trim() },
      {
        onSuccess: (app) => {
          onClose()
          navigate(`/hub/apps/${app.id}`)
        },
      },
    )
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>New app</DialogTitle>
        <DialogDescription>The key is permanent and goes in the app&apos;s launch tokens. The name is what people see.</DialogDescription>
      </DialogHeader>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="new-app-key">Key</Label>
        <Input id="new-app-key" value={key} onChange={(e) => setKey(e.target.value)} className="font-numeric" aria-invalid={!!keyProblem} autoComplete="off" />
        {(keyProblem || (touched && key === '')) && <p className="text-xs text-destructive">{keyProblem ?? validateAppKey(key)}</p>}
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="new-app-name">Name</Label>
        <Input id="new-app-name" value={name} onChange={(e) => setName(e.target.value)} aria-invalid={!!nameProblem} autoComplete="off" />
        {(nameProblem || (touched && name === '')) && <p className="text-xs text-destructive">{nameProblem ?? validateAppName(name)}</p>}
      </div>
      {serverProblem && <p role="alert" className="text-xs text-destructive">{serverProblem}</p>}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose} disabled={create.isPending}>
          Cancel
        </Button>
        <Button type="submit" disabled={create.isPending}>
          {create.isPending && <Loader2 className="size-3.5 animate-spin" />}
          Create app
        </Button>
      </DialogFooter>
    </form>
  )
}
