import { KeyRound, Loader2, RefreshCw } from 'lucide-react'
import { useState } from 'react'
import { Link } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { generateEncryptionKey, plural, reencryptSummary } from '@/features/admin/encryption-key'
import { useEncryptionKeyStatus, useReencrypt } from '@/hooks/useAdmin'
import type { ReencryptResultDto } from '@/types'

const SHARED_DATABASE_WARNING =
  'Every app that shares this database must already run with the new key before you re-encrypt. An app that still has only the old key can no longer read what was re-encrypted.'

/** `hub` switches to the hub-wide routes: the key is shared by every app, so its sources are not links into one app. */
export function EncryptionKeySection({ hub = false }: { hub?: boolean }) {
  const { data } = useEncryptionKeyStatus(hub)
  const [generating, setGenerating] = useState(false)
  const [confirming, setConfirming] = useState(false)
  const [result, setResult] = useState<ReencryptResultDto | null>(null)
  const reencrypt = useReencrypt(hub)

  // Nothing to say on a host with no key and no managed sources.
  if (!data || (!data.keyConfigured && data.managedCount === 0)) return null

  function runReencrypt() {
    reencrypt.mutate(undefined, {
      onSuccess: (r) => {
        setResult(r)
        setConfirming(false)
      },
    })
  }

  return (
    <section className="flex flex-col gap-3 rounded-lg border border-border p-4" aria-labelledby="encryption-key-heading">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 id="encryption-key-heading" className="flex items-center gap-2 text-sm font-semibold">
            <KeyRound className="size-4" /> Encryption key
          </h2>
          <p className="text-xs text-muted-foreground">
            Saved connection details are encrypted with a key set in the host app. Change it without retyping every source.
          </p>
        </div>
        <div className="flex shrink-0 gap-2">
          <Button variant="outline" size="sm" onClick={() => setGenerating(true)}>
            Generate a new key
          </Button>
          <Button size="sm" disabled={!data.canReencrypt || data.onPreviousKey === 0} onClick={() => setConfirming(true)}>
            <RefreshCw className="size-3.5" />
            Re-encrypt with the current key
          </Button>
        </div>
      </div>

      {data.keyConfigured ? (
        <dl className="flex flex-wrap gap-x-6 gap-y-1 text-xs">
          <div className="flex items-center gap-1.5">
            <dt className="text-muted-foreground">Current key</dt>
            <dd>
              <code>{data.fingerprint}</code>
            </dd>
          </div>
          <div className="flex items-center gap-1.5">
            <dt className="text-muted-foreground">Previous keys</dt>
            <dd>
              {data.previousFingerprints.length === 0 ? (
                'none'
              ) : (
                <span className="flex gap-1.5">
                  {data.previousFingerprints.map((f) => (
                    <code key={f}>{f}</code>
                  ))}
                </span>
              )}
            </dd>
          </div>
        </dl>
      ) : (
        <p className="text-xs text-destructive">No encryption key is configured, so saved connection details can&apos;t be read.</p>
      )}

      {data.keyConfigured && !data.canReencrypt && (
        <p className="text-xs text-muted-foreground">
          Re-encrypting is off here because admin pages are open to anyone. Restrict AdminAuthorization, or run it from an app
          that shares this database and has the new key.
        </p>
      )}

      <p className="text-xs">
        {plural(data.managedCount, 'managed data source', 'managed data sources')}: {data.onCurrentKey} on the current key,{' '}
        {data.onPreviousKey} on a previous key, {data.unreadable} unreadable.
      </p>

      {data.sources.length > 0 && (
        <ul className="flex flex-col divide-y divide-border rounded-md border border-border text-sm">
          {data.sources.map((s) => (
            <li key={s.id} className="flex items-center justify-between gap-3 px-3 py-1.5">
              {hub ? (
                <span className="min-w-0 flex-1 truncate font-medium">{s.name}</span>
              ) : (
                <Link to={`/admin/data-sources/${s.id}`} className="min-w-0 flex-1 truncate font-medium underline-offset-2 hover:underline">
                  {s.name}
                </Link>
              )}
              {!s.isActive && (
                <Badge variant="outline" className="text-xs font-normal">
                  Inactive
                </Badge>
              )}
              <span className="shrink-0 text-xs text-muted-foreground">
                {s.state === 'previous' ? 'On a previous key' : 'Unreadable: re-enter the password on its page'}
              </span>
            </li>
          ))}
        </ul>
      )}

      {result && (
        <div role="status" className="rounded-md border border-border bg-muted/40 px-3 py-2 text-xs">
          <ul className="flex flex-col gap-0.5">
            {reencryptSummary(result).map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        </div>
      )}

      <Dialog open={generating} onOpenChange={setGenerating}>
        <DialogContent className="sm:max-w-lg">{generating && <GenerateKeyBody onClose={() => setGenerating(false)} />}</DialogContent>
      </Dialog>

      <Dialog open={confirming} onOpenChange={(open) => !reencrypt.isPending && setConfirming(open)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Re-encrypt with the current key?</DialogTitle>
            <DialogDescription>
              {plural(data.onPreviousKey, 'data source', 'data sources')} will be saved again under the current key. Nothing connects to
              the databases, and running it twice is harmless.
            </DialogDescription>
          </DialogHeader>
          <p className="rounded-md border border-border bg-muted/40 px-3 py-2 text-sm">{SHARED_DATABASE_WARNING}</p>
          <DialogFooter>
            <Button variant="outline" disabled={reencrypt.isPending} onClick={() => setConfirming(false)}>
              Cancel
            </Button>
            <Button disabled={reencrypt.isPending} onClick={runReencrypt}>
              {reencrypt.isPending && <Loader2 className="size-3.5 animate-spin" />}
              Re-encrypt
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </section>
  )
}

function GenerateKeyBody({ onClose }: { onClose: () => void }) {
  // The key exists only in this component's state: it is made here, never sent to the server or stored, and is gone
  // when the dialog closes (the body isn't mounted while it's closed).
  const [key] = useState(generateEncryptionKey)
  return (
    <>
      <DialogHeader>
        <DialogTitle>Generate a new key</DialogTitle>
        <DialogDescription>Made in this browser. The server never sees it, and it is gone when you close this.</DialogDescription>
      </DialogHeader>
      <Input readOnly value={key} aria-label="New encryption key" className="font-mono text-xs" onFocus={(e) => e.currentTarget.select()} />
      <ol className="list-decimal space-y-1 pl-5 text-sm">
        <li>Store a copy in a password manager now. Losing it means re-entering every source.</li>
        <li>
          Set it as <code>EncryptionKey</code> and move the current key into <code>PreviousEncryptionKeys</code>.
        </li>
        <li>Restart the app.</li>
        <li>Come back here and choose Re-encrypt with the current key.</li>
      </ol>
      <p className="text-xs text-muted-foreground">{SHARED_DATABASE_WARNING}</p>
      <DialogFooter>
        <Button onClick={onClose}>Done</Button>
      </DialogFooter>
    </>
  )
}
