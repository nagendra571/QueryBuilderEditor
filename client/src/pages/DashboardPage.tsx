import { useQueryClient } from '@tanstack/react-query'
import { AlertTriangle, ArrowLeft, BarChart3, Loader2, MoreHorizontal, Pencil, Plus, Rows3, Share2, StickyNote, Trash2 } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from '@/components/ui/dropdown-menu'
import { AccessLevelBadge } from '@/features/builder/ShareDialog'
import { AddChartDialog } from '@/features/dashboards/AddChartDialog'
import { ConfirmDeleteDialog } from '@/features/dashboards/ConfirmDeleteDialog'
import { DashboardParameterBar } from '@/features/dashboards/DashboardParameterBar'
import { DashboardParameterDialog } from '@/features/dashboards/DashboardParameterDialog'
import { DashboardIdContext, DashboardValuesContext, toQueryParameter } from '@/features/dashboards/dashboard-params'
import { ParameterMappingDialog } from '@/features/dashboards/ParameterMappingDialog'
import { defaultValues, valuesFromSearchParams, writeValuesToSearchParams, type ParameterValues } from '@/features/parameters/param-values'
import { DashboardGrid, type DraftTile } from '@/features/dashboards/DashboardGrid'
import { bottomRow, newTileKey } from '@/features/dashboards/layout'
import { DashboardNameDialog } from '@/features/dashboards/DashboardNameDialog'
import { RefreshControl } from '@/features/dashboards/RefreshControl'
import { createLimiter, RenderLimiterContext } from '@/features/dashboards/render-limiter'
import { ShareDashboardDialog } from '@/features/dashboards/ShareDashboardDialog'
import { SectionTileDialog } from '@/features/dashboards/SectionTileDialog'
import { sectionBox } from '@/features/dashboards/section'
import { TextTileDialog } from '@/features/dashboards/TextTileDialog'
import {
  dashboardKey,
  isVersionConflict,
  useDashboard,
  useDeleteDashboard,
  useRenameDashboard,
  useSaveDashboardTiles,
} from '@/hooks/useDashboards'
import { visualizationDataPrefix } from '@/hooks/useVisualizations'
import { ApiError } from '@/lib/api-client'
import type {
  DashboardParameterDto,
  DashboardTileDto,
  ParameterMappingDto,
  PickableVisualizationDto,
  SaveDashboardTileRequest,
} from '@/types'

const MAX_TILES = 30
const MAX_CONCURRENT_RENDERS = 4

function toDraft(tile: DashboardTileDto): DraftTile {
  return { ...tile, key: tile.id }
}

function toRequest(tile: DraftTile, order: number): SaveDashboardTileRequest {
  return {
    id: tile.isNew ? null : tile.id,
    kind: tile.kind,
    order,
    x: tile.x,
    y: tile.y,
    width: tile.width,
    height: tile.height,
    autoHeight: tile.autoHeight,
    // A restricted tile has no chart id to send; the server keeps the one it stored.
    title: tile.restricted ? null : tile.title ?? null,
    text: tile.kind === 'text' || tile.kind === 'section' ? tile.text ?? null : null,
    visualizationId: tile.kind === 'chart' && !tile.restricted ? tile.chart?.visualizationId ?? null : null,
    // Left out (not null-cleared) for restricted tiles: the server keeps what it stored and never reads a forged value.
    parameterMappings: tile.kind === 'chart' && !tile.restricted ? tile.parameterMappings ?? undefined : undefined,
  }
}

export function DashboardPage() {
  const { id } = useParams()
  const navigate = useNavigate()
  const location = useLocation()
  const queryClient = useQueryClient()
  const { data: dashboard, isLoading, error, refetch } = useDashboard(id)
  const saveTiles = useSaveDashboardTiles(id ?? '')
  const rename = useRenameDashboard(id ?? '')
  const remove = useDeleteDashboard()

  const limiter = useMemo(() => createLimiter(MAX_CONCURRENT_RENDERS), [])
  const [draft, setDraft] = useState<DraftTile[] | null>(null)
  const [baseVersion, setBaseVersion] = useState(0)
  const [conflict, setConflict] = useState(false)
  const [lastRefreshed, setLastRefreshed] = useState<Date | null>(null)
  const [addChartOpen, setAddChartOpen] = useState(false)
  const [textDialog, setTextDialog] = useState<{ key: string | null } | null>(null)
  const [sectionDialog, setSectionDialog] = useState<{ key: string | null } | null>(null)
  const [shareOpen, setShareOpen] = useState(false)
  const [renameOpen, setRenameOpen] = useState(false)
  const [deleteOpen, setDeleteOpen] = useState(false)
  const [narrow, setNarrow] = useState(false)
  const [searchParams, setSearchParams] = useSearchParams()
  // Dashboard parameters being edited; null outside edit mode.
  const [draftParams, setDraftParams] = useState<DashboardParameterDto[] | null>(null)
  const [mapTileKey, setMapTileKey] = useState<string | null>(null)
  const [manageName, setManageName] = useState<string | null>(null)

  const savedParams = useMemo(() => dashboard?.parameters ?? [], [dashboard])
  const params = draftParams ?? savedParams
  const queryParams = useMemo(() => params.map(toQueryParameter), [params])

  // What the bar and the tiles start from: a shared link's p_ values, else the saved defaults. A
  // later bar edit or Apply is kept as an override tied to the saved parameter list, so saving new
  // parameters (a different list) falls back to the fresh starting values.
  const savedSig = JSON.stringify(savedParams)
  const searchRef = useRef(searchParams)
  searchRef.current = searchParams
  const initialValues = useMemo<ParameterValues>(() => {
    const saved = savedParams.map(toQueryParameter)
    return valuesFromSearchParams(saved, searchRef.current) ?? defaultValues(saved)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [savedSig])
  const [override, setOverride] = useState<{ sig: string; bar: ParameterValues; applied: ParameterValues } | null>(null)
  const live = override && override.sig === savedSig ? override : null
  const barValues = live?.bar ?? initialValues
  const appliedValues = live?.applied ?? initialValues
  const commitValues = (bar: ParameterValues, applied: ParameterValues) => setOverride({ sig: savedSig, bar, applied })

  const viewTiles = useMemo(() => (dashboard ? dashboard.tiles.map(toDraft) : []), [dashboard])
  const editing = draft !== null
  const tiles = draft ?? viewTiles
  const canEdit = dashboard?.accessLevel === 'owner' || dashboard?.accessLevel === 'editor'

  function beginEdit() {
    if (!dashboard) return
    setDraft(dashboard.tiles.map(toDraft))
    setDraftParams(dashboard.parameters ?? [])
    setBaseVersion(dashboard.version)
    setConflict(false)
  }

  function endEdit() {
    setDraft(null)
    setDraftParams(null)
    setConflict(false)
  }

  // A just-created dashboard opens straight into edit mode.
  const openInEdit = useRef(Boolean((location.state as { edit?: boolean } | null)?.edit))
  useEffect(() => {
    if (openInEdit.current && dashboard && canEdit) {
      openInEdit.current = false
      setDraft(dashboard.tiles.map(toDraft))
      setDraftParams(dashboard.parameters ?? [])
      setBaseVersion(dashboard.version)
      // History state survives a browser reload; clear it so a reload opens in view mode.
      navigate(location.pathname, { replace: true, state: null })
    }
  }, [dashboard, canEdit, navigate, location.pathname])

  function update(fn: (tiles: DraftTile[]) => DraftTile[]) {
    setDraft((current) => (current ? fn(current) : current))
  }

  function addChart(viz: PickableVisualizationDto) {
    setAddChartOpen(false)
    const key = newTileKey()
    update((current) => [
      ...current,
      {
        key,
        id: key,
        isNew: true,
        kind: 'chart',
        order: current.length,
        x: 0,
        y: bottomRow(current),
        width: 6,
        height: 6,
        autoHeight: false,
        restricted: false,
        missing: false,
        chart: {
          savedQueryId: viz.savedQueryId,
          savedQueryName: viz.savedQueryName,
          visualizationId: viz.visualizationId,
          visualizationName: viz.visualizationName,
          type: viz.type,
        },
      },
    ])
  }

  function saveText(title: string, text: string) {
    const target = textDialog?.key ?? null
    setTextDialog(null)
    if (target) {
      update((current) => current.map((t) => (t.key === target ? { ...t, title: title || null, text } : t)))
      return
    }
    const key = newTileKey()
    update((current) => [
      ...current,
      {
        key,
        id: key,
        isNew: true,
        kind: 'text',
        order: current.length,
        x: 0,
        y: bottomRow(current),
        width: 6,
        height: 3,
        autoHeight: false,
        restricted: false,
        missing: false,
        title: title || null,
        text,
      },
    ])
  }

  function saveSection(title: string, description: string) {
    const target = sectionDialog?.key ?? null
    setSectionDialog(null)
    if (target) {
      update((current) => current.map((t) => (t.key === target ? { ...t, title, text: description || null } : t)))
      return
    }
    const key = newTileKey()
    update((current) => [
      ...current,
      {
        key,
        id: key,
        isNew: true,
        kind: 'section',
        order: current.length,
        ...sectionBox(bottomRow(current)),
        autoHeight: false,
        restricted: false,
        missing: false,
        title,
        text: description || null,
      },
    ])
  }

  function handleSave() {
    if (!draft || !dashboard) return
    saveTiles.mutate(
      { version: baseVersion, tiles: draft.map((t, i) => toRequest(t, i)), parameters: draftParams ?? undefined },
      {
        onSuccess: () => {
          endEdit()
        },
        onError: (e) => {
          if (isVersionConflict(e)) setConflict(true)
        },
      },
    )
  }

  function handleReload() {
    endEdit()
    refetch()
  }

  function changeValue(name: string, value: string | null) {
    commitValues({ ...barValues, [name]: value }, appliedValues)
  }

  function applyValues(values: ParameterValues) {
    commitValues(values, values)
    setSearchParams(writeValuesToSearchParams(queryParams, values, searchParams), { replace: true })
  }

  function saveMappings(mappings: Record<string, ParameterMappingDto>, created: DashboardParameterDto[]) {
    const key = mapTileKey
    setMapTileKey(null)
    if (!key) return
    update((current) => current.map((t) => (t.key === key ? { ...t, parameterMappings: mappings } : t)))
    if (created.length > 0) {
      setDraftParams((current) => [...(current ?? []), ...created])
      const starts = Object.fromEntries(created.map((c) => [c.name, c.value ?? null]))
      commitValues({ ...barValues, ...starts }, { ...appliedValues, ...starts })
    }
  }

  function saveParameter(name: string, title: string, value: string | null) {
    setManageName(null)
    setDraftParams((current) => (current ?? []).map((p) => (p.name === name ? { ...p, title, value } : p)))
    commitValues({ ...barValues, [name]: value }, { ...appliedValues, [name]: value })
  }

  function removeParameter(name: string) {
    setManageName(null)
    setDraftParams((current) => (current ?? []).filter((p) => p.name !== name))
    // A tile can't keep pointing at a parameter that no longer exists (the server would refuse the save).
    update((current) =>
      current.map((t) => {
        const entries = Object.entries(t.parameterMappings ?? {})
        if (!entries.some(([, m]) => m.source === 'dashboard' && m.dashboardParameter === name)) return t
        return { ...t, parameterMappings: Object.fromEntries(entries.filter(([, m]) => !(m.source === 'dashboard' && m.dashboardParameter === name))) }
      }),
    )
    const { [name]: _bar, ...bar } = barValues
    const { [name]: _applied, ...applied } = appliedValues
    commitValues(bar, applied)
  }

  function refreshAll() {
    if (!dashboard || !id) return
    for (const tile of dashboard.tiles) {
      if (!tile.chart) continue
      queryClient.invalidateQueries({ queryKey: [...visualizationDataPrefix(tile.chart.savedQueryId), tile.chart.visualizationId] })
      queryClient.invalidateQueries({ queryKey: ['saved-query', tile.chart.savedQueryId] })
    }
    // Access can change between refreshes, so a tile may turn restricted (or stop being so).
    queryClient.invalidateQueries({ queryKey: dashboardKey(id) })
    setLastRefreshed(new Date())
  }

  if (isLoading) {
    return (
      <div className="flex items-center justify-center py-24 text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading dashboard…
      </div>
    )
  }

  if (error || !dashboard) {
    const status = error instanceof ApiError ? error.status : 0
    return (
      <div className="mx-auto flex max-w-xl flex-col items-center gap-3 py-24 text-center">
        <AlertTriangle className="size-8 text-muted-foreground" />
        <p className="text-sm font-medium">
          {status === 403
            ? "You don't have access to this dashboard."
            : status === 404
              ? 'This dashboard no longer exists.'
              : 'The dashboard could not be loaded.'}
        </p>
        <Button variant="outline" size="sm" onClick={() => navigate('/dashboards')}>
          Back to dashboards
        </Button>
      </div>
    )
  }

  const editingText = textDialog?.key ? tiles.find((t) => t.key === textDialog.key) : undefined
  const editingSection = sectionDialog?.key ? tiles.find((t) => t.key === sectionDialog.key) : undefined
  const mappingTile = mapTileKey ? tiles.find((t) => t.key === mapTileKey) ?? null : null
  const managedParam = manageName ? params.find((p) => p.name === manageName) ?? null : null
  const usedBy = (name: string) =>
    tiles.filter((t) => Object.values(t.parameterMappings ?? {}).some((m) => m.source === 'dashboard' && m.dashboardParameter === name)).length

  return (
    <RenderLimiterContext.Provider value={limiter}>
      <DashboardIdContext.Provider value={id ?? null}>
      <DashboardValuesContext.Provider value={appliedValues}>
      <div className="mx-auto flex max-w-7xl flex-col gap-(--space-section) p-(--space-section)">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <Link to="/dashboards" className="mb-1 inline-flex items-center gap-1 text-xs text-muted-foreground hover:text-foreground">
              <ArrowLeft className="size-3" /> Dashboards
            </Link>
            <h1 className="flex min-w-0 items-center gap-2 text-[1.75rem] leading-tight font-bold tracking-tight">
              <span className="truncate">{dashboard.name}</span>
              <AccessLevelBadge level={dashboard.accessLevel} />
            </h1>
            {!dashboard.isOwner && <p className="text-xs text-muted-foreground">Owned by {dashboard.ownerName}</p>}
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {!editing && <RefreshControl onRefresh={refreshAll} paused={editing} lastRefreshed={lastRefreshed} />}

            {!editing && canEdit && (
              <Button
                size="sm"
                variant="outline"
                className="h-8 text-xs"
                onClick={beginEdit}
                disabled={narrow}
                title={narrow ? 'Arranging tiles needs a wider screen' : undefined}
              >
                <Pencil className="size-3.5" />
                Edit
              </Button>
            )}
            {!editing && dashboard.isOwner && (
              <Button size="sm" variant="outline" className="h-8 text-xs" onClick={() => setShareOpen(true)}>
                <Share2 className="size-3.5" />
                Share
              </Button>
            )}
            {!editing && canEdit && (
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <Button size="icon" variant="outline" className="size-8" aria-label="More dashboard actions">
                    <MoreHorizontal className="size-3.5" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem onSelect={() => setRenameOpen(true)}>
                    <Pencil className="size-3.5" />
                    Rename
                  </DropdownMenuItem>
                  {dashboard.isOwner && (
                    <DropdownMenuItem variant="destructive" onSelect={() => setDeleteOpen(true)}>
                      <Trash2 className="size-3.5" />
                      Delete
                    </DropdownMenuItem>
                  )}
                </DropdownMenuContent>
              </DropdownMenu>
            )}

            {editing && (
              <>
                <Button size="sm" variant="outline" className="h-8 text-xs" disabled={tiles.length >= MAX_TILES} onClick={() => setAddChartOpen(true)}>
                  <BarChart3 className="size-3.5" />
                  Add chart
                </Button>
                <Button size="sm" variant="outline" className="h-8 text-xs" disabled={tiles.length >= MAX_TILES} onClick={() => setTextDialog({ key: null })}>
                  <StickyNote className="size-3.5" />
                  Add text
                </Button>
                <Button size="sm" variant="outline" className="h-8 text-xs" disabled={tiles.length >= MAX_TILES} onClick={() => setSectionDialog({ key: null })}>
                  <Rows3 className="size-3.5" />
                  Add section
                </Button>
                <Button size="sm" variant="ghost" className="h-8 text-xs" onClick={endEdit}>
                  Cancel
                </Button>
                <Button size="sm" className="h-8 text-xs" disabled={saveTiles.isPending} onClick={handleSave}>
                  {saveTiles.isPending && <Loader2 className="size-3.5 animate-spin" />}
                  Save
                </Button>
              </>
            )}
          </div>
        </div>

        {conflict && (
          <Alert variant="destructive">
            <AlertTriangle className="size-4" />
            <AlertTitle>This dashboard was changed by someone else</AlertTitle>
            <AlertDescription className="flex flex-col items-start gap-2">
              <span>Reload to see the latest version. Your unsaved changes will be discarded.</span>
              <Button size="sm" variant="outline" onClick={handleReload}>
                Reload
              </Button>
            </AlertDescription>
          </Alert>
        )}

        {editing && tiles.length >= MAX_TILES && (
          <p className="text-xs text-muted-foreground">A dashboard can have at most {MAX_TILES} tiles.</p>
        )}

        <DashboardParameterBar
          parameters={params}
          values={barValues}
          appliedValues={appliedValues}
          onChange={changeValue}
          onApply={() => applyValues(barValues)}
          onReset={() => applyValues(defaultValues(queryParams))}
          editing={editing}
          onManage={setManageName}
          dashboardId={id}
          tiles={dashboard?.tiles}
        />

        {tiles.length === 0 ? (
          <div className="flex flex-col items-center gap-3 rounded-lg border border-dashed border-border py-16 text-center">
            <p className="text-sm font-medium">This dashboard is empty</p>
            {canEdit ? (
              <>
                <p className="text-xs text-muted-foreground">Add charts from your saved queries, or a text note.</p>
                <div className="flex gap-2">
                  <Button
                    size="sm"
                    onClick={() => {
                      if (!editing) beginEdit()
                      setAddChartOpen(true)
                    }}
                  >
                    <Plus className="size-3.5" />
                    Add chart
                  </Button>
                  <Button
                    size="sm"
                    variant="outline"
                    onClick={() => {
                      if (!editing) beginEdit()
                      setTextDialog({ key: null })
                    }}
                  >
                    <Plus className="size-3.5" />
                    Add text
                  </Button>
                </div>
              </>
            ) : (
              <p className="text-xs text-muted-foreground">The owner hasn&apos;t added anything yet.</p>
            )}
          </div>
        ) : (
          <DashboardGrid
            tiles={tiles}
            editing={editing}
            onChange={update}
            onNarrowChange={setNarrow}
            onRemove={(key) => update((current) => current.filter((t) => t.key !== key))}
            onEditText={(key) =>
              tiles.find((t) => t.key === key)?.kind === 'section' ? setSectionDialog({ key }) : setTextDialog({ key })
            }
            onEditParameters={setMapTileKey}
          />
        )}
      </div>

      <ParameterMappingDialog
        tile={mappingTile}
        dashboardParameters={params}
        onOpenChange={(open) => !open && setMapTileKey(null)}
        onSave={saveMappings}
      />
      <DashboardParameterDialog
        parameter={managedParam}
        usedBy={managedParam ? usedBy(managedParam.name) : 0}
        onOpenChange={(open) => !open && setManageName(null)}
        onSave={saveParameter}
        onRemove={removeParameter}
      />
      <AddChartDialog open={addChartOpen} onOpenChange={setAddChartOpen} onPick={addChart} />
      <TextTileDialog
        open={textDialog !== null}
        onOpenChange={(open) => !open && setTextDialog(null)}
        initialTitle={editingText?.title ?? ''}
        initialText={editingText?.text ?? ''}
        onSave={saveText}
      />
      <SectionTileDialog
        open={sectionDialog !== null}
        onOpenChange={(open) => !open && setSectionDialog(null)}
        initialTitle={editingSection?.title ?? ''}
        initialDescription={editingSection?.text ?? ''}
        onSave={saveSection}
      />
      {dashboard.isOwner && (
        <ShareDashboardDialog open={shareOpen} onOpenChange={setShareOpen} dashboardId={dashboard.id} dashboardName={dashboard.name} />
      )}
      <DashboardNameDialog
        open={renameOpen}
        onOpenChange={setRenameOpen}
        title="Rename dashboard"
        confirmLabel="Rename"
        initialName={dashboard.name}
        pending={rename.isPending}
        onConfirm={(name) =>
          rename.mutate(
            { name, description: dashboard.description ?? null, version: dashboard.version },
            {
              onSuccess: () => setRenameOpen(false),
              onError: (e) => {
                if (isVersionConflict(e)) {
                  setRenameOpen(false)
                  setConflict(true)
                }
              },
            },
          )
        }
      />
      <ConfirmDeleteDialog
        open={deleteOpen}
        onOpenChange={setDeleteOpen}
        name={dashboard.name}
        pending={remove.isPending}
        onConfirm={() => remove.mutate(dashboard.id, { onSuccess: () => navigate('/dashboards') })}
      />
      </DashboardValuesContext.Provider>
      </DashboardIdContext.Provider>
    </RenderLimiterContext.Provider>
  )
}
