import { AlertTriangle, Ban, LayoutDashboard, Loader2, Lock, Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { AddToDashboardDialog } from '@/features/dashboards/AddToDashboardDialog'
import { ChartRenderer } from '@/features/visualizations/ChartRenderer'
import { typeNoun } from '@/features/visualizations/chart-data'
import { useVisualizationData } from '@/hooks/useVisualizations'
import { ApiError } from '@/lib/api-client'
import type { QueryParameter, VisualizationDto } from '@/types'

type PanelState = 'ok' | 'stale' | 'disabled' | 'noAccess' | 'tooMany' | 'error'

function classify(error: unknown): { state: PanelState; message: string } {
  if (error instanceof ApiError) {
    if (error.status === 403) return { state: 'noAccess', message: 'You no longer have access to this chart.' }
    if (error.status === 409 && error.title === 'Query disabled') return { state: 'disabled', message: 'This query is disabled.' }
    if (error.status === 409) return { state: 'tooMany', message: error.message }
    if (error.status === 400 && error.message.startsWith('This chart needs updating')) {
      return { state: 'stale', message: error.message }
    }
    return { state: 'error', message: error.message }
  }
  return { state: 'error', message: 'The chart could not be drawn.' }
}

export function ChartPanel({
  queryId,
  visualization,
  parameters,
  parameterValues,
  isDisabled,
  canEdit,
  onEdit,
  onDelete,
}: {
  queryId: string
  visualization: VisualizationDto
  /** Parameters of the *saved* query — the chart is always drawn from the saved definition. */
  parameters: QueryParameter[]
  /** Values last applied in the parameter bar (already limited to this query's parameters); null before any Apply. */
  parameterValues: Record<string, string | null> | null
  isDisabled: boolean
  canEdit: boolean
  onEdit: () => void
  onDelete: () => void
}) {
  const [addToDashboardOpen, setAddToDashboardOpen] = useState(false)

  const needsPrompt = parameters.some((p) => p.isRequired && !p.defaultValue)
  const waitingForParameters = needsPrompt && parameterValues === null
  // A disabled query is refused server-side; skip the doomed request and say why straight away.
  const enabled = !waitingForParameters && !isDisabled

  const { data, error, isFetching } = useVisualizationData(queryId, visualization.id, parameterValues ?? {}, enabled)

  const failure = error ? classify(error) : null

  return (
    <div className="flex min-w-0 flex-col gap-3 p-(--space-section)">
      <div className="flex items-center gap-2">
        <div className="flex min-w-0 flex-col">
          <h2 className="truncate text-sm font-semibold">{visualization.name}</h2>
          <span className="text-[11px] text-muted-foreground">{typeNoun(visualization.type)}</span>
        </div>
        <div className="ml-auto flex items-center gap-1.5">
          <Button variant="outline" size="sm" className="h-8 text-xs" onClick={() => setAddToDashboardOpen(true)}>
            <LayoutDashboard className="size-3.5" />
            Add to dashboard
          </Button>
          {canEdit && (
            <>
              <Button variant="outline" size="sm" className="h-8 text-xs" onClick={onEdit}>
                <Pencil className="size-3.5" />
                Edit chart
              </Button>
              <Button
                variant="outline"
                size="icon"
                className="size-8"
                aria-label={`Delete chart ${visualization.name}`}
                onClick={onDelete}
              >
                <Trash2 className="size-3.5" />
              </Button>
            </>
          )}
        </div>
      </div>

      {isDisabled && (
        <PanelNotice icon={<Ban className="size-4" />} title="This query is disabled" message="Charts are not drawn while the query is disabled." />
      )}

      {waitingForParameters && !isDisabled && (
        <div className="flex flex-col items-center gap-1 py-10 text-sm text-muted-foreground">
          This chart needs parameter values before it can be drawn.
          <span className="text-xs">Fill in the parameters above, then press Apply.</span>
        </div>
      )}

      {enabled && isFetching && !data && (
        <div className="flex items-center justify-center py-10 text-sm text-muted-foreground">
          <Loader2 className="mr-2 size-4 animate-spin" /> Drawing chart…
        </div>
      )}

      {enabled && failure?.state === 'noAccess' && (
        <PanelNotice icon={<Lock className="size-4" />} title="No access" message={failure.message} />
      )}
      {enabled && failure?.state === 'disabled' && (
        <PanelNotice icon={<Ban className="size-4" />} title="This query is disabled" message="Charts are not drawn while the query is disabled." />
      )}
      {enabled && failure?.state === 'tooMany' && (
        <PanelNotice icon={<AlertTriangle className="size-4" />} title="Too many rows to chart" message={failure.message} />
      )}
      {enabled && failure?.state === 'stale' && (
        <PanelNotice
          icon={<AlertTriangle className="size-4" />}
          title="This chart needs updating"
          message={failure.message.replace(/^This chart needs updating:\s*/, '')}
          action={
            canEdit ? (
              <Button size="sm" variant="outline" onClick={onEdit}>
                Edit chart
              </Button>
            ) : undefined
          }
        />
      )}
      {enabled && failure?.state === 'error' && (
        <PanelNotice icon={<AlertTriangle className="size-4" />} title="The chart could not be drawn" message={failure.message} />
      )}

      {enabled && !failure && data && (
        <ChartRenderer type={visualization.type} options={visualization.options} rows={data.rows}
          name={visualization.name}
          columns={data.columns}
          truncated={data.truncated}
          rowLimit={data.rowLimit}
        />
      )}

      <AddToDashboardDialog
        open={addToDashboardOpen}
        onOpenChange={setAddToDashboardOpen}
        visualization={visualization}
      />
    </div>
  )
}

function PanelNotice({
  icon,
  title,
  message,
  action,
}: {
  icon: React.ReactNode
  title: string
  message: string
  action?: React.ReactNode
}) {
  return (
    <Alert>
      {icon}
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription className="flex flex-col items-start gap-2">
        <span>{message}</span>
        {action}
      </AlertDescription>
    </Alert>
  )
}
