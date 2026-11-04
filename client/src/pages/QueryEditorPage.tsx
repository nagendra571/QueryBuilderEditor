import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Ban, ChartColumn, Code2, Download, FileSpreadsheet, History, Loader2, Play, Power, Save, Share2, Star } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { CatalogSidebar } from '@/features/catalog/CatalogSidebar'
import { ColumnsPanel } from '@/features/builder/ColumnsPanel'
import { AddFilterButton, FiltersPanel } from '@/features/builder/FiltersPanel'
import { AddHavingButton, HavingPanel } from '@/features/builder/HavingPanel'
import { HistoryPanel } from '@/features/builder/HistoryPanel'
import { ResultsTable } from '@/features/builder/ResultsTable'
import { SaveQueryDialog } from '@/features/builder/SaveQueryDialog'
import { AccessLevelBadge, ShareDialog } from '@/features/builder/ShareDialog'
import { AddSortControl, SortPanel } from '@/features/builder/SortPanel'
import { SqlPreviewDialog } from '@/features/builder/SqlPreviewDialog'
import { useBuilderStore } from '@/features/builder/builder-store'
import { ParameterBar } from '@/features/parameters/ParameterBar'
import { ParameterSettingsDialog } from '@/features/parameters/ParameterSettingsDialog'
import {
  allowedTypesFor,
  boundConditions,
  defaultValues,
  missingRequired,
  pickKnown,
  valuesFromSearchParams,
  writeValuesToSearchParams,
  type ParameterValues,
} from '@/features/parameters/param-values'
import { ChartEditorDialog } from '@/features/visualizations/ChartEditorDialog'
import { ChartPanel } from '@/features/visualizations/ChartPanel'
import { outputColumns } from '@/features/visualizations/chart-data'
import { useDataSourceCatalog } from '@/hooks/useDataSources'
import { savedQueriesKey } from '@/hooks/useSavedQueries'
import { useDeleteVisualization, useSaveVisualization, useVisualizations, visualizationDataPrefix } from '@/hooks/useVisualizations'
import { savedQueriesApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import { cn } from '@/lib/utils'
import type { ParameterType, RunQueryRequest, VisualizationDto } from '@/types'

const ALL_PARAMETER_TYPES: ParameterType[] = ['text', 'number', 'date', 'dateTime', 'dateRange', 'dateTimeRange', 'list', 'queryList', 'boolean']

export function QueryEditorPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { id } = useParams<{ id: string }>()
  const [searchParams, setSearchParams] = useSearchParams()
  const dataSourceIdFromQuery = searchParams.get('dataSourceId')

  const store = useBuilderStore()
  const { definition, dataSourceId, name, description, queryId } = store

  const [sqlDialogOpen, setSqlDialogOpen] = useState(false)
  const [saveDialogOpen, setSaveDialogOpen] = useState(false)
  const [shareDialogOpen, setShareDialogOpen] = useState(false)
  const [activeTab, setActiveTab] = useState('build')
  const [chartEditorOpen, setChartEditorOpen] = useState(false)
  const [editingChart, setEditingChart] = useState<VisualizationDto | null>(null)
  const [chartToDelete, setChartToDelete] = useState<VisualizationDto | null>(null)
  // What the user has typed into the parameter bar on top of the saved defaults, and what the
  // grid/charts were last run with. Values shown in the bar are derived from these on every render.
  const [overrides, setOverrides] = useState<ParameterValues>({})
  const [appliedValues, setAppliedValues] = useState<ParameterValues | null>(null)
  const [urlReadyFor, setUrlReadyFor] = useState<string | null>(null)
  const previousQueryId = useRef<string | null>(null)

  const existingQuery = useQuery({
    queryKey: ['saved-query', id],
    queryFn: () => savedQueriesApi.getById(id!),
    enabled: !!id,
  })

  useEffect(() => {
    if (id && existingQuery.data) {
      store.loadExisting(existingQuery.data.id, existingQuery.data.dataSourceId, existingQuery.data.name, existingQuery.data.description ?? '', existingQuery.data.definition)
    } else if (!id && dataSourceIdFromQuery) {
      store.startNew(dataSourceIdFromQuery)
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [id, existingQuery.data, dataSourceIdFromQuery])

  const { data: catalog } = useDataSourceCatalog(dataSourceId ?? undefined)
  const activeObject = catalog?.schemas
    .flatMap((s) => s.objects)
    .find((o) => o.schemaName === definition.source.schemaName && o.name === definition.source.objectName)
  const availableColumns = activeObject?.columns ?? []

  const parameterValues = useMemo<ParameterValues>(() => {
    const defaults = defaultValues(definition.parameters)
    const merged: ParameterValues = {}
    for (const p of definition.parameters) merged[p.name] = p.name in overrides ? overrides[p.name] : defaults[p.name]
    return merged
  }, [definition.parameters, overrides])
  const missing = missingRequired(definition.parameters, parameterValues)

  // A different saved query starts from its own defaults; saving a brand-new query keeps what was typed.
  const scope = queryId ?? (dataSourceId ? `new:${dataSourceId}` : null)
  useEffect(() => {
    if (!scope) return
    const previous = previousQueryId.current
    previousQueryId.current = queryId
    if (previous && queryId && previous !== queryId) {
      setOverrides({})
      setAppliedValues(null)
    }
    // A shared link carries its values as p_<name>; they fill the bar and also drive the charts.
    const fromUrl = valuesFromSearchParams(definition.parameters, searchParams)
    if (fromUrl && urlReadyFor !== scope) {
      setOverrides(fromUrl)
      setAppliedValues(fromUrl)
    }
    setUrlReadyFor(scope)
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [scope])

  const urlKey = useMemo(() => JSON.stringify(parameterValues), [parameterValues])
  useEffect(() => {
    if (!scope || urlReadyFor !== scope) return
    const next = writeValuesToSearchParams(definition.parameters, parameterValues, searchParams)
    if (next.toString() !== searchParams.toString()) setSearchParams(next, { replace: true })
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [urlKey, urlReadyFor, scope])

  const previewMutation = useMutation({
    mutationFn: () => savedQueriesApi.previewSql({ ...buildRunRequest(dataSourceId!, definition, queryId), parameterValues: parameterValues }),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not build SQL preview.'),
  })

  const runMutation = useMutation({
    mutationFn: (runValues: ParameterValues) =>
      savedQueriesApi.run({ ...buildRunRequest(dataSourceId!, definition, queryId), parameterValues: runValues }),
    onSuccess: () => {
      setActiveTab('results')
      if (queryId) queryClient.invalidateQueries({ queryKey: ['audit', queryId] })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'The query failed to run.'),
  })

  const exportMutation = useMutation({
    mutationFn: async ({ format, exportValues }: { format: 'csv' | 'xlsx'; exportValues: ParameterValues }) => {
      const blob = await savedQueriesApi.export({
        dataSourceId: dataSourceId!,
        definition,
        parameterValues: exportValues,
        format,
        fileName: name || 'query-results',
        savedQueryId: queryId,
      })
      downloadBlob(blob, `${name || 'query-results'}.${format}`)
    },
    onSuccess: () => {
      if (queryId) queryClient.invalidateQueries({ queryKey: ['audit', queryId] })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Export failed.'),
  })

  const [saveAsCopy, setSaveAsCopy] = useState(false)

  const saveMutation = useMutation({
    mutationFn: (input: { name: string; description: string; asCopy: boolean }) =>
      savedQueriesApi.save({
        id: input.asCopy ? null : queryId,
        name: input.name,
        description: input.description,
        dataSourceId: dataSourceId!,
        definition,
      }),
    onSuccess: (result, input) => {
      toast.success(input.asCopy ? 'Saved as a new query.' : 'Query saved.')
      setSaveDialogOpen(false)
      queryClient.invalidateQueries({ queryKey: savedQueriesKey })
      if (input.asCopy) {
        navigate(`/queries/${result.id}`, { replace: true })
        return
      }
      store.setName(input.name)
      store.setDescription(input.description)
      queryClient.invalidateQueries({ queryKey: ['audit', queryId ?? result.id] })
      // Charts are drawn from the saved definition, so a re-save changes what they show and which columns they can use.
      queryClient.invalidateQueries({ queryKey: ['saved-query', queryId ?? result.id] })
      queryClient.invalidateQueries({ queryKey: visualizationDataPrefix(queryId ?? result.id) })
      if (!queryId) navigate(`/queries/${result.id}`, { replace: true })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not save the query.'),
  })

  const favoriteMutation = useMutation({
    mutationFn: () => savedQueriesApi.toggleFavorite(queryId!),
  })

  const disabledMutation = useMutation({
    mutationFn: () => savedQueriesApi.toggleDisabled(queryId!),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ['saved-query', queryId] })
      queryClient.invalidateQueries({ queryKey: savedQueriesKey })
      queryClient.invalidateQueries({ queryKey: visualizationDataPrefix(queryId!) })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not update the query.'),
  })

  const myAccessLevel = existingQuery.data?.myAccessLevel ?? 'owner'
  const isViewer = myAccessLevel === 'viewer'
  const isOwner = myAccessLevel === 'owner'
  const isDisabled = existingQuery.data?.isDisabled ?? false

  const visualizations = useVisualizations(queryId)
  const saveVisualization = useSaveVisualization(queryId ?? '')
  const deleteVisualization = useDeleteVisualization(queryId ?? '')
  const savedDefinition = existingQuery.data?.definition
  const canEditCharts = !!queryId && !!existingQuery.data && !isViewer
  const charts = visualizations.data ?? []

  const hasColumns = definition.columns.some((c) => c.isVisible)
  const canQuery = hasColumns && !!dataSourceId
  const hasAggregates = definition.columns.some((c) => c.aggregate !== 'none')

  const hasParameters = definition.parameters.length > 0
  const blockedReason = isDisabled
    ? 'This query is disabled and cannot be run.'
    : !canQuery
      ? 'Pick at least one column to run the query.'
      : undefined

  // Run and Apply are the same action when the query has parameters: run with exactly what the bar shows.
  function applyAndRun() {
    if (missing.length > 0) return
    setAppliedValues(parameterValues)
    runMutation.mutate(parameterValues)
  }

  function exportAs(format: 'csv' | 'xlsx') {
    if (missing.length > 0) return
    exportMutation.mutate({ format, exportValues: parameterValues })
  }

  const settingsParameter = definition.parameters.find((p) => p.name === store.parameterSettingsFor) ?? null
  const settingsBound = settingsParameter ? boundConditions(definition, settingsParameter.name) : []
  const settingsAllowedTypes = settingsBound.length
    ? ALL_PARAMETER_TYPES.filter((t) => settingsBound.every((c) => allowedTypesFor(c.dataType, c.operator).includes(t)))
    : ALL_PARAMETER_TYPES
  const settingsCanAllowMultiple = settingsBound.length > 0 && settingsBound.every((c) => c.operator === 'in' || c.operator === 'notIn')

  if (id && existingQuery.isLoading) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        <Loader2 className="mr-2 size-4 animate-spin" /> Loading query…
      </div>
    )
  }

  if (!dataSourceId) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
        Missing data source. Go back and start a new query from the list.
      </div>
    )
  }

  return (
    <div className="flex h-full min-h-0 flex-col md:flex-row">
      <div className="h-56 shrink-0 border-b border-border md:h-auto md:w-72 md:border-b-0">
        <CatalogSidebar dataSourceId={dataSourceId} />
      </div>

      <div className="flex min-h-0 min-w-0 flex-1 flex-col">
        <div className="flex flex-wrap items-center gap-2 border-b border-border px-(--space-row-x) py-2">
          <Button variant="ghost" size="icon" className="size-8" onClick={() => navigate('/')}>
            <ArrowLeft className="size-4" />
          </Button>

          <div className="flex min-w-0 flex-col">
            <span className="flex items-center gap-1.5 truncate text-sm font-semibold">
              {name || 'Untitled query'}
              <AccessLevelBadge level={myAccessLevel} />
              {isDisabled && (
                <Badge variant="outline" className="h-4 gap-1 px-1.5 text-[10px] font-normal text-muted-foreground">
                  <Ban className="size-2.5" />
                  Disabled
                </Badge>
              )}
            </span>
            {definition.source.objectName && (
              <span className="truncate text-[11px] text-muted-foreground">
                {definition.source.schemaName}.{definition.source.objectName}
              </span>
            )}
          </div>

          {queryId && (
            <Button variant="ghost" size="icon" className="size-7" onClick={() => favoriteMutation.mutate()}>
              <Star className={favoriteMutation.data?.isFavorite ? 'size-4 fill-warning text-warning' : 'size-4'} />
            </Button>
          )}

          <div className="ml-auto flex flex-wrap items-center gap-1.5">
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery}
              onClick={() => {
                setSqlDialogOpen(true)
                previewMutation.mutate()
              }}
            >
              <Code2 className="size-3.5" />
              Show query
            </Button>

            <Button
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery || isDisabled || runMutation.isPending || missing.length > 0}
              title={isDisabled ? 'This query is disabled and cannot be run.' : missing.length > 0 ? 'Fill in the required parameters first.' : undefined}
              onClick={applyAndRun}
            >
              {runMutation.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" />}
              Run
            </Button>

            <Separator orientation="vertical" className="h-5" />

            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery || isDisabled || exportMutation.isPending || missing.length > 0}
              title={isDisabled ? 'This query is disabled and cannot be exported.' : missing.length > 0 ? 'Fill in the required parameters first.' : undefined}
              onClick={() => exportAs('csv')}
            >
              <Download className="size-3.5" />
              CSV
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery || isDisabled || exportMutation.isPending || missing.length > 0}
              title={isDisabled ? 'This query is disabled and cannot be exported.' : missing.length > 0 ? 'Fill in the required parameters first.' : undefined}
              onClick={() => exportAs('xlsx')}
            >
              <FileSpreadsheet className="size-3.5" />
              Excel
            </Button>

            <Separator orientation="vertical" className="h-5" />

            {queryId && isOwner && (
              <Button
                variant="outline"
                size="sm"
                className="h-8 text-xs"
                disabled={disabledMutation.isPending}
                onClick={() => disabledMutation.mutate()}
              >
                {isDisabled ? <Power className="size-3.5" /> : <Ban className="size-3.5" />}
                {isDisabled ? 'Enable' : 'Disable'}
              </Button>
            )}

            {queryId && isOwner && (
              <Button variant="outline" size="sm" className="h-8 text-xs" onClick={() => setShareDialogOpen(true)}>
                <Share2 className="size-3.5" />
                Share
              </Button>
            )}

            <Button
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery}
              onClick={() => {
                setSaveAsCopy(isViewer)
                setSaveDialogOpen(true)
              }}
            >
              <Save className="size-3.5" />
              {isViewer ? 'Save a copy' : 'Save'}
            </Button>
          </div>
        </div>

        {hasParameters && (
          <ParameterBar
            parameters={definition.parameters}
            values={parameterValues}
            appliedValues={appliedValues}
            onChange={(name, value) => setOverrides((prev) => ({ ...prev, [name]: value }))}
            onApply={applyAndRun}
            isApplying={runMutation.isPending}
            applyDisabledReason={blockedReason}
            canEdit={!isViewer}
            onOpenSettings={store.openParameterSettings}
            onSaveDefaults={() => {
              store.setParameterDefaults(parameterValues)
              toast.success('Defaults updated. Save the query to keep them.')
            }}
            optionsSourceFor={(parameter) => ({ kind: 'draft', parameter })}
          />
        )}

        <Tabs value={activeTab} onValueChange={setActiveTab} className="flex min-h-0 flex-1 flex-col gap-0">
          <div className="mx-(--space-row-x) mt-2 flex min-w-0 flex-wrap items-center gap-2">
          <TabsList className="w-fit max-w-full overflow-x-auto">
            <TabsTrigger value="build" className="text-xs">
              Build
            </TabsTrigger>
            <TabsTrigger value="results" className="text-xs">
              Results
              {runMutation.data && (
                <Badge variant="secondary" className="ml-1 h-4 px-1 text-[10px]">
                  {runMutation.data.rowCount}
                </Badge>
              )}
            </TabsTrigger>
            {charts.map((chart) => (
              <TabsTrigger key={chart.id} value={`chart:${chart.id}`} className="min-w-0 gap-1 text-xs">
                <ChartColumn className="size-3.5 shrink-0" />
                <span className="max-w-40 min-w-0 truncate">{chart.name}</span>
              </TabsTrigger>
            ))}
            {queryId && (
              <TabsTrigger value="history" className="gap-1 text-xs">
                <History className="size-3.5" />
                History
              </TabsTrigger>
            )}
          </TabsList>
          {canEditCharts && hasColumns && (
            <Button
              variant="outline"
              size="sm"
              className="h-8 shrink-0 text-xs"
              onClick={() => {
                setEditingChart(null)
                setChartEditorOpen(true)
              }}
            >
              <ChartColumn className="size-3.5" />
              Add chart
            </Button>
          )}
          </div>

          <TabsContent value="build" className="min-h-0 flex-1 overflow-auto p-(--space-section)">
            {!definition.source.objectName ? (
              <div className="flex h-full items-center justify-center text-center text-sm text-muted-foreground">
                Pick a table or view from the catalog on the left to start building your query.
              </div>
            ) : (
              // The builder reads as a course: numbered checkpoints from source to sort, joined by a route line.
              <ol className="relative flex flex-col gap-(--space-section)" aria-label="Query steps">
                <span aria-hidden className="absolute top-4 bottom-4 left-[13px] w-0.5 bg-query/35" />
                <BuilderSection step={1} done title="Source" description="The table or view this query reads from.">
                  <p className="rounded-md border border-border bg-card px-3 py-2 text-sm">
                    <span className="text-muted-foreground">{definition.source.schemaName}.</span>
                    <span className="font-semibold">{definition.source.objectName}</span>
                  </p>
                </BuilderSection>

                <BuilderSection
                  step={2}
                  done={definition.columns.length > 0}
                  title="Columns"
                  description={
                    hasAggregates
                      ? 'Give a column a total (Sum, Count...) to summarize it — every other column is automatically grouped.'
                      : 'Choose and order the columns to include in your results. Give one a total (Sum, Count...) to summarize instead of listing every row.'
                  }
                >
                  <ColumnsPanel />
                </BuilderSection>

                <BuilderSection
                  step={3}
                  done={definition.filters.conditions.length + definition.filters.groups.length > 0}
                  title="Filters (WHERE)"
                  description="Narrow down the rows returned, before totals are calculated."
                  action={<AddFilterButton disabled={availableColumns.length === 0} />}
                >
                  <FiltersPanel availableColumns={availableColumns} />
                </BuilderSection>

                {hasAggregates && (
                  <BuilderSection
                    step={4}
                    done={!!definition.having && definition.having.conditions.length + definition.having.groups.length > 0}
                    title="Filter the totals (HAVING)"
                    description="Narrow down groups after totals are calculated — e.g. only regions where Sum of Amount is above 10,000."
                    action={<AddHavingButton />}
                  >
                    <HavingPanel />
                  </BuilderSection>
                )}

                <BuilderSection
                  step={hasAggregates ? 5 : 4}
                  done={definition.sorts.length > 0}
                  title="Sort"
                  description="Control the order rows come back in."
                  action={<AddSortControl availableColumns={availableColumns} />}
                >
                  <SortPanel />
                </BuilderSection>
              </ol>
            )}
          </TabsContent>

          <TabsContent value="results" className="min-h-0 flex-1 overflow-hidden">
            {runMutation.isPending && (
              <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                <Loader2 className="mr-2 size-4 animate-spin" /> Running query…
              </div>
            )}
            {!runMutation.isPending && runMutation.data && <ResultsTable result={runMutation.data} />}
            {!runMutation.isPending && !runMutation.data && (
              <div className="flex h-full items-center justify-center text-sm text-muted-foreground">
                Run the query to see results here.
              </div>
            )}
          </TabsContent>

          {queryId &&
            charts.map((chart) => (
              <TabsContent key={chart.id} value={`chart:${chart.id}`} className="min-h-0 flex-1 overflow-auto">
                <ChartPanel
                  queryId={queryId}
                  visualization={chart}
                  parameters={savedDefinition?.parameters ?? []}
                  parameterValues={appliedValues ? pickKnown(savedDefinition?.parameters ?? [], appliedValues) : null}
                  isDisabled={isDisabled}
                  canEdit={canEditCharts}
                  onEdit={() => {
                    setEditingChart(chart)
                    setChartEditorOpen(true)
                  }}
                  onDelete={() => setChartToDelete(chart)}
                />
              </TabsContent>
            ))}

          {queryId && (
            <TabsContent value="history" className="min-h-0 flex-1 overflow-auto">
              <HistoryPanel queryId={queryId} />
            </TabsContent>
          )}
        </Tabs>
      </div>

      <SqlPreviewDialog open={sqlDialogOpen} onOpenChange={setSqlDialogOpen} preview={previewMutation.data} isLoading={previewMutation.isPending} />

      <SaveQueryDialog
        open={saveDialogOpen}
        onOpenChange={setSaveDialogOpen}
        title={saveAsCopy ? 'Save as a new query' : 'Save query'}
        initialName={saveAsCopy ? `${name} (copy)` : name}
        initialDescription={description}
        isSaving={saveMutation.isPending}
        onConfirm={(n, d) => saveMutation.mutate({ name: n, description: d, asCopy: saveAsCopy })}
      />

      {queryId && (
        <ShareDialog
          open={shareDialogOpen}
          onOpenChange={setShareDialogOpen}
          queryId={queryId}
          dataSourceId={dataSourceId!}
          queryName={name}
        />
      )}

      {savedDefinition && (
        <ChartEditorDialog
          open={chartEditorOpen}
          onOpenChange={setChartEditorOpen}
          columns={outputColumns(savedDefinition.columns)}
          editing={editingChart}
          previewResult={runMutation.data}
          isSaving={saveVisualization.isPending}
          onSave={(request) =>
            saveVisualization.mutate(
              { id: editingChart?.id, request },
              {
                onSuccess: (saved) => {
                  setChartEditorOpen(false)
                  setActiveTab(`chart:${saved.id}`)
                },
              },
            )
          }
        />
      )}

      <Dialog open={!!chartToDelete} onOpenChange={(open) => !open && setChartToDelete(null)}>
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Delete chart</DialogTitle>
            <DialogDescription>
              Delete “{chartToDelete?.name}”? The query and its results are not affected.
            </DialogDescription>
          </DialogHeader>
          <DialogFooter>
            <Button variant="outline" onClick={() => setChartToDelete(null)}>
              Cancel
            </Button>
            <Button
              variant="destructive"
              disabled={deleteVisualization.isPending}
              onClick={() => {
                const target = chartToDelete
                if (!target) return
                deleteVisualization.mutate(target.id, {
                  onSuccess: () => {
                    setChartToDelete(null)
                    if (activeTab === `chart:${target.id}`) setActiveTab('results')
                  },
                })
              }}
            >
              Delete
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>

      <ParameterSettingsDialog
        parameter={settingsParameter}
        allowedTypes={settingsAllowedTypes}
        canAllowMultiple={settingsCanAllowMultiple}
        currentQueryId={queryId}
        onClose={() => store.openParameterSettings(null)}
        onSave={(updated) => {
          store.updateParameter(updated.name, updated)
          // The default may have changed; drop what was typed so the bar shows it.
          setOverrides((prev) => {
            const { [updated.name]: _removed, ...rest } = prev
            return rest
          })
          store.openParameterSettings(null)
        }}
      />
    </div>
  )
}

function BuilderSection({
  step,
  done,
  title,
  description,
  action,
  children,
}: {
  step: number
  /** The checkpoint has something in it; an empty one stays an open circle. */
  done: boolean
  title: string
  description: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <li className="group/step relative flex gap-3">
      <span
        aria-hidden
        className={cn(
          'relative z-10 mt-0.5 flex size-7 shrink-0 items-center justify-center rounded-full border-2 font-numeric text-sm font-semibold transition-shadow duration-200',
          done ? 'border-query bg-query text-query-foreground' : 'border-query bg-background text-query',
          // The checkpoint you're working in gets the course's double ring.
          'group-focus-within/step:shadow-[0_0_0_3px_var(--background),0_0_0_5px_var(--query)]',
        )}
      >
        {step}
      </span>
      <section className="flex min-w-0 flex-1 flex-col gap-2" aria-label={`Step ${step}: ${title}`}>
        <div className="flex min-h-7 items-start justify-between gap-3">
          <div>
            <h2 className="text-sm font-semibold">{title}</h2>
            <p className="text-xs text-muted-foreground">{description}</p>
          </div>
          {action}
        </div>
        {children}
      </section>
    </li>
  )
}

function buildRunRequest(dataSourceId: string, definition: RunQueryRequest['definition'], savedQueryId: string | null): RunQueryRequest {
  return { dataSourceId, definition, parameterValues: {}, maxRows: 1000, savedQueryId }
}

function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  URL.revokeObjectURL(url)
}
