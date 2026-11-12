import { AlertTriangle, ArrowLeft, Loader2, Lock, Save, Search } from 'lucide-react'
import { useId, useMemo, useState } from 'react'
import { useNavigate, useParams } from 'react-router-dom'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { ConnectionSection } from '@/features/admin/ConnectionSection'
import { DataScopeSection } from '@/features/admin/DataScopeSection'
import { RecordLimitSection } from '@/features/admin/RecordLimitSection'
import { catalogControlsLocked } from '@/features/admin/connection-form'
import { initialAllowlist, pruneAllowlist } from '@/features/catalog/allowlist'
import { connectionReason, lockReason } from '@/features/hub/permissions'
import { adminUnavailableReason, objectRowKey, qualifiedName } from '@/features/catalog/unavailable'
import { useAdminDataSourceDetail, useUpdateCatalogPolicy } from '@/hooks/useAdmin'
import type { CatalogScope } from '@/types'

export function AdminDataSourcePolicyPage() {
  const { id } = useParams<{ id: string }>()
  const navigate = useNavigate()
  const { data, isLoading, isError } = useAdminDataSourceDetail(id)
  const idPrefix = useId()
  const updatePolicy = useUpdateCatalogPolicy(id!)

  const [scope, setScope] = useState<CatalogScope | null>(null)
  const [restrict, setRestrict] = useState<boolean | null>(null)
  const [selected, setSelected] = useState<Set<string> | null>(null)
  const [search, setSearch] = useState('')

  const effectiveScope = scope ?? data?.catalogScope ?? 'tablesAndViews'
  const effectiveRestrict = restrict ?? (data ? data.allowedObjects.length > 0 : false)
  const effectiveSelected = selected ?? new Set(data?.allowedObjects ?? [])

  const visibleObjects = useMemo(() => {
    if (!data) return []
    const term = search.trim().toLowerCase()
    return data.objects.filter((o) => {
      if (effectiveScope === 'views' && o.kind !== 'view') return false
      if (effectiveScope === 'tables' && o.kind !== 'table') return false
      if (term && !`${o.schemaName}.${o.name}`.toLowerCase().includes(term)) return false
      return true
    })
  }, [data, effectiveScope, search])

  const unavailableObjects = (data?.unavailable ?? []).filter((o) => {
    const term = search.trim().toLowerCase()
    return !term || qualifiedName(o.schemaName, o.name).toLowerCase().includes(term)
  })

  if (isError) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Couldn't load this data source — you may not have access, or it may not exist.
      </div>
    )
  }

  if (isLoading || !data) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
      </div>
    )
  }

  const nothingAllowed = effectiveRestrict && effectiveSelected.size === 0
  // With the data source unreachable its object list is empty, and a scope change would prune the allowlist to
  // nothing; a later save would then lose it. Nothing here can be edited until the connection works.
  const locked = catalogControlsLocked(data.catalogError)
  // Hub mode: a super admin's data source is read-only for app admins, and some admins may not touch connections at all.
  const readOnlyReason = lockReason(data)
  const connectionNote = readOnlyReason ?? connectionReason(data)

  // Switching scope must drop selections that are no longer valid for it — otherwise the
  // checklist shows one thing and the saved payload carries another (e.g. views saved under
  // a tables-only scope, which yields an empty business-facing catalog).
  function handleScopeChange(value: string) {
    const next = value as CatalogScope
    setScope(next)
    if (!data) return
    const kindByKey = new Map<string, string>(
      data.objects.map((o) => [`${o.schemaName}.${o.name}`, o.kind as string]),
    )
    const unavailableKeys = new Set((data.unavailable ?? []).map((o) => `${o.schemaName}.${o.name}`))
    const pruned = pruneAllowlist(effectiveSelected, kindByKey, unavailableKeys, next)
    if (pruned.size !== effectiveSelected.size) setSelected(pruned)
  }

  function handleRestrictChange(checked: boolean) {
    setRestrict(checked)
    // Turning the switch on with an empty selection would save as "allow nothing", which the
    // backend reads as "no restriction" — start from everything in scope instead.
    if (checked && selected === null) {
      const unavailableKeys = new Set((data?.unavailable ?? []).map((o) => `${o.schemaName}.${o.name}`))
      setSelected(
        initialAllowlist(
          visibleObjects.map((o) => `${o.schemaName}.${o.name}`),
          data?.allowedObjects ?? [],
          unavailableKeys,
        ),
      )
    }
  }

  function toggleObject(key: string, checked: boolean) {
    const next = new Set(effectiveSelected)
    if (checked) next.add(key)
    else next.delete(key)
    setSelected(next)
  }

  function handleSave() {
    updatePolicy.mutate({
      catalogScope: effectiveScope,
      allowedObjects: effectiveRestrict ? Array.from(effectiveSelected) : [],
    })
  }

  return (
    <div className="mx-auto flex max-w-4xl flex-col gap-(--space-section) p-(--space-section)">
      <div className="flex items-center gap-2">
        <Button variant="ghost" size="icon" className="size-8" onClick={() => navigate('/admin')}>
          <ArrowLeft className="size-4" />
        </Button>
        <div>
          <h1 className="flex items-center gap-2 text-[1.75rem] leading-tight font-bold tracking-tight">
            {data.name}
            {data.locked && (
              <Badge variant="secondary" className="gap-1 text-xs font-normal">
                <Lock className="size-3" aria-hidden />
                Locked
              </Badge>
            )}
            {!data.isActive && (
              <Badge variant="outline" className="text-xs font-normal">
                Inactive
              </Badge>
            )}
          </h1>
          <p className="text-sm text-muted-foreground">Control which tables and views this data source exposes to the catalog.</p>
        </div>
      </div>

      {readOnlyReason && (
        <p role="status" className="flex items-center gap-2 rounded-md border border-border bg-muted/50 px-3 py-2 text-sm">
          <Lock className="size-4 shrink-0" aria-hidden />
          {readOnlyReason}. You can read everything here but not change it.
        </p>
      )}

      {/* A disabled fieldset turns off every control inside it, so nothing has to be wired one by one. */}
      <fieldset disabled={!!readOnlyReason} className="m-0 flex min-w-0 flex-col gap-(--space-section) border-0 p-0">
      {connectionNote && !readOnlyReason && <p className="text-xs text-muted-foreground">{connectionNote}</p>}
      {/* Remounts whenever anything the section edits changes on the server, so its drafts (and the password) restart clean. */}
      <fieldset disabled={!!connectionNote} className="m-0 min-w-0 border-0 p-0">
        <ConnectionSection
          key={`${data.id}|${data.name}|${data.description ?? ''}|${data.connectionUnreadable}|${JSON.stringify(data.connection)}`}
          dataSource={data}
        />
      </fieldset>

      <div className="flex flex-col gap-2">
        <h2 className="text-sm font-semibold">Catalog scope</h2>
        <Tabs value={effectiveScope} onValueChange={handleScopeChange}>
          <TabsList>
            <TabsTrigger value="views" disabled={locked}>Views only</TabsTrigger>
            <TabsTrigger value="tables" disabled={locked}>Tables only</TabsTrigger>
            <TabsTrigger value="tablesAndViews" disabled={locked}>Tables + Views</TabsTrigger>
          </TabsList>
        </Tabs>
      </div>

      <div className="flex items-center gap-3">
        <Switch checked={effectiveRestrict} onCheckedChange={handleRestrictChange} disabled={locked} />
        <div>
          <p className="text-sm font-medium">Restrict to selected objects</p>
          <p className="text-xs text-muted-foreground">
            Off: every table/view matching the scope above is exposed. On: only the ones checked below.
          </p>
        </div>
      </div>

      {effectiveRestrict && (
        <div className="flex flex-col gap-2">
          <div className="relative w-64">
            <Search className="absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground" />
            <Input value={search} onChange={(e) => setSearch(e.target.value)} placeholder="Search tables, views…" className="h-8 pl-8 text-xs" />
          </div>
          <div className="max-h-96 overflow-auto rounded-lg border border-border">
            {visibleObjects.map((o) => {
              const key = `${o.schemaName}.${o.name}`
              return (
                <label key={key} className="flex items-center gap-2 border-b border-border px-3 py-2 text-sm last:border-b-0 hover:bg-muted/50">
                  <Checkbox checked={effectiveSelected.has(key)} onCheckedChange={(checked) => toggleObject(key, checked === true)} />
                  <span className="flex-1 truncate">{key}</span>
                  <Badge variant="secondary" className="text-[10px] font-normal">
                    {o.kind}
                  </Badge>
                </label>
              )
            })}
            {visibleObjects.length === 0 && unavailableObjects.length === 0 && (
              <div className="px-3 py-6 text-center text-sm text-muted-foreground">No tables/views match the current scope.</div>
            )}
          </div>
        </div>
      )}

      {unavailableObjects.length > 0 && (
        <div className="flex flex-col gap-2">
          <h2 className="text-sm font-semibold">Unavailable objects</h2>
          <div className="rounded-lg border border-border">
            {unavailableObjects.map((o, i) => {
              const key = `${o.schemaName}.${o.name}`
              const reasonId = `${idPrefix}-unavailable-${i}`
              return (
                <div key={objectRowKey(o.schemaName, o.name)} className="flex items-start gap-2 border-b border-border px-3 py-2 text-sm text-muted-foreground last:border-b-0">
                  <Checkbox checked={effectiveSelected.has(key)} disabled className="mt-0.5" aria-label={qualifiedName(o.schemaName, o.name)} aria-describedby={reasonId} />
                  <div className="min-w-0 flex-1">
                    <span className="flex items-center gap-1.5">
                      <AlertTriangle className="size-3.5 shrink-0 text-warning" aria-hidden="true" />
                      <span className="min-w-0 flex-1 truncate">{qualifiedName(o.schemaName, o.name)}</span>
                    </span>
                    <p id={reasonId} className="text-xs">
                      {adminUnavailableReason(o.clashesWith)}
                    </p>
                  </div>
                  <Badge variant="secondary" className="shrink-0 text-[10px] font-normal">
                    {o.kind}
                  </Badge>
                </div>
              )
            })}
          </div>
        </div>
      )}

      <div className="flex items-center gap-3">
        <Button onClick={handleSave} disabled={updatePolicy.isPending || nothingAllowed || locked}>
          {updatePolicy.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Save className="size-3.5" />}
          Save
        </Button>
        {locked && (
          <p className="text-xs text-muted-foreground">
            The catalog can't be edited while this data source can't be reached. Fix the connection above first.
          </p>
        )}
        {nothingAllowed && (
          <p className="text-xs text-destructive">
            Select at least one table or view, or turn off "Restrict to selected objects".
          </p>
        )}
      </div>

      <RecordLimitSection key={`${data.id}:${data.maxRecords ?? ''}`} dataSource={data} />

      <DataScopeSection dataSourceId={data.id} />
      </fieldset>
    </div>
  )
}
