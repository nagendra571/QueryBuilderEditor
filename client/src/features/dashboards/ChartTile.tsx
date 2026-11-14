import { AlertTriangle, Ban, Check, Loader2, Lock, SlidersHorizontal, Trash2 } from 'lucide-react'
import { useContext, useId, useMemo, useState, type ReactNode } from 'react'
import { useQuery } from '@tanstack/react-query'
import { Button } from '@/components/ui/button'
import { ParameterPromptDialog } from '@/features/builder/ParameterPromptDialog'
import { DashboardIdContext, DashboardValuesContext, mappedBy, resolveTileValues } from '@/features/dashboards/dashboard-params'
import { useRenderLimiter } from '@/features/dashboards/render-limiter'
import { Label } from '@/components/ui/label'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import { isBlank, parameterTitle, valuesEqual, type ParameterValues } from '@/features/parameters/param-values'
import { ChartDownloadMenu } from '@/features/visualizations/ChartDownloadMenu'
import { ChartRenderer } from '@/features/visualizations/ChartRenderer'
import { isHub } from '@/features/hub/permissions'
import { useMe, zoneLabel } from '@/hooks/useMe'
import { useVisualizations, visualizationDataPrefix } from '@/hooks/useVisualizations'
import { dashboardsApi, savedQueriesApi, visualizationsApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import { cn } from '@/lib/utils'
import type { DashboardContext, DashboardTileDto, QueryParameter } from '@/types'

type Failure = { kind: 'noAccess' | 'disabled' | 'tooMany' | 'stale' | 'error'; message: string }

function classify(error: unknown): Failure {
  if (error instanceof ApiError) {
    if (error.status === 403) return { kind: 'noAccess', message: "You don't have access to this chart." }
    if (error.status === 404) return { kind: 'error', message: 'This chart or its query could not be found.' }
    if (error.status === 409 && error.title === 'Query disabled') return { kind: 'disabled', message: "This chart's query is disabled." }
    if (error.status === 409) return { kind: 'tooMany', message: error.message }
    if (error.status === 400 && error.message.startsWith('This chart needs updating')) {
      return { kind: 'stale', message: error.message.replace(/^This chart needs updating:\s*/, '') }
    }
    return { kind: 'error', message: error.message }
  }
  return { kind: 'error', message: 'The chart could not be drawn.' }
}

function Notice({
  icon,
  title,
  message,
  action,
  fenced,
}: {
  icon: ReactNode
  title: string
  message: string
  action?: ReactNode
  /** Drawn as no-access ground: the viewer's zone doesn't reach this data. */
  fenced?: boolean
}) {
  return (
    <div
      className={cn(
        'flex h-full min-h-0 flex-col items-center justify-center gap-1.5 overflow-auto rounded-sm p-2 text-center text-sm text-muted-foreground',
        fenced && 'zone-hatch',
      )}
    >
      <span aria-hidden="true" className={cn(fenced && 'rounded-full border border-border bg-card p-1.5')}>
        {icon}
      </span>
      <span className="font-medium text-foreground">{title}</span>
      <span className="text-xs">{message}</span>
      {action}
    </div>
  )
}

/** The tile header and body for any chart tile; the page-level frame (drag handle, menu) is the caller's. */
export function ChartTileBody({ tile, titleId, statusId }: { tile: DashboardTileDto; titleId: string; statusId: string }) {
  const natural = tile.autoHeight
  const limit = useRenderLimiter()
  const dashboardValues = useContext(DashboardValuesContext)
  const dashboardId = useContext(DashboardIdContext)
  const context = useMemo(() => (dashboardId ? { dashboardId, tileId: tile.id } : undefined), [dashboardId, tile.id])
  // Values typed into the "Set values" prompt, for parameters that have no mapping.
  const [promptValues, setPromptValues] = useState<ParameterValues | null>(null)
  // Values applied in this tile's own header controls (parameters mapped to "Tile input").
  const [tileValues, setTileValues] = useState<ParameterValues>({})
  const [promptOpen, setPromptOpen] = useState(false)

  const { data: me, isLoading: meLoading } = useMe()
  // Under the hub a tile asks the dashboard for its chart, because the viewer may not be allowed to open the query.
  const hubTile = isHub(me) && !!context

  const chart = tile.chart
  const queryId = chart?.savedQueryId ?? ''
  const vizId = chart?.visualizationId ?? ''

  // Parameters and the disabled flag live on the saved query; a viewer of this tile can read it.
  const queryDetail = useQuery({
    queryKey: ['saved-query', queryId],
    queryFn: () => savedQueriesApi.getById(queryId),
    enabled: !!chart && !meLoading && !hubTile,
    retry: false,
    staleTime: 30_000,
  })
  const tileChart = useQuery({
    queryKey: ['tile-chart', context?.dashboardId, tile.id],
    queryFn: () => dashboardsApi.tileChart(context!.dashboardId, context!.tileId),
    enabled: !!chart && hubTile,
    retry: false,
    staleTime: 30_000,
  })
  const vizList = useVisualizations(chart && !meLoading && !hubTile ? queryId : null)
  const visualization = hubTile ? tileChart.data?.visualization : vizList.data?.find((v) => v.id === vizId)
  const detail = hubTile ? tileChart : queryDetail

  const parameters: QueryParameter[] = hubTile
    ? (tileChart.data?.parameters ?? []).map((p) => ({ ...p, optionsQueryId: null }))
    : (queryDetail.data?.definition.parameters ?? [])
  const mappings = tile.parameterMappings
  const { values, missing } = resolveTileValues(parameters, mappings, dashboardValues, tileValues, promptValues)
  const needsValues = missing.length > 0
  const tileInputs = mappedBy(mappings, 'tile')
    .map((name) => parameters.find((p) => p.name === name))
    .filter((p): p is NonNullable<typeof p> => !!p)
  const hasUnmapped = parameters.some((p) => !mappings?.[p.name] || mappings[p.name].source === 'default')
  const needsPrompt = missing.some((m) => !m.mapping || m.mapping.source === 'default')
  const isDisabled = (hubTile ? tileChart.data?.isDisabled : queryDetail.data?.isDisabled) ?? false
  const ready = !!detail.data && !!visualization && !isDisabled && !needsValues

  const data = useQuery({
    queryKey: [...visualizationDataPrefix(queryId), vizId, values, dashboardId, tile.id],
    queryFn: () => limit(() => visualizationsApi.render(queryId, vizId, { parameterValues: values, ...context })),
    enabled: ready,
    // Errors here are meaningful tile states (no access, disabled, too many rows), not transient.
    retry: false,
    staleTime: 30_000,
  })

  const title = tile.title?.trim() || chart?.visualizationName || 'Chart'
  const zone = zoneLabel(me)
  const refreshing = data.isFetching && !!data.data

  let body: ReactNode
  let status = ''
  if (detail.isError) {
    const failure = classify(detail.error)
    status = failure.message
    body = (
      <Notice
        icon={<Lock className="size-4" />}
        title={failure.kind === 'noAccess' ? 'No access' : 'Unavailable'}
        message={failure.message}
        fenced={failure.kind === 'noAccess'}
      />
    )
  } else if (!hubTile && vizList.isError) {
    const failure = classify(vizList.error)
    status = failure.message
    body = <Notice icon={<Lock className="size-4" />} title="No access" message={failure.message} fenced />
  } else if (meLoading || detail.isLoading || (!hubTile && vizList.isLoading)) {
    status = 'Loading'
    body = <Loading />
  } else if (!visualization) {
    status = 'This chart was removed.'
    body = <Notice icon={<Trash2 className="size-4" />} title="This chart was removed" message="It no longer exists on its query." />
  } else if (isDisabled) {
    status = "This chart's query is disabled."
    body = <Notice icon={<Ban className="size-4" />} title="Query disabled" message="Charts are not drawn while the query is disabled." />
  } else if (needsValues) {
    status = 'Needs values'
    body = (
      <Notice
        icon={<SlidersHorizontal className="size-4" />}
        title="Needs values"
        message={
          needsPrompt
            ? "This chart's query asks for values before it can run."
            : `Enter ${missing.map((m) => parameterTitle(m.parameter)).join(', ')} ${
                missing.some((m) => m.mapping?.source === 'dashboard') ? 'in the dashboard parameters above.' : 'in the controls on this tile.'
              }`
        }
        action={
          needsPrompt ? (
            <Button size="sm" variant="outline" onClick={() => setPromptOpen(true)}>
              Set values
            </Button>
          ) : undefined
        }
      />
    )
  } else if (data.isError) {
    const failure = classify(data.error)
    status = failure.message
    const icon = failure.kind === 'noAccess' ? <Lock className="size-4" /> : failure.kind === 'disabled' ? <Ban className="size-4" /> : <AlertTriangle className="size-4" />
    const heading =
      failure.kind === 'noAccess' ? 'No access'
      : failure.kind === 'disabled' ? 'Query disabled'
      : failure.kind === 'tooMany' ? 'Too many rows to chart'
      : failure.kind === 'stale' ? 'This chart needs updating'
      : 'The chart could not be drawn'
    body = <Notice icon={icon} title={heading} message={failure.message} fenced={failure.kind === 'noAccess'} />
  } else if (data.data) {
    status = data.isFetching ? 'Refreshing' : data.data.rows.length === 0 ? 'No data' : 'Loaded'
    // overflow-x-hidden: charts round up by under a pixel at fractional tile widths, which would
    // otherwise reserve a scrollbar. Wide tables have their own horizontal scroller.
    body = (
      <div className={natural ? undefined : 'h-full min-h-0 overflow-y-auto overflow-x-hidden'}>
        <ChartRenderer type={visualization.type} options={visualization.options} rows={data.data.rows} name={title} fill={!natural} compact={natural} columns={data.data.columns} truncated={data.data.truncated} rowLimit={data.data.rowLimit} />
      </div>
    )
  } else {
    status = 'Loading'
    body = <Loading />
  }

  return (
    <>
      {/* The header strip restyles in place while refreshing, so the grid never shifts. */}
      <div
        className={cn(
          '-mx-3 first:-mt-3 flex h-10 shrink-0 items-center gap-2 border-b border-border px-3 transition-colors duration-300',
          refreshing ? 'bg-accent/70' : 'bg-transparent',
        )}
      >
        <h2 id={titleId} className="min-w-0 flex-1 truncate text-sm font-semibold">
          {title}
        </h2>
        {zone && (
          <span
            className="flex shrink-0 items-center gap-1.5 rounded-sm border border-border px-1.5 py-0.5 font-numeric text-xs font-medium text-muted-foreground"
            title={`Drawn with your zone: ${zone}`}
          >
            <span
              aria-hidden
              className={cn(
                'size-2.5 rounded-[1px] border border-foreground/40',
                me?.zone?.unrestricted ? 'border-foreground bg-zone-yellow' : 'bg-zone-yellow',
              )}
            />
            <span className="hidden max-w-40 truncate sm:inline">{me?.zone?.unrestricted ? 'All data' : `Your zone: ${zone}`}</span>
            <span className="sr-only sm:hidden">{me?.zone?.unrestricted ? 'All data' : `Your zone: ${zone}`}</span>
          </span>
        )}
        {hasUnmapped && parameters.length > 0 && !isDisabled && visualization && (
          <Button variant="ghost" size="icon" className="size-6 shrink-0" aria-label={`Set values for ${title}`} onClick={() => setPromptOpen(true)}>
            <SlidersHorizontal className="size-3.5" />
          </Button>
        )}
        {ready && data.data && !data.isError && (
          <ChartDownloadMenu compact queryId={queryId} visualizationId={vizId} name={title} parameterValues={values} context={context} />
        )}
        <span className="flex size-4 shrink-0 items-center justify-center" aria-hidden="true">
          {refreshing && <Loader2 className="size-3.5 animate-spin text-accent-foreground" />}
        </span>
      </div>
      {tileInputs.length > 0 && !isDisabled && visualization && (
        <TileInputs
          parameters={tileInputs}
          mappings={mappings}
          applied={tileValues}
          queryId={queryId}
          context={context}
          title={title}
          onApply={setTileValues}
        />
      )}
      <div className={natural ? undefined : 'min-h-0 flex-1'}>{body}</div>
      <p id={statusId} role="status" aria-live="polite" className="sr-only">
        {status}
      </p>
      <ParameterPromptDialog
        open={promptOpen}
        onOpenChange={setPromptOpen}
        parameters={parameters.filter((p) => !mappings?.[p.name] || mappings[p.name].source === 'default')}
        queryId={queryId}
        context={context}
        onConfirm={(v) => {
          setPromptOpen(false)
          setPromptValues(v)
        }}
      />
    </>
  )
}

function Loading() {
  return (
    <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
      <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
    </div>
  )
}

/** The small controls for parameters a tile asks its viewer for. Nothing redraws until Apply. */
function TileInputs({
  parameters,
  mappings,
  applied,
  queryId,
  context,
  title,
  onApply,
}: {
  parameters: QueryParameter[]
  mappings: DashboardTileDto['parameterMappings']
  applied: ParameterValues
  queryId: string
  context?: DashboardContext
  title: string
  onApply: (values: ParameterValues) => void
}) {
  const baseId = useId()
  const [pending, setPending] = useState<ParameterValues>({})
  const current = (p: QueryParameter) => (p.name in pending ? pending[p.name] : (applied[p.name] ?? p.defaultValue ?? null))
  const draft: ParameterValues = Object.fromEntries(parameters.map((p) => [p.name, current(p)]))
  const effectiveApplied: ParameterValues = Object.fromEntries(parameters.map((p) => [p.name, applied[p.name] ?? p.defaultValue ?? null]))
  const unapplied = !valuesEqual(parameters, draft, effectiveApplied)
  const missing = parameters.filter((p) => p.isRequired && isBlank(draft[p.name]))

  return (
    <form
      aria-label={`Values for ${title}`}
      className="-mx-3 flex shrink-0 flex-wrap items-end gap-x-3 gap-y-1.5 border-b border-border bg-muted/40 px-3 py-1.5"
      onSubmit={(e) => {
        e.preventDefault()
        if (missing.length === 0) onApply({ ...applied, ...draft })
      }}
    >
      {parameters.map((p) => {
        const id = `${baseId}-${p.name}`
        const label = mappings?.[p.name]?.title?.trim() || parameterTitle(p)
        return (
          <div key={p.name} className="flex min-w-0 flex-col gap-0.5">
            <Label htmlFor={id} className="map-label text-muted-foreground">
              {label}
            </Label>
            <ParameterControl
              parameter={{ ...p, label }}
              id={id}
              value={current(p)}
              onChange={(v) => setPending((prev) => ({ ...prev, [p.name]: v }))}
              optionsSource={{ kind: 'saved', queryId, name: p.name, context }}
            />
          </div>
        )
      })}
      <Button
        type="submit"
        size="sm"
        variant={unapplied ? 'default' : 'outline'}
        className="h-7 text-xs"
        disabled={!unapplied || missing.length > 0}
        title={missing.length > 0 ? `Enter ${missing.map(parameterTitle).join(', ')}` : undefined}
      >
        <Check className="size-3.5" />
        Apply
      </Button>
    </form>
  )
}
