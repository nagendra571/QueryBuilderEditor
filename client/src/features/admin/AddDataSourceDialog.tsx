import { Database, Loader2, Plug } from 'lucide-react'
import { useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import { toast } from 'sonner'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { ConnectionFields, TestResultLine } from '@/features/admin/ConnectionFields'
import {
  emptyForm,
  isDescriptionValid,
  isFormDirty,
  isNameValid,
  MANAGEABLE_PROVIDERS,
  portAfterProviderChange,
  toConnectionInput,
  validateConnection,
  type ConnectionForm,
} from '@/features/admin/connection-form'
import { providerLabel } from '@/features/catalog/unavailable'
import { useCreateDataSource, useTestConnection } from '@/hooks/useAdmin'
import { ApiError } from '@/lib/api-client'
import type { ConnectionTestResult, DataSourceProvider } from '@/types'

export function AddDataSourceDialog({ open, onOpenChange }: { open: boolean; onOpenChange: (open: boolean) => void }) {
  // Typed values, the password included, live only in the body's component state and are dropped when the dialog
  // closes; the mutations are set not to keep their request either (see useAdmin). While anything is typed, an
  // outside click or Escape is ignored, so a stray click can't throw the form away: Cancel is the way out.
  const dirty = useRef(false)
  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        className="sm:max-w-lg"
        onInteractOutside={(e) => {
          if (dirty.current) e.preventDefault()
        }}
        onEscapeKeyDown={(e) => {
          if (dirty.current) e.preventDefault()
        }}
      >
        {open && <AddDataSourceBody onClose={() => onOpenChange(false)} dirty={dirty} />}
      </DialogContent>
    </Dialog>
  )
}

function AddDataSourceBody({ onClose, dirty }: { onClose: () => void; dirty: { current: boolean } }) {
  const navigate = useNavigate()
  const create = useCreateDataSource()
  const test = useTestConnection()

  const [provider, setProvider] = useState<DataSourceProvider | null>(null)
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [form, setForm] = useState<ConnectionForm>(emptyForm('sqlServer'))
  // The provider the port default belongs to; stays set while the chooser is showing after Back.
  const [formProvider, setFormProvider] = useState<DataSourceProvider>('sqlServer')
  const [result, setResult] = useState<ConnectionTestResult | null>(null)
  const [showErrors, setShowErrors] = useState(false)
  const [serverError, setServerError] = useState<string | null>(null)

  // Ref writes during render are fine here: it only gates dismissal and is read from event handlers.
  // Also true on the chooser after Back: the typed fields are still held.
  dirty.current = name !== '' || description !== '' || isFormDirty(form, formProvider)

  // Back keeps everything typed. Choosing another provider changes only a port still at the old default.
  function choose(next: DataSourceProvider) {
    setProvider(next)
    if (next !== formProvider) {
      setForm({ ...form, port: portAfterProviderChange(form.port, formProvider, next) })
      setFormProvider(next)
      setResult(null)
      setServerError(null)
    }
  }

  // A result describes the values it was run with, so editing any of them retires it.
  function changeForm(next: ConnectionForm) {
    setForm(next)
    setResult(null)
    setServerError(null)
  }

  if (provider === null) {
    return (
      <>
        <DialogHeader>
          <DialogTitle>Add data source</DialogTitle>
          <DialogDescription>Which kind of database is it?</DialogDescription>
        </DialogHeader>
        <div className="grid gap-2">
          {MANAGEABLE_PROVIDERS.map((p) => (
            <button
              key={p}
              type="button"
              onClick={() => choose(p)}
              className="flex items-center gap-3 rounded-lg border border-input px-3 py-3 text-left text-sm font-medium hover:bg-muted focus-visible:border-ring focus-visible:ring-3 focus-visible:ring-ring/50 focus-visible:outline-none"
            >
              <Database className="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
              {providerLabel(p)}
            </button>
          ))}
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>
            Cancel
          </Button>
        </DialogFooter>
      </>
    )
  }

  const errors = showErrors ? validateConnection(form, provider) : {}
  const nameOk = isNameValid(name)
  const descriptionOk = isDescriptionValid(description)
  const formOk = Object.keys(validateConnection(form, provider)).length === 0

  async function runTest() {
    if (!provider) return
    setShowErrors(true)
    if (!formOk) return
    setResult(null)
    try {
      setResult(await test.mutateAsync({ provider, connection: toConnectionInput(form, provider) }))
    } catch (error) {
      setResult({ ok: false, message: error instanceof ApiError ? error.message : 'The test could not be run.' })
    } finally {
      test.reset()
    }
  }

  async function submit() {
    if (!provider) return
    setShowErrors(true)
    if (!nameOk || !descriptionOk || !formOk) return
    setServerError(null)
    try {
      const created = await create.mutateAsync({
        name: name.trim(),
        description: description.trim(),
        provider,
        connection: toConnectionInput(form, provider),
      })
      toast.success(`Added "${created.name}". Choose what it shows next.`)
      onClose()
      // New sources start views-only and scoped users see nothing until rules exist, so land where that is set.
      navigate(`/admin/data-sources/${created.id}`)
    } catch (error) {
      setServerError(error instanceof ApiError ? error.message : 'Could not add the data source.')
    } finally {
      create.reset()
    }
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Add {providerLabel(provider)} data source</DialogTitle>
        <DialogDescription>QueryBuilder connects to this database as the user below, so give it a read-only login.</DialogDescription>
      </DialogHeader>

      <form
        className="flex max-h-[60vh] flex-col gap-3 overflow-y-auto px-0.5"
        onSubmit={(e) => {
          e.preventDefault()
          void submit()
        }}
        autoComplete="off"
      >
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="ds-name">Name</Label>
          <Input id="ds-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="e.g. Sales warehouse" autoFocus aria-invalid={showErrors && !nameOk} />
          {showErrors && !nameOk && <p className="text-xs text-destructive">Enter a name of up to 200 characters.</p>}
        </div>
        <div className="flex flex-col gap-1.5">
          <Label htmlFor="ds-description">Description (optional)</Label>
          <Textarea id="ds-description" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} aria-invalid={!descriptionOk} />
          {!descriptionOk && <p className="text-xs text-destructive">The description can be at most 1,000 characters.</p>}
        </div>

        <ConnectionFields form={form} errors={errors} onChange={changeForm} />

        {/* Enter in a field must not submit without the Create button's own checks. */}
        <button type="submit" hidden aria-hidden="true" tabIndex={-1} />
      </form>

      {result && <TestResultLine result={result} />}
      {serverError && (
        <p role="alert" className="text-sm text-destructive">
          {serverError}
        </p>
      )}

      <DialogFooter className="sm:justify-between">
        <Button variant="ghost" onClick={() => setProvider(null)}>
          Back
        </Button>
        <div className="flex gap-2">
          <Button variant="outline" onClick={() => void runTest()} disabled={test.isPending}>
            {test.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Plug className="size-3.5" />}
            Test connection
          </Button>
          <Button onClick={() => void submit()} disabled={create.isPending}>
            {create.isPending && <Loader2 className="size-3.5 animate-spin" />}
            Create
          </Button>
        </div>
      </DialogFooter>
    </>
  )
}
