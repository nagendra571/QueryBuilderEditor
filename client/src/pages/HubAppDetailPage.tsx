import { ArrowLeft, KeyRound, Loader2, Save } from 'lucide-react'
import { useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from '@/components/ui/table'
import { Textarea } from '@/components/ui/textarea'
import {
  MAX_PEM,
  parseCeiling,
  validateAppName,
  validateKeyInput,
  validateReturnUrls,
  validateScopeKeys,
} from '@/features/hub/app-form'
import { DEFAULT_APP_ID } from '@/features/hub/permissions'
import { hubAppsKey, useAddSigningKey, useHubApps, useRevokeSigningKey, useSetReturnUrls, useUpdateApp } from '@/hooks/useHub'
import { ApiError } from '@/lib/api-client'
import type { HubAppDto, HubSigningKeyDto } from '@/lib/api'

const STALE = 'Someone else changed this app. Reload to see their changes.'

const when = (iso: string) => new Date(iso).toLocaleString()
const splitList = (text: string, separator: RegExp) => text.split(separator).map((s) => s.trim()).filter(Boolean)

export function HubAppDetailPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const apps = useHubApps(true)
  const app = apps.data?.find((a) => a.id === id)

  if (apps.isError) {
    return <div className="flex h-full items-center justify-center text-sm text-muted-foreground">Couldn&apos;t load this app.</div>
  }
  if (apps.isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
      </div>
    )
  }
  if (!app) {
    return <div className="flex h-full items-center justify-center text-sm text-muted-foreground">This app doesn&apos;t exist.</div>
  }

  const isDefault = app.id === DEFAULT_APP_ID
  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" className="size-8" onClick={() => navigate('/hub/apps')} aria-label="Back to apps">
          <ArrowLeft className="size-4" />
        </Button>
        <div>
          <h1 className="flex items-center gap-2 text-[1.75rem] leading-tight font-bold tracking-tight">
            {app.name}
            {!app.isEnabled && (
              <Badge variant="outline" className="text-xs font-normal">
                Off
              </Badge>
            )}
          </h1>
          <p className="font-numeric text-xs text-muted-foreground">{app.key}</p>
        </div>
      </div>

      {/* Remounts on a new version so drafts restart from what the server now holds. */}
      <SettingsSection key={`${app.id}:${app.version}`} app={app} isDefault={isDefault} />

      {isDefault ? (
        <p className="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">
          This is the app embedded mode used. It is never launched from another app, so it has no signing keys or return URLs.
        </p>
      ) : (
        <>
          <KeysSection app={app} />
          <ReturnUrlsSection key={`${app.id}:${(app.returnUrls ?? []).join('\n')}`} app={app} />
        </>
      )}
    </div>
  )
}

function SettingsSection({ app, isDefault }: { app: HubAppDto; isDefault: boolean }) {
  const queryClient = useQueryClient()
  const update = useUpdateApp(app.id)
  const [name, setName] = useState(app.name)
  const [enabled, setEnabled] = useState(app.isEnabled)
  const [scopeText, setScopeText] = useState((app.scopeKeys ?? []).join(', '))
  const [ceilingText, setCeilingText] = useState(app.maxRecords?.toString() ?? '')
  const [canManage, setCanManage] = useState(app.appAdminsCanManageConnections ?? false)

  const scopeKeys = splitList(scopeText, /[\s,]+/)
  const ceiling = parseCeiling(ceilingText)
  const problem = validateAppName(name) ?? validateScopeKeys(scopeKeys) ?? ceiling.problem
  const stale = update.error instanceof ApiError && update.error.status === 409
  const serverProblem = !stale && update.error ? (update.error instanceof ApiError ? update.error.message : 'Could not save.') : null

  function save() {
    if (problem) return
    update.mutate({
      version: app.version,
      name: name.trim(),
      isEnabled: enabled,
      scopeKeys,
      maxRecords: ceiling.value,
      appAdminsCanManageConnections: canManage,
    })
  }

  return (
    <section className="flex flex-col gap-4 rounded-lg border border-border p-4" aria-labelledby="app-settings-heading">
      <h2 id="app-settings-heading" className="text-sm font-semibold">
        Settings
      </h2>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="app-name">Name</Label>
        <Input id="app-name" value={name} onChange={(e) => setName(e.target.value)} className="max-w-md" />
      </div>

      <div className="flex items-center gap-3">
        <Switch id="app-enabled" checked={enabled} onCheckedChange={setEnabled} disabled={isDefault} />
        <div>
          <Label htmlFor="app-enabled">Turned on</Label>
          <p className="text-xs text-muted-foreground">
            {isDefault ? 'The Default app is turned off by the hub when it takes over a database, and this switch has no effect.' : 'A turned-off app refuses new launches and ends its users’ sessions within half a minute.'}
          </p>
        </div>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="app-scope-keys">Scope keys</Label>
        <Input id="app-scope-keys" value={scopeText} onChange={(e) => setScopeText(e.target.value)} className="max-w-md" placeholder="ProgramId, Region" />
        <p className="text-xs text-muted-foreground">The names this app may send in a launch token&apos;s scope. Separate with commas. At most 20.</p>
      </div>

      <div className="flex flex-col gap-1.5">
        <Label htmlFor="app-ceiling">Record ceiling</Label>
        <Input
          id="app-ceiling"
          type="number"
          inputMode="numeric"
          min={1}
          max={100000}
          value={ceilingText}
          onChange={(e) => setCeilingText(e.target.value)}
          placeholder="No ceiling"
          className="h-9 w-44"
        />
        <p className="text-xs text-muted-foreground">
          No query in this app returns more than this. This ceiling always caps it. A data source&apos;s own limit overrides the host default, but never goes above the ceiling.
        </p>
      </div>

      <div className="flex items-center gap-3">
        <Switch id="app-can-manage" checked={canManage} onCheckedChange={setCanManage} />
        <div>
          <Label htmlFor="app-can-manage">App admins can add data sources</Label>
          <p className="text-xs text-muted-foreground">
            Lets this app&apos;s admins add, edit, test and turn off connections. Off keeps connections with super admins.
          </p>
        </div>
      </div>

      {problem && <p className="text-xs text-destructive">{problem}</p>}
      {serverProblem && (
        <p role="alert" className="text-xs text-destructive">
          {serverProblem}
        </p>
      )}
      {stale && (
        <div role="alert" className="flex items-center gap-3 rounded-md border border-destructive/50 px-3 py-2 text-sm">
          <span className="flex-1">{STALE}</span>
          <Button size="sm" variant="outline" onClick={() => queryClient.invalidateQueries({ queryKey: hubAppsKey })}>
            Reload
          </Button>
        </div>
      )}
      <div>
        <Button onClick={save} disabled={update.isPending || !!problem}>
          {update.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save
        </Button>
      </div>
    </section>
  )
}

function KeysSection({ app }: { app: HubAppDto }) {
  const [adding, setAdding] = useState(false)
  const [revoking, setRevoking] = useState<HubSigningKeyDto | null>(null)
  const revoke = useRevokeSigningKey(app.id)
  const keys = app.keys ?? []

  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border p-4" aria-labelledby="app-keys-heading">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 id="app-keys-heading" className="flex items-center gap-2 text-sm font-semibold">
            <KeyRound className="size-4" /> Signing keys
          </h2>
          <p className="text-xs text-muted-foreground">
            Public keys only. The app signs launch tokens with the private key and QueryBuilder checks them with these. Compare the
            fingerprint with the one the app shows.
          </p>
        </div>
        <Button variant="outline" size="sm" onClick={() => setAdding(true)}>
          Add a key
        </Button>
      </div>

      <div className="overflow-x-auto rounded-md border border-border">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Key id</TableHead>
              <TableHead>Algorithm</TableHead>
              <TableHead>Fingerprint</TableHead>
              <TableHead>Added</TableHead>
              <TableHead>Revoked</TableHead>
              <TableHead />
            </TableRow>
          </TableHeader>
          <TableBody>
            {keys.map((k) => (
              <TableRow key={k.kid}>
                <TableCell className="font-numeric text-xs">{k.kid}</TableCell>
                <TableCell className="text-xs">{k.algorithm}</TableCell>
                <TableCell>
                  {k.fingerprint ? (
                    <code className="text-[11px] break-all select-all">{k.fingerprint}</code>
                  ) : (
                    <span className="text-xs text-muted-foreground">unreadable</span>
                  )}
                </TableCell>
                <TableCell className="text-xs text-muted-foreground">{when(k.createdAtUtc)}</TableCell>
                <TableCell className="text-xs text-muted-foreground">{k.revokedAtUtc ? when(k.revokedAtUtc) : '—'}</TableCell>
                <TableCell className="text-right">
                  {!k.revokedAtUtc && (
                    <Button variant="outline" size="sm" onClick={() => setRevoking(k)}>
                      Revoke
                    </Button>
                  )}
                </TableCell>
              </TableRow>
            ))}
            {keys.length === 0 && (
              <TableRow>
                <TableCell colSpan={6} className="py-5 text-center text-sm text-muted-foreground">
                  No keys yet, so this app can&apos;t launch.
                </TableCell>
              </TableRow>
            )}
          </TableBody>
        </Table>
      </div>

      <Dialog open={adding} onOpenChange={setAdding}>
        <DialogContent className="sm:max-w-lg">{adding && <AddKeyBody appId={app.id} onClose={() => setAdding(false)} />}</DialogContent>
      </Dialog>

      <Dialog open={!!revoking} onOpenChange={(open) => !revoke.isPending && !open && setRevoking(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Revoke key {revoking?.kid}?</DialogTitle>
            <DialogDescription>
              Launch tokens signed with it are refused from now on. Revoking a key also ends every session of this app, so everyone has to sign in again. A revoked key can&apos;t be
              turned back on; add it again as a new key id if you need it.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" disabled={revoke.isPending} onClick={() => setRevoking(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              disabled={revoke.isPending}
              onClick={() => revoking && revoke.mutate(revoking.kid, { onSuccess: () => setRevoking(null) })}
            >
              {revoke.isPending && <Loader2 className="size-3.5 animate-spin" />}
              Revoke
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

function AddKeyBody({ appId, onClose }: { appId: string; onClose: () => void }) {
  const add = useAddSigningKey(appId)
  const [kid, setKid] = useState('')
  const [algorithm, setAlgorithm] = useState('RS256')
  const [pem, setPem] = useState('')
  const [touched, setTouched] = useState(false)
  const problem = validateKeyInput(kid, algorithm, pem)
  const serverProblem = add.error instanceof ApiError ? add.error.message : add.error ? 'Could not add the key.' : null

  function submit(e: React.FormEvent) {
    e.preventDefault()
    setTouched(true)
    if (problem) return
    add.mutate({ kid, algorithm, publicKeyPem: pem.trim() }, { onSuccess: onClose })
  }

  return (
    <form onSubmit={submit} className="flex flex-col gap-4">
      <DialogHeader>
        <DialogTitle>Add a signing key</DialogTitle>
        <DialogDescription>Paste the app&apos;s public key. A private key or a certificate is refused.</DialogDescription>
      </DialogHeader>
      <div className="flex gap-3">
        <div className="flex flex-1 flex-col gap-1.5">
          <Label htmlFor="key-kid">Key id</Label>
          <Input id="key-kid" value={kid} onChange={(e) => setKid(e.target.value)} className="font-numeric" autoComplete="off" />
        </div>
        <div className="flex w-32 flex-col gap-1.5">
          <Label htmlFor="key-alg">Algorithm</Label>
          <Select value={algorithm} onValueChange={(v) => v && setAlgorithm(v)}>
            <SelectTrigger id="key-alg" className="w-full">
              <SelectValue />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="RS256">RS256</SelectItem>
              <SelectItem value="ES256">ES256</SelectItem>
            </SelectContent>
          </Select>
        </div>
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="key-pem">Public key (PEM)</Label>
        <Textarea
          id="key-pem"
          value={pem}
          onChange={(e) => setPem(e.target.value)}
          rows={8}
          maxLength={MAX_PEM}
          className="font-mono text-xs"
          placeholder={'-----BEGIN PUBLIC KEY-----\n…\n-----END PUBLIC KEY-----'}
          spellCheck={false}
        />
        <p className="text-xs text-muted-foreground">RS256 needs RSA of 2048 to 8192 bits; ES256 needs the P-256 curve.</p>
      </div>
      {touched && problem && <p className="text-xs text-destructive">{problem}</p>}
      {serverProblem && <p role="alert" className="text-xs text-destructive">{serverProblem}</p>}
      <DialogFooter>
        <Button type="button" variant="outline" onClick={onClose} disabled={add.isPending}>
          Cancel
        </Button>
        <Button type="submit" disabled={add.isPending}>
          {add.isPending && <Loader2 className="size-3.5 animate-spin" />}
          Add key
        </Button>
      </DialogFooter>
    </form>
  )
}

function ReturnUrlsSection({ app }: { app: HubAppDto }) {
  const save = useSetReturnUrls(app.id)
  const saved = app.returnUrls ?? []
  const [text, setText] = useState(saved.join('\n'))
  const urls = splitList(text, /\n/)
  const problem = validateReturnUrls(urls)
  const dirty = urls.join('\n') !== saved.join('\n')
  const serverProblem = save.error instanceof ApiError ? save.error.message : save.error ? 'Could not save the return URLs.' : null

  return (
    <section className="flex flex-col gap-2 rounded-lg border border-border p-4" aria-labelledby="app-urls-heading">
      <div>
        <h2 id="app-urls-heading" className="text-sm font-semibold">
          Return URLs
        </h2>
        <p className="text-xs text-muted-foreground">
          Where &quot;Back to the app&quot; may send people, one per line, at most 20. Each must be https, written exactly as the browser
          will follow it (lowercase host, a <code>/</code> after the host). Plain http is allowed only for localhost.
        </p>
      </div>
      <Textarea
        value={text}
        onChange={(e) => setText(e.target.value)}
        rows={5}
        className="font-mono text-xs"
        aria-label="Return URLs"
        placeholder="https://app.example.com/home"
        spellCheck={false}
      />
      {problem && <p className="text-xs text-destructive">{problem}</p>}
      {serverProblem && <p role="alert" className="text-xs text-destructive">{serverProblem}</p>}
      <div>
        <Button onClick={() => save.mutate(urls)} disabled={save.isPending || !!problem || !dirty}>
          {save.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save return URLs
        </Button>
      </div>
    </section>
  )
}
