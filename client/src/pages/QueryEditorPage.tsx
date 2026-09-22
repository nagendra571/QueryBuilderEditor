import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ArrowLeft, Code2, Download, FileSpreadsheet, History, Loader2, Play, Save, Star } from 'lucide-react'
import { useEffect, useState } from 'react'
import { useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { toast } from 'sonner'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Separator } from '@/components/ui/separator'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { CatalogSidebar } from '@/features/catalog/CatalogSidebar'
import { ColumnsPanel } from '@/features/builder/ColumnsPanel'
import { AddFilterButton, FiltersPanel } from '@/features/builder/FiltersPanel'
import { AddHavingButton, HavingPanel } from '@/features/builder/HavingPanel'
import { HistoryPanel } from '@/features/builder/HistoryPanel'
import { ParameterPromptDialog } from '@/features/builder/ParameterPromptDialog'
import { ResultsTable } from '@/features/builder/ResultsTable'
import { SaveQueryDialog } from '@/features/builder/SaveQueryDialog'
import { AddSortControl, SortPanel } from '@/features/builder/SortPanel'
import { SqlPreviewDialog } from '@/features/builder/SqlPreviewDialog'
import { useBuilderStore } from '@/features/builder/builder-store'
import { useDataSourceCatalog } from '@/hooks/useDataSources'
import { savedQueriesApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import type { RunQueryRequest } from '@/types'

type PendingAction = { kind: 'run' } | { kind: 'export'; format: 'csv' | 'xlsx' } | null

export function QueryEditorPage() {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const { id } = useParams<{ id: string }>()
  const [searchParams] = useSearchParams()
  const dataSourceIdFromQuery = searchParams.get('dataSourceId')

  const store = useBuilderStore()
  const { definition, dataSourceId, name, description, queryId } = store

  const [sqlDialogOpen, setSqlDialogOpen] = useState(false)
  const [saveDialogOpen, setSaveDialogOpen] = useState(false)
  const [paramDialogOpen, setParamDialogOpen] = useState(false)
  const [pendingAction, setPendingAction] = useState<PendingAction>(null)
  const [activeTab, setActiveTab] = useState('build')

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

  const previewMutation = useMutation({
    mutationFn: () => savedQueriesApi.previewSql(buildRunRequest(dataSourceId!, definition, queryId)),
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not build SQL preview.'),
  })

  const runMutation = useMutation({
    mutationFn: (parameterValues: Record<string, string | null>) =>
      savedQueriesApi.run({ ...buildRunRequest(dataSourceId!, definition, queryId), parameterValues }),
    onSuccess: () => {
      setActiveTab('results')
      if (queryId) queryClient.invalidateQueries({ queryKey: ['audit', queryId] })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'The query failed to run.'),
  })

  const exportMutation = useMutation({
    mutationFn: async ({ format, parameterValues }: { format: 'csv' | 'xlsx'; parameterValues: Record<string, string | null> }) => {
      const blob = await savedQueriesApi.export({
        dataSourceId: dataSourceId!,
        definition,
        parameterValues,
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

  const saveMutation = useMutation({
    mutationFn: (input: { name: string; description: string }) =>
      savedQueriesApi.save({ id: queryId, name: input.name, description: input.description, dataSourceId: dataSourceId!, definition }),
    onSuccess: (result, input) => {
      toast.success('Query saved.')
      setSaveDialogOpen(false)
      store.setName(input.name)
      store.setDescription(input.description)
      queryClient.invalidateQueries({ queryKey: ['audit', queryId ?? result.id] })
      if (!queryId) navigate(`/queries/${result.id}`, { replace: true })
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'Could not save the query.'),
  })

  const favoriteMutation = useMutation({
    mutationFn: () => savedQueriesApi.toggleFavorite(queryId!),
  })

  const hasColumns = definition.columns.some((c) => c.isVisible)
  const canQuery = hasColumns && !!dataSourceId
  const hasAggregates = definition.columns.some((c) => c.aggregate !== 'none')

  function requireParametersThen(action: PendingAction) {
    if (definition.parameters.length > 0) {
      setPendingAction(action)
      setParamDialogOpen(true)
      return
    }
    executeAction(action, {})
  }

  function executeAction(action: PendingAction, parameterValues: Record<string, string | null>) {
    if (!action) return
    if (action.kind === 'run') runMutation.mutate(parameterValues)
    if (action.kind === 'export') exportMutation.mutate({ format: action.format, parameterValues })
  }

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
    <div className="flex h-full min-h-0">
      <div className="w-72 shrink-0">
        <CatalogSidebar dataSourceId={dataSourceId} />
      </div>

      <div className="flex min-w-0 flex-1 flex-col">
        <div className="flex items-center gap-2 border-b border-border px-(--space-row-x) py-2">
          <Button variant="ghost" size="icon" className="size-8" onClick={() => navigate('/')}>
            <ArrowLeft className="size-4" />
          </Button>

          <div className="flex min-w-0 flex-col">
            <span className="truncate text-sm font-semibold">{name || 'Untitled query'}</span>
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

          <div className="ml-auto flex items-center gap-1.5">
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

            <Button size="sm" className="h-8 text-xs" disabled={!canQuery || runMutation.isPending} onClick={() => requireParametersThen({ kind: 'run' })}>
              {runMutation.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" />}
              Run
            </Button>

            <Separator orientation="vertical" className="h-5" />

            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery || exportMutation.isPending}
              onClick={() => requireParametersThen({ kind: 'export', format: 'csv' })}
            >
              <Download className="size-3.5" />
              CSV
            </Button>
            <Button
              variant="outline"
              size="sm"
              className="h-8 text-xs"
              disabled={!canQuery || exportMutation.isPending}
              onClick={() => requireParametersThen({ kind: 'export', format: 'xlsx' })}
            >
              <FileSpreadsheet className="size-3.5" />
              Excel
            </Button>

            <Separator orientation="vertical" className="h-5" />

            <Button size="sm" className="h-8 text-xs" disabled={!canQuery} onClick={() => setSaveDialogOpen(true)}>
              <Save className="size-3.5" />
              Save
            </Button>
          </div>
        </div>

        <Tabs value={activeTab} onValueChange={setActiveTab} className="flex min-h-0 flex-1 flex-col gap-0">
          <TabsList className="mx-(--space-row-x) mt-2 w-fit">
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
            {queryId && (
              <TabsTrigger value="history" className="gap-1 text-xs">
                <History className="size-3.5" />
                History
              </TabsTrigger>
            )}
          </TabsList>

          <TabsContent value="build" className="min-h-0 flex-1 overflow-auto p-(--space-section)">
            {!definition.source.objectName ? (
              <div className="flex h-full items-center justify-center text-center text-sm text-muted-foreground">
                Pick a table or view from the catalog on the left to start building your query.
              </div>
            ) : (
              <div className="flex flex-col gap-(--space-section)">
                <BuilderSection
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
                  title="Filters (WHERE)"
                  description="Narrow down the rows returned, before totals are calculated."
                  action={<AddFilterButton disabled={availableColumns.length === 0} />}
                >
                  <FiltersPanel availableColumns={availableColumns} />
                </BuilderSection>

                {hasAggregates && (
                  <BuilderSection
                    title="Filter the totals (HAVING)"
                    description="Narrow down groups after totals are calculated — e.g. only regions where Sum of Amount is above 10,000."
                    action={<AddHavingButton />}
                  >
                    <HavingPanel />
                  </BuilderSection>
                )}

                <BuilderSection
                  title="Sort"
                  description="Control the order rows come back in."
                  action={<AddSortControl availableColumns={availableColumns} />}
                >
                  <SortPanel />
                </BuilderSection>
              </div>
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
        initialName={name}
        initialDescription={description}
        isSaving={saveMutation.isPending}
        onConfirm={(n, d) => saveMutation.mutate({ name: n, description: d })}
      />

      <ParameterPromptDialog
        open={paramDialogOpen}
        onOpenChange={setParamDialogOpen}
        parameters={definition.parameters}
        onConfirm={(values) => {
          setParamDialogOpen(false)
          executeAction(pendingAction, values)
        }}
      />
    </div>
  )
}

function BuilderSection({
  title,
  description,
  action,
  children,
}: {
  title: string
  description: string
  action?: React.ReactNode
  children: React.ReactNode
}) {
  return (
    <section className="flex flex-col gap-2">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h2 className="text-sm font-semibold">{title}</h2>
          <p className="text-xs text-muted-foreground">{description}</p>
        </div>
        {action}
      </div>
      {children}
    </section>
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
