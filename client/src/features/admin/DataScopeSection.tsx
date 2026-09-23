import { AlertTriangle, Loader2, Save, Search } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import { Switch } from '@/components/ui/switch'
import { useAdminDataScope, useUpdateDataScope } from '@/hooks/useAdmin'
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
  const { data, isLoading } = useAdminDataScope(dataSourceId)
  const updateScope = useUpdateDataScope(dataSourceId)

  const [edits, setEdits] = useState<Record<string, ObjectEdit>>({})
  const [search, setSearch] = useState('')
  const [undecidedOnly, setUndecidedOnly] = useState(false)

  const declaredKeys = data?.declaredKeys ?? []

  function effective(o: AdminDataScopeObjectDto): ObjectEdit {
    return edits[o.objectName] ?? { state: o.state, mappings: o.mappings }
  }

  const objects = data?.objects ?? []
  const visibleObjects = useMemo(() => {
    const term = search.trim().toLowerCase()
    return objects.filter((o) => {
      if (term && !o.objectName.toLowerCase().includes(term)) return false
      if (undecidedOnly && (edits[o.objectName]?.state ?? o.state) !== 'undecided') return false
      return true
    })
  }, [objects, search, undecidedOnly, edits])

  if (isLoading || !data) {
    return (
      <div className="flex items-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading data scope…
      </div>
    )
  }

  if (!data.enabled) return null

  const effectiveAll = data.objects.map((o) => ({ object: o, edit: effective(o) }))
  const undecidedCount = effectiveAll.filter((x) => x.edit.state === 'undecided').length
  const incomplete = effectiveAll.filter(
    (x) => x.edit.state === 'scoped' && !declaredKeys.some((k) => x.edit.mappings[k]),
  )
  const isDirty = Object.keys(edits).length > 0

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
          Decide, per view, which column each scope key ({declaredKeys.join(', ')}) filters on. Scoped users only see
          rows matching their own values. Undecided views are hidden from scoped users; unrestricted users always see
          everything.
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
                  Mapped to key(s) no longer declared by the host app ({o.staleKeys.join(', ')}) — hidden from scoped
                  users until saved again.
                </p>
              )}
            </div>
          )
        })}
        {visibleObjects.length === 0 && (
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
    </div>
  )
}
