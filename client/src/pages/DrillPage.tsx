import { AlertTriangle, ArrowLeft, Download, FileSpreadsheet, FileText, Link2, Loader2, Lock } from 'lucide-react'
import { useEffect, useMemo, useRef, useState } from 'react'
import { useMutation, useQuery } from '@tanstack/react-query'
import { Link, useLocation, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { toast } from 'sonner'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { ResultsTable } from '@/features/builder/ResultsTable'
import { DrillTrail, type TrailCrumb } from '@/features/drill/DrillTrail'
import { describeParameterValue, drillUrl, parseDrillSearch, stepCrumbs } from '@/features/drill/drill-nav'
import { downloadFileName } from '@/features/visualizations/download-name'
import { useDrill } from '@/hooks/useDrill'
import { useMe, zoneLabel } from '@/hooks/useMe'
import { useVisualizations } from '@/hooks/useVisualizations'
import { dashboardsApi, savedQueriesApi, visualizationsApi } from '@/lib/api'
import { ApiError } from '@/lib/api-client'
import { downloadBlob } from '@/lib/download'
import { cn } from '@/lib/utils'
import type { DownloadFormat, QueryResultDto } from '@/types'

function failureMessage(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return 'You do not have access to these rows.'
    if (error.status === 404) return 'This chart or its query could not be found.'
    return error.message
  }
  return 'The rows could not be loaded.'
}

/** The rows behind a chart mark (or a results-grid row): the same data, narrowed by the trail, drawn as the ordinary results table. */
export function DrillPage() {
  const { queryId = '', vizId } = useParams<{ queryId: string; vizId?: string }>()
  const [searchParams] = useSearchParams()
  const location = useLocation()
  const navigate = useNavigate()

  const parsed = useMemo(() => parseDrillSearch(searchParams), [searchParams])
  const trailOk = parsed.steps !== null && parsed.filters !== null
  const steps = useMemo(() => parsed.steps ?? [], [parsed.steps])
  const filters = useMemo(() => parsed.filters ?? [], [parsed.filters])

  const me = useMe()
  const zone = zoneLabel(me.data)

  const drill = useDrill({
    queryId,
    vizId,
    action: 'rows',
    steps,
    filters,
    dashboardId: parsed.dashboardId,
    tileId: parsed.tileId,
    parameterValues: parsed.params,
    enabled: trailOk && !!queryId,
  })

  // A hub Viewer cannot open the query itself, so names fall back to what the dashboard tile can read.
  const query = useQuery({ queryKey: ['saved-query', queryId], queryFn: () => savedQueriesApi.getById(queryId), enabled: !!queryId, retry: false, staleTime: 30_000 })
  const charts = useVisualizations(vizId ? queryId : null)
  const tile = useQuery({
    queryKey: ['tile-chart', parsed.dashboardId, parsed.tileId],
    queryFn: () => dashboardsApi.tileChart(parsed.dashboardId!, parsed.tileId!),
    enabled: !!parsed.dashboardId && !!parsed.tileId && query.isError,
    retry: false,
    staleTime: 30_000,
  })
  const rootLabel =
    (vizId ? charts.data?.find((v) => v.id === vizId)?.name : null) ?? tile.data?.visualization?.name ?? query.data?.name ?? (vizId ? 'Chart' : 'Query')

  const backTo = parsed.dashboardId ? `/dashboards/${parsed.dashboardId}` : `/queries/${queryId}`
  const goBack = () => (location.key !== 'default' ? navigate(-1) : navigate(backTo))

  const linkBase = { queryId, vizId, filters, dashboardId: parsed.dashboardId, tileId: parsed.tileId, params: parsed.params }
  const crumbs: TrailCrumb[] = [
    { label: rootLabel, to: backTo },
    ...stepCrumbs(steps).map((c) => ({ label: c.label, to: drillUrl({ ...linkBase, steps: steps.slice(0, c.keep) }) })),
  ]

  const result = useMemo<QueryResultDto | null>(
    () =>
      drill.data
        ? {
            columns: drill.data.columns,
            rows: drill.data.rows,
            rowCount: drill.data.rowCount,
            executionTimeMs: drill.data.executionTimeMs,
            truncated: drill.data.truncated,
            rowLimit: drill.data.rowLimit,
          }
        : null,
    [drill.data],
  )

  const exportRows = useMutation({
    mutationFn: async (format: DownloadFormat) => {
      const blob = vizId
        ? await visualizationsApi.exportDrill(queryId, vizId, {
            action: 'rows',
            steps,
            crossFilters: filters.length > 0 ? filters : undefined,
            parameterValues: parsed.params,
            dashboardId: parsed.dashboardId,
            tileId: parsed.tileId,
            format,
          })
        : await savedQueriesApi.exportDrillQuery(queryId, { steps, parameterValues: parsed.params, format })
      downloadBlob(blob, downloadFileName(`${rootLabel} rows`, format, 'rows'))
    },
    onError: (error) => toast.error(error instanceof ApiError ? error.message : 'The download failed.'),
  })

  const paramEntries = Object.entries(parsed.params).filter(([, v]) => v !== null && v !== '')

  return (
    <div className="flex h-full min-h-0 flex-col">
      <header className="flex shrink-0 flex-col gap-2 border-b border-border p-(--space-section)">
        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <Button type="button" variant="ghost" size="sm" className="h-8 gap-1.5 px-2 text-xs" onClick={goBack}>
            <ArrowLeft className="size-3.5" aria-hidden="true" />
            Back
          </Button>
          {trailOk ? <DrillTrail crumbs={crumbs} /> : <span className="text-sm font-semibold">{rootLabel}</span>}
          {zone && <ZoneChip label={zone} unrestricted={!!me.data?.zone?.unrestricted} />}
        </div>

        <div className="flex flex-wrap items-center gap-x-3 gap-y-2">
          <p role="status" aria-live="polite" className="text-sm text-muted-foreground">
            {drill.isPending && trailOk ? (
              'Loading rows…'
            ) : drill.data ? (
              <>
                {parsed.n !== null && (
                  <>
                    <span className="font-numeric font-semibold text-foreground">{parsed.n.toLocaleString()}</span> in the chart ·{' '}
                  </>
                )}
                <span className="font-numeric font-semibold text-foreground">{drill.data.rowCount.toLocaleString()}</span>{' '}
                {drill.data.rowCount === 1 ? 'row' : 'rows'}
              </>
            ) : null}
          </p>
          {paramEntries.length > 0 && (
            <ul aria-label="Parameter values" className="flex min-w-0 flex-wrap gap-1.5">
              {paramEntries.map(([name, value]) => (
                <li key={name} className="max-w-64 truncate rounded-sm border border-border bg-muted/40 px-1.5 py-0.5 text-xs" title={`${name} = ${value}`}>
                  <span className="text-muted-foreground">{name}</span> {describeParameterValue(value)}
                </li>
              ))}
            </ul>
          )}
          <div className="ml-auto flex items-center gap-1.5">
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button variant="outline" size="sm" className="h-8 text-xs" disabled={!drill.data || exportRows.isPending}>
                  {exportRows.isPending ? <Loader2 className="size-3.5 animate-spin" /> : <Download className="size-3.5" />}
                  Export
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent align="end" className="w-48">
                <DropdownMenuLabel className="text-xs font-normal text-muted-foreground">These rows</DropdownMenuLabel>
                <DropdownMenuItem onSelect={() => exportRows.mutate('csv')}>
                  <FileText className="size-3.5" />
                  CSV (.csv)
                </DropdownMenuItem>
                <DropdownMenuItem onSelect={() => exportRows.mutate('xlsx')}>
                  <FileSpreadsheet className="size-3.5" />
                  Excel (.xlsx)
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
            <CopyLink />
          </div>
        </div>
      </header>

      <div className="flex min-h-0 flex-1 flex-col">
        {!trailOk && (
          <Alert className="m-(--space-section) w-auto">
            <AlertTriangle className="size-4" />
            <AlertTitle>This link is not valid</AlertTitle>
            <AlertDescription>
              <span>Its trail could not be read. </span>
              <Link to={backTo} className="underline underline-offset-2">
                Go back to the {vizId ? 'chart' : 'query'}
              </Link>
              .
            </AlertDescription>
          </Alert>
        )}
        {trailOk && drill.isPending && (
          <div className="flex flex-1 items-center justify-center text-sm text-muted-foreground">
            <Loader2 className="mr-2 size-4 animate-spin" /> Loading rows…
          </div>
        )}
        {trailOk && drill.isError && (
          <Alert className={cn('m-(--space-section) w-auto', drill.error instanceof ApiError && drill.error.status === 403 && 'zone-hatch')}>
            {drill.error instanceof ApiError && drill.error.status === 403 ? <Lock className="size-4" /> : <AlertTriangle className="size-4" />}
            <AlertTitle>The rows could not be shown</AlertTitle>
            <AlertDescription>{failureMessage(drill.error)}</AlertDescription>
          </Alert>
        )}
        {result && (
          <div className="min-h-0 flex-1">
            <ResultsTable result={result} />
          </div>
        )}
      </div>
    </div>
  )
}

function ZoneChip({ label, unrestricted }: { label: string; unrestricted: boolean }) {
  const text = unrestricted ? 'All data' : `Your zone: ${label}`
  return (
    <span
      className="ml-auto flex shrink-0 items-center gap-1.5 rounded-sm border border-border px-1.5 py-0.5 font-numeric text-xs font-medium text-muted-foreground"
      title={`Drawn with your zone: ${label}`}
    >
      <span aria-hidden className={cn('size-2.5 rounded-[1px] border border-foreground/40 bg-zone-yellow', unrestricted && 'border-foreground')} />
      <span className="max-w-56 truncate">{text}</span>
    </span>
  )
}

/**
 * The address is copied with a hidden read-only input and the old copy command, which work on plain http where the
 * async clipboard is missing. If the browser still refuses, the input is shown selected so Ctrl+C finishes the job.
 */
function CopyLink() {
  const input = useRef<HTMLInputElement>(null)
  const [manual, setManual] = useState(false)
  const [href, setHref] = useState('')

  useEffect(() => {
    if (manual) input.current?.select()
  }, [manual])

  const copy = () => {
    setHref(window.location.href)
    // The value lands on the next render; select and copy after it.
    setTimeout(() => {
      const el = input.current
      if (!el) return
      el.focus()
      el.select()
      let ok = false
      try {
        ok = document.execCommand('copy')
      } catch {
        ok = false
      }
      if (ok) {
        toast.success('Link copied.')
        setManual(false)
        el.blur()
      } else {
        setManual(true)
      }
    }, 0)
  }

  return (
    <div className="flex items-center gap-1.5">
      <input
        ref={input}
        readOnly
        value={href}
        aria-label="Link to these rows"
        tabIndex={manual ? 0 : -1}
        className={manual ? 'h-8 w-56 rounded-md border border-input bg-background px-2 text-xs' : 'sr-only'}
        onFocus={(e) => e.currentTarget.select()}
      />
      {manual && <span className="text-xs text-muted-foreground">Press Ctrl+C</span>}
      <Button type="button" variant="outline" size="sm" className="h-8 text-xs" onClick={copy}>
        <Link2 className="size-3.5" />
        Copy link
      </Button>
    </div>
  )
}
