import { AlertTriangle, Loader2, Lock, Plus, Save, Search, X } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { adminUnavailableReason, objectRowKey, qualifiedName } from '@/features/catalog/unavailable'
import { countObjectsUsingKey, removeKeyWarning, validateNewScopeKey } from '@/features/admin/scope-keys'
import { useAdminDataScope, useUpdateDataScope, useUpdateScopeKeys } from '@/hooks/useAdmin'
import type { AdminDataScopeObjectDto, DataScopeState } from '@/types'

// Radix Select forbids an empty-string item value, so "not mapped" needs a sentinel.
const NOT_MAPPED = '__none'

interface ObjectEdit {
  state: DataScopeState
  mappings: Record<string, string>
}

const STATE_LABELS: Record<DataScopeState, string> = {
  undecided: 'Undecided (hidden)',
  notScoped: 'Not scoped (everyone)',
  scoped: 'Scoped',
}

export function DataScopeSection({ dataSourceId }: { dataSourceId: string }) {
  const { data, isLoading, isError } = useAdminDataScope(dataSourceId)
  const updateScope = useUpdateDataScope(dataSourceId)
  const scopeKeys = useUpdateScopeKeys(dataSourceId)

  // Mapping edits live here, apart from the query data, so a refetch after a key change never wipes them.
  const [edits, setEdits] = useState<Record<string, ObjectEdit>>({})
  const [search, setSearch] = useState('')
  const [undecidedOnly, setUndecidedOnly] = useState(false)
  const [newKey, setNewKey] = useState('')
  const [removing, setRemoving] = useState<string | null>(null)

  const declaredKeys = data?.declaredKeys ?? []

  function effective(o: AdminDataScopeObjectDto): ObjectEdit {
    return edits[o.objectName] ?? { state: o.state, mappings: o.mappings }
  }

  const objects = data?.objects ?? []
  const unavailable = (data?.unavailable ?? []).filter(
    (o) => !search.trim() || qualifiedName(o.schemaName, o.name).toLowerCase().includes(search.trim().toLowerCase()),
  )
  const visibleObjects = useMemo(() => {
    const term = search.trim().toLowerCase()
    return objects.filter((o) => {
      if (term && !o.objectName.toLowerCase().includes(term)) return false
      if (undecidedOnly && (edits[o.objectName]?.state ?? o.state) !== 'undecided') return false
      return true
    })
  }, [objects, search, undecidedOnly, edits])

  if (isError) {
    return (
      <p role="alert" className="text-sm text-muted-foreground">
        Couldn't load the data scope for this data source. If it can't be reached, fix the connection above and reload.
      </p>
    )
  }

  if (isLoading || !data) {
    return (
      <div className="flex items-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading data scope…
      </div>
    )
  }

  if (!data.enabled) {
    return (
      <div className="flex flex-col gap-1">
        <h2 className="text-sm font-semibold">Row-level data scope</h2>
        <p className="text-xs text-muted-foreground">
          Data scoping is turned off in the app's code (no scope resolver is set), so every user sees all rows and keys
          can't be added here. Ask the app's developers to turn it on.
        </p>
      </div>
    )
  }

  const effectiveAll = data.objects.map((o) => ({ object: o, edit: effective(o) }))
  const undecidedCount = effectiveAll.filter((x) => x.edit.state === 'undecided').length
  const incomplete = effectiveAll.filter(
    (x) => x.edit.state === 'scoped' && !declaredKeys.some((k) => x.edit.mappings[k]),
  )
  const isDirty = Object.keys(edits).length > 0
  const addProblem = newKey === '' ? null : validateNewScopeKey(newKey, data.hostKeys, data.dataSourceKeys)

  function saveKeys(keys: string[], onSuccess?: () => void) {
    scopeKeys.mutate({ keys }, { onSuccess })
  }

  function handleAddKey() {
    if (newKey === '' || addProblem) return
    saveKeys([...data!.dataSourceKeys, newKey], () => setNewKey(''))
  }

  function requestRemove(key: string) {
    scopeKeys.reset()
    if (countObjectsUsingKey(data!.objects, key) > 0) setRemoving(key)
    else removeKey(key)
  }

  function removeKey(key: string) {
    saveKeys(
      data!.dataSourceKeys.filter((k) => k !== key),
      () => setRemoving(null),
    )
  }

  function update(o: AdminDataScopeObjectDto, next: Partial<ObjectEdit>) {
    const current = effective(o)
    setEdits({ ...edits, [o.objectName]: { ...current, ...next } })
  }

  function setMapping(o: AdminDataScopeObjectDto, key: string, column: string) {
    const mappings = { ...effective(o).mappings }
    if (column === NOT_MAPPED) delete mappings[key]
    else mappings[key] = column
    update(o, { mappings })
  }

  function handleSave() {
    updateScope.mutate(
      {
        objects: effectiveAll.map(({ object, edit }) => ({
          objectName: object.objectName,
          state: edit.state,
          // Only declared keys are sent — stale keys (no longer declared by the host) are dropped on save.
          mappings:
            edit.state === 'scoped'
              ? Object.fromEntries(declaredKeys.filter((k) => edit.mappings[k]).map((k) => [k, edit.mappings[k]]))
              : null,
        })),
      },
      { onSuccess: () => setEdits({}) },
    )
  }

  return (
    <div className="flex flex-col gap-3">
      <div>
        <h2 className="text-sm font-semibold">Row-level data scope</h2>
        <p className="text-xs text-muted-foreground">
          Decide, per view, which column each scope key
          {declaredKeys.length > 0 ? ` (${declaredKeys.join(', ')})` : ''} filters on. Scoped users only see rows
          matching their own values. Undecided views are hidden from scoped users; unrestricted users always see
          everything.
        </p>
      </div>

      <div className="flex flex-col gap-2 rounded-lg border border-border p-3">
        <h3 className="text-xs font-semibold">Scope keys</h3>
        <div className="flex flex-wrap items-center gap-1.5">
          {data.hostKeys.map((key) => (
            <Badge key={key} variant="secondary" className="gap-1 font-normal">
              <Lock className="size-3" aria-hidden="true" />
              {key}
              <span className="text-[10px] text-muted-foreground">from app settings</span>
            </Badge>
          ))}
          {data.dataSourceKeys.map((key) => (
            <Badge key={key} variant="outline" className="gap-1 pr-0.5 font-normal">
              {key}
              <button
                type="button"
                aria-label={`Remove key ${key}`}
                disabled={scopeKeys.isPending}
                onClick={() => requestRemove(key)}
                className="rounded-sm p-0.5 text-muted-foreground hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50"
              >
                <X className="size-3" />
              </button>
            </Badge>
          ))}
          {declaredKeys.length === 0 && <span className="text-xs text-muted-foreground">No keys yet.</span>}
        </div>
        <form
          className="flex flex-wrap items-center gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            handleAddKey()
          }}
        >
          <Input
            value={newKey}
            onChange={(e) => {
              setNewKey(e.target.value)
              scopeKeys.reset()
            }}
            placeholder="e.g. RegionId"
            className="h-8 w-56 text-xs"
            aria-label="New scope key"
            aria-invalid={addProblem !== null}
          />
          <Button type="submit" size="sm" disabled={scopeKeys.isPending || newKey === '' || addProblem !== null}>
            {scopeKeys.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Plus className="size-3.5" />}
            Add key
          </Button>
        </form>
        {addProblem && <p className="text-xs text-destructive">{addProblem}</p>}
        {scopeKeys.isError && removing === null && (
          <p role="alert" className="text-xs text-destructive">
            {scopeKeys.error instanceof Error ? scopeKeys.error.message : 'Could not update the keys.'}
          </p>
        )}
        <p className="text-xs text-muted-foreground">
          Values for every key come from the app's resolver. A view mapped to a key the resolver doesn't supply is hidden
          from scoped users.
        </p>
      </div>

      <div className="flex flex-wrap items-center gap-3">
        <div className="relative w-64">
          <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
          <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search views…" className="h-8 pl-8 text-xs" />
        </div>
        <label className="flex items-center gap-2 text-xs">
          <Switch checked={undecidedOnly} onCheckedChange={setUndecidedOnly} />
          Undecided only
        </label>
        <Badge variant={undecidedCount > 0 ? 'destructive' : 'secondary'} className="text-[10px] font-normal">
          {undecidedCount} undecided
        </Badge>
      </div>

      <div className="max-h-[32rem] overflow-auto rounded-lg border border-border">
        {visibleObjects.map((o) => {
          const edit = effective(o)
          return (
            <div key={o.objectName} className="flex flex-col gap-2 border-b border-border px-3 py-2 last:border-b-0">
              <div className="flex items-center gap-2">
                <span className="min-w-0 flex-1 truncate text-sm">{o.objectName}</span>
                <Badge variant="secondary" className="shrink-0 text-[10px] font-normal">
                  {o.kind}
                </Badge>
                <Select value={edit.state} onValueChange={(value) => update(o, { state: value as DataScopeState })}>
                  <SelectTrigger size="sm" className="h-7 w-48 shrink-0 text-xs" aria-label={`Scope for ${o.objectName}`}>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(Object.keys(STATE_LABELS) as DataScopeState[]).map((state) => (
                      <SelectItem key={state} value={state} className="text-xs">
                        {STATE_LABELS[state]}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              {edit.state === 'scoped' && (
                <div className="flex flex-wrap gap-3 pl-2">
                  {declaredKeys.map((key) => (
                    <label key={key} className="flex items-center gap-2 text-xs text-muted-foreground">
                      <span className="shrink-0">{key} →</span>
                      <Select value={edit.mappings[key] ?? NOT_MAPPED} onValueChange={(value) => setMapping(o, key, value)}>
                        <SelectTrigger size="sm" className="h-7 w-44 text-xs" aria-label={`${key} column for ${o.objectName}`}>
                          <SelectValue />
                        </SelectTrigger>
                        <SelectContent>
                          <SelectItem value={NOT_MAPPED} className="text-xs">
                            — not filtered by {key}
                          </SelectItem>
                          {o.columns.map((column) => (
                            <SelectItem key={column} value={column} className="text-xs">
                              {column}
                            </SelectItem>
                          ))}
                        </SelectContent>
                      </Select>
                    </label>
                  ))}
                </div>
              )}

              {o.staleKeys.length > 0 && (
                <p className="flex items-center gap-1.5 pl-2 text-xs text-warning">
                  <AlertTriangle className="size-3.5 shrink-0" />
                  Mapped to key(s) that are no longer scope keys here ({o.staleKeys.join(', ')}) — hidden from scoped
                  users until the key is added back or the view is saved again.
                </p>
              )}
            </div>
          )
        })}
        {unavailable.map((o) => (
          <div key={objectRowKey(o.schemaName, o.name)} className="flex flex-col gap-1 border-b border-border px-3 py-2 text-muted-foreground last:border-b-0">
            <div className="flex items-center gap-2">
              <AlertTriangle className="size-3.5 shrink-0 text-warning" aria-hidden="true" />
              <span className="min-w-0 flex-1 truncate text-sm">{qualifiedName(o.schemaName, o.name)}</span>
              <Badge variant="secondary" className="shrink-0 text-[10px] font-normal">
                {o.kind}
              </Badge>
            </div>
            <p className="pl-5 text-xs">{adminUnavailableReason(o.clashesWith)}</p>
          </div>
        ))}
        {visibleObjects.length === 0 && unavailable.length === 0 && (
          <div className="px-3 py-6 text-center text-sm text-muted-foreground">No views match.</div>
        )}
      </div>

      <div className="flex items-center gap-3">
        <Button onClick={handleSave} disabled={updateScope.isPending || incomplete.length > 0 || !isDirty}>
          {updateScope.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save data scope
        </Button>
        {incomplete.length > 0 && (
          <p className="text-xs text-destructive">
            Map at least one key to a column for: {incomplete.map((x) => x.object.objectName).join(', ')}.
          </p>
        )}
      </div>

      <Dialog open={removing !== null} onOpenChange={(open) => !scopeKeys.isPending && !open && setRemoving(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Remove key {removing}?</DialogTitle>
            <DialogDescription>
              {removing !== null && removeKeyWarning(countObjectsUsingKey(data.objects, removing), removing)}
            </DialogDescription>
          </DialogHeader>
          {scopeKeys.isError && removing !== null && (
            <p role="alert" className="text-xs text-destructive">
              {scopeKeys.error instanceof Error ? scopeKeys.error.message : 'Could not update the keys.'}
            </p>
          )}
          <DialogFooter>
            <Button variant="outline" disabled={scopeKeys.isPending} onClick={() => setRemoving(null)}>
              Cancel
            </Button>
            <Button variant="destructive" disabled={scopeKeys.isPending} onClick={() => removing !== null && removeKey(removing)}>
              {scopeKeys.isPending && <Loader2 className="size-3.5 animate-spin" />}
              Remove key
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </div>
  )
}
