import { AlertTriangle, Loader2, Plug, Power, Save } from 'lucide-react'
import { useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { ConnectionFields, TestResultLine } from '@/features/admin/ConnectionFields'
import {
  emptyForm,
  formFromDetails,
  isDescriptionValid,
  blankPasswordProblem,
  connectionTargetChanged,
  isNameValid,
  sslLabel,
  toConnectionInput,
  validateConnection,
  type ConnectionForm,
} from '@/features/admin/connection-form'
import { providerLabel } from '@/features/catalog/unavailable'
import { useDataSourceManagement, useSetDataSourceActive, useTestConnection, useUpdateDataSource } from '@/hooks/useAdmin'
import { ApiError } from '@/lib/api-client'
import type { AdminDataSourceDetailDto, ConnectionTestResult } from '@/types'

export function ConnectionSection({ dataSource }: { dataSource: AdminDataSourceDetailDto }) {
  const management = useDataSourceManagement()
  const enabled = management.data?.enabled === true

  return (
    <section className="flex flex-col gap-3" aria-labelledby="connection-heading">
      <h2 id="connection-heading" className="text-sm font-semibold">
        Connection
      </h2>

      {dataSource.catalogError && (
        <p role="alert" className="flex items-start gap-1.5 text-sm text-destructive">
          <AlertTriangle className="mt-0.5 size-4 shrink-0" aria-hidden="true" />
          <span>Couldn't read this data source's tables and views: {dataSource.catalogError}</span>
        </p>
      )}

      {!dataSource.isActive && (
        <p className="text-sm text-muted-foreground">
          This data source is turned off. Saved queries and charts on it can't run until it is turned back on.
        </p>
      )}

      {enabled ? (
        <EditableConnection dataSource={dataSource} />
      ) : (
        <ReadOnlyConnection dataSource={dataSource} loading={management.isLoading} />
      )}
    </section>
  )
}

function ReadOnlyConnection({ dataSource, loading }: { dataSource: AdminDataSourceDetailDto; loading: boolean }) {
  const c = dataSource.connection
  return (
    <div className="flex flex-col gap-2 text-sm">
      <dl className="grid grid-cols-[8rem_1fr] gap-x-3 gap-y-1">
        <dt className="text-muted-foreground">Name</dt>
        <dd>{dataSource.name}</dd>
        <dt className="text-muted-foreground">Kind</dt>
        <dd>{providerLabel(dataSource.provider)}</dd>
        {c ? (
          <>
            <dt className="text-muted-foreground">Host</dt>
            <dd className="[font-variant-numeric:tabular-nums]">
              {c.host}:{c.port}
            </dd>
            <dt className="text-muted-foreground">Database</dt>
            <dd>{c.database}</dd>
            <dt className="text-muted-foreground">Username</dt>
            <dd>{c.username}</dd>
            <dt className="text-muted-foreground">Encryption</dt>
            <dd>{sslLabel(c.sslMode)}</dd>
          </>
        ) : dataSource.connectionStringName ? (
          <>
            <dt className="text-muted-foreground">Connection</dt>
            <dd>From host configuration ("{dataSource.connectionStringName}")</dd>
          </>
        ) : dataSource.usesMetadataDatabase ? (
          <>
            <dt className="text-muted-foreground">Connection</dt>
            <dd>QueryBuilder's own database</dd>
          </>
        ) : null}
      </dl>
      {dataSource.connectionUnreadable && (
        <p className="text-muted-foreground">The saved connection details can't be read with the current encryption key.</p>
      )}
      {!loading && (
        <p className="text-xs text-muted-foreground">
          The host app can turn on adding and editing data sources here with <code>DataSourceManagement</code>.
        </p>
      )}
    </div>
  )
}

function EditableConnection({ dataSource }: { dataSource: AdminDataSourceDetailDto }) {
  const update = useUpdateDataSource(dataSource.id)
  const test = useTestConnection()
  const setActive = useSetDataSourceActive(dataSource.id)

  const [name, setName] = useState(dataSource.name)
  const [description, setDescription] = useState(dataSource.description ?? '')
  const [form, setForm] = useState<ConnectionForm>(
    dataSource.connection ? formFromDetails(dataSource.connection) : emptyForm(dataSource.provider),
  )
  const [result, setResult] = useState<ConnectionTestResult | null>(null)
  const [showErrors, setShowErrors] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)

  const saved = dataSource.connection
  const provider = dataSource.provider
  const managed = dataSource.isManaged

  const savedForm = saved ? formFromDetails(saved) : null
  const connectionDirty =
    managed &&
    (savedForm === null ||
      form.password !== '' ||
      (Object.keys(savedForm) as (keyof ConnectionForm)[]).some((key) => form[key] !== savedForm[key]))
  const passwordProblem = managed ? blankPasswordProblem(saved, dataSource.connectionUnreadable, form, provider) : null
  const targetMoved = managed && connectionTargetChanged(saved, form)
  const connectionErrors = managed && connectionDirty && showErrors ? validateConnection(form, provider) : {}
  const connectionValid = !managed || !connectionDirty || Object.keys(validateConnection(form, provider)).length === 0
  const metaDirty = name.trim() !== dataSource.name || description.trim() !== (dataSource.description ?? '')
  const nameOk = isNameValid(name)
  const descriptionOk = isDescriptionValid(description)

  function changeForm(next: ConnectionForm) {
    setForm(next)
    setResult(null)
    setError(null)
  }

  async function runTest() {
    setShowErrors(true)
    if (passwordProblem || Object.keys(validateConnection(form, provider)).length > 0) return
    setResult(null)
    try {
      setResult(
        await test.mutateAsync({
          provider,
          connection: toConnectionInput(form, provider),
          // Lets a blank password mean "the saved one" for the same host, port and username.
          existingId: dataSource.id,
        }),
      )
    } catch (e) {
      setResult({ ok: false, message: e instanceof ApiError ? e.message : 'The test could not be run.' })
    } finally {
      test.reset()
    }
  }

  async function save() {
    setShowErrors(true)
    setError(null)
    if (!nameOk || !descriptionOk || !connectionValid || passwordProblem) return
    try {
      await update.mutateAsync({
        name: name.trim(),
        description: description.trim(),
        ...(managed && connectionDirty ? { connection: toConnectionInput(form, provider) } : {}),
      })
      // The password has done its job; nothing keeps it in the page after a save.
      setForm((current) => ({ ...current, password: '' }))
      setResult(null)
      setShowErrors(false)
    } catch (e) {
      setError(e instanceof ApiError ? e.message : 'Could not save the data source.')
    } finally {
      update.reset()
    }
  }

  return (
    <div className="flex max-w-xl flex-col gap-3">
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="conn-name">Name</Label>
        <Input id="conn-name" value={name} onChange={(e) => setName(e.target.value)} aria-invalid={!nameOk} />
        {!nameOk && <p className="text-xs text-destructive">Enter a name of up to 200 characters.</p>}
      </div>
      <div className="flex flex-col gap-1.5">
        <Label htmlFor="conn-description">Description (optional)</Label>
        <Textarea id="conn-description" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} aria-invalid={!descriptionOk} />
        {!descriptionOk && <p className="text-xs text-destructive">The description can be at most 1,000 characters.</p>}
      </div>

      {managed ? (
        <>
          {dataSource.connectionUnreadable && (
            <p role="alert" className="flex items-start gap-1.5 text-sm">
              <AlertTriangle className="mt-0.5 size-4 shrink-0 text-warning" aria-hidden="true" />
              <span>
                The saved connection details can't be read with the current encryption key. Enter them again below and save to
                replace them.
              </span>
            </p>
          )}
          <ConnectionFields
            form={form}
            errors={connectionErrors}
            onChange={changeForm}
            passwordPlaceholder={saved?.hasPassword ? 'Leave blank to keep the current password' : undefined}
          />
          {passwordProblem && (
            <p role="alert" className="text-xs text-destructive">
              {passwordProblem}
            </p>
          )}
          {targetMoved && (
            <p className="flex items-start gap-1.5 text-xs text-muted-foreground">
              <AlertTriangle className="mt-0.5 size-3.5 shrink-0 text-warning" aria-hidden="true" />
              <span>
                Data scope rules and "not scoped" decisions are kept by object name. After this change they apply to
                whatever has the same names on the new host or database, so check them below.
              </span>
            </p>
          )}
        </>
      ) : (
        <p className="text-sm text-muted-foreground">
          {dataSource.usesMetadataDatabase ? (
            <>This data source uses QueryBuilder's own database</>
          ) : (
            <>
              The connection for this data source comes from host configuration
              {dataSource.connectionStringName ? <> (connection string "{dataSource.connectionStringName}")</> : null}
            </>
          )}
          , so it can't be changed here. You can still rename it and change its description.
        </p>
      )}

      {result && <TestResultLine result={result} />}
      {error && (
        <p role="alert" className="text-sm text-destructive">
          {error}
        </p>
      )}

      <div className="flex flex-wrap items-center gap-2">
        <Button onClick={() => void save()} disabled={update.isPending || (!metaDirty && !connectionDirty)}>
          {update.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save
        </Button>
        {managed && (
          <Button variant="outline" onClick={() => void runTest()} disabled={test.isPending}>
            {test.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Plug className="size-3.5" />}
            Test connection
          </Button>
        )}
        <Button variant={dataSource.isActive ? 'destructive' : 'outline'} className="ml-auto" onClick={() => setConfirmOpen(true)}>
          <Power className="size-3.5" />
          {dataSource.isActive ? 'Turn off' : 'Turn on'}
        </Button>
      </div>

      <Dialog open={confirmOpen} onOpenChange={setConfirmOpen}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>
              {dataSource.isActive ? `Turn off "${dataSource.name}"?` : `Turn on "${dataSource.name}"?`}
            </DialogTitle>
            <DialogDescription>
              {dataSource.isActive
                ? 'Saved queries and charts on this data source stop working, and it leaves the data source list, until it is turned back on. Nothing is deleted.'
                : 'It returns to the data source list, and the saved queries and charts on it work again.'}
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setConfirmOpen(false)}>
              Cancel
            </Button>
            <Button
              variant={dataSource.isActive ? 'destructive' : 'default'}
              disabled={setActive.isPending}
              onClick={() => setActive.mutate(!dataSource.isActive, { onSettled: () => setConfirmOpen(false) })}
            >
              {dataSource.isActive ? 'Turn off' : 'Turn on'}
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
