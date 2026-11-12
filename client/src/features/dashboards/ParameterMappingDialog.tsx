import { useQuery } from '@tanstack/react-query'
import { Loader2 } from 'lucide-react'
import { useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from '@/components/ui/select'
import {
  dashboardParameterTitle,
  isCompatible,
  MAX_DASHBOARD_PARAMETERS,
  parameterFromQuery,
} from '@/features/dashboards/dashboard-params'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import { effectiveType, isBlank, parameterTitle, TYPE_LABELS } from '@/features/parameters/param-values'
import { isHub } from '@/features/hub/permissions'
import { useMe } from '@/hooks/useMe'
import { dashboardsApi, savedQueriesApi } from '@/lib/api'
import type { DashboardContext, DashboardParameterDto, DashboardTileDto, ParameterMappingDto, ParameterMappingSource, QueryParameter } from '@/types'

const NEW_PARAMETER = '__new__'

type Row = { source: ParameterMappingSource; dashboardParameter: string; fixed: string | null; label: string }

const SOURCE_LABELS: Record<ParameterMappingSource, string> = {
  default: 'Query default',
  dashboard: 'Dashboard parameter',
  tile: 'Tile input',
  fixed: 'Fixed value',
}

/** Edit mode: choose, for each parameter of a chart's query, where its value comes from. */
export function ParameterMappingDialog({
  tile,
  dashboardParameters,
  dashboardId,
  onOpenChange,
  onSave,
}: {
  tile: DashboardTileDto | null
  /** Under the hub the tile's query is read through the dashboard, because the editor may not be allowed to open it. */
  dashboardId?: string
  dashboardParameters: DashboardParameterDto[]
  onOpenChange: (open: boolean) => void
  onSave: (mappings: Record<string, ParameterMappingDto>, created: DashboardParameterDto[]) => void
}) {
  const queryId = tile?.chart?.savedQueryId ?? ''
  const { data: me, isLoading: meLoading } = useMe()
  const hubTile = isHub(me) && !!dashboardId && !!tile
  const context = useMemo(() => (hubTile && tile ? { dashboardId: dashboardId!, tileId: tile.id } : undefined), [hubTile, dashboardId, tile])
  const savedDetail = useQuery({
    queryKey: ['saved-query', queryId],
    queryFn: () => savedQueriesApi.getById(queryId),
    enabled: !!tile?.chart && !meLoading && !hubTile,
    retry: false,
    staleTime: 30_000,
  })
  const tileChart = useQuery({
    queryKey: ['tile-chart', context?.dashboardId, context?.tileId],
    queryFn: () => dashboardsApi.tileChart(context!.dashboardId, context!.tileId),
    enabled: !!tile?.chart && hubTile,
    retry: false,
    staleTime: 30_000,
  })
  const detail = hubTile
    ? {
        isLoading: tileChart.isLoading,
        isError: tileChart.isError,
        data: tileChart.data
          ? { definition: { parameters: tileChart.data.parameters.map((p) => ({ ...p, optionsQueryId: null }) as QueryParameter) } }
          : undefined,
      }
    : {
        isLoading: savedDetail.isLoading || meLoading,
        isError: savedDetail.isError,
        data: savedDetail.data ? { definition: { parameters: savedDetail.data.definition.parameters } } : undefined,
      }

  return (
    <Dialog open={tile !== null} onOpenChange={onOpenChange}>
      <DialogContent className="sm:max-w-2xl">
        {tile && (
          <>
            <DialogHeader>
              <DialogTitle>Parameters of {tile.title?.trim() || tile.chart?.visualizationName || 'this chart'}</DialogTitle>
              <DialogDescription>
                Choose where each value comes from. A dashboard parameter is shared with other charts; a tile input appears in this
                chart&apos;s header; a fixed value is hidden from viewers.
              </DialogDescription>
            </DialogHeader>
            {detail.isLoading && (
              <div className="flex items-center justify-center py-8 text-sm text-muted-foreground">
                <Loader2 className="mr-2 size-4 animate-spin" /> Loading…
              </div>
            )}
            {detail.isError && <p className="py-6 text-center text-sm text-muted-foreground">This chart&apos;s query could not be loaded.</p>}
            {detail.data &&
              (detail.data.definition.parameters.length === 0 ? (
                <p className="py-6 text-center text-sm text-muted-foreground">
                  This chart&apos;s query has no parameters. Mark a filter &ldquo;Ask the user at run time&rdquo; on the query to add one.
                </p>
              ) : (
                <Form
                  tile={tile}
                  queryParameters={detail.data.definition.parameters}
                  dashboardContext={context}
                  dashboardParameters={dashboardParameters}
                  onCancel={() => onOpenChange(false)}
                  onSave={onSave}
                />
              ))}
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

function Form({
  tile,
  queryParameters,
  dashboardContext,
  dashboardParameters,
  onCancel,
  onSave,
}: {
  tile: DashboardTileDto
  queryParameters: QueryParameter[]
  dashboardContext?: DashboardContext
  dashboardParameters: DashboardParameterDto[]
  onCancel: () => void
  onSave: (mappings: Record<string, ParameterMappingDto>, created: DashboardParameterDto[]) => void
}) {
  const [rows, setRows] = useState<Record<string, Row>>(() =>
    Object.fromEntries(
      queryParameters.map((p) => {
        const m = tile.parameterMappings?.[p.name]
        const row: Row = {
          source: m?.source ?? 'default',
          dashboardParameter: m?.dashboardParameter ?? '',
          fixed: m?.value ?? null,
          label: m?.title ?? '',
        }
        return [p.name, row]
      }),
    ),
  )

  const set = (name: string, patch: Partial<Row>) => setRows((r) => ({ ...r, [name]: { ...r[name], ...patch } }))

  const asksForNew = (p: QueryParameter) => rows[p.name].source === 'dashboard' && rows[p.name].dashboardParameter === NEW_PARAMETER
  const newCount = queryParameters.filter(asksForNew).length

  const problems = queryParameters.flatMap((p) => {
    const row = rows[p.name]
    if (row.source === 'dashboard' && row.dashboardParameter === '') return [`${parameterTitle(p)}: choose a dashboard parameter.`]
    if (row.source === 'fixed' && isBlank(row.fixed)) return [`${parameterTitle(p)}: enter the fixed value.`]
    return []
  })
  if (dashboardParameters.length + newCount > MAX_DASHBOARD_PARAMETERS) {
    problems.push(`A dashboard can have at most ${MAX_DASHBOARD_PARAMETERS} parameters.`)
  }

  function save() {
    const mappings: Record<string, ParameterMappingDto> = {}
    const created: DashboardParameterDto[] = []
    for (const p of queryParameters) {
      const row = rows[p.name]
      if (row.source === 'default') continue
      if (row.source === 'tile') {
        mappings[p.name] = { source: 'tile', title: row.label.trim() || null }
      } else if (row.source === 'fixed') {
        mappings[p.name] = { source: 'fixed', value: row.fixed }
      } else if (row.dashboardParameter === NEW_PARAMETER) {
        const made = parameterFromQuery(p, [...dashboardParameters.map((d) => d.name), ...created.map((c) => c.name)])
        created.push(made)
        mappings[p.name] = { source: 'dashboard', dashboardParameter: made.name }
      } else {
        mappings[p.name] = { source: 'dashboard', dashboardParameter: row.dashboardParameter }
      }
    }
    onSave(mappings, created)
  }

  return (
    <>
      <div className="flex max-h-[55vh] flex-col gap-4 overflow-y-auto pr-1">
        {queryParameters.map((p) => {
          const row = rows[p.name]
          const compatible = dashboardParameters.filter((d) => isCompatible(d, p))
          const sourceId = `map-source-${p.name}`
          return (
            <fieldset key={p.name} className="flex flex-col gap-2 rounded-md border border-border p-3">
              <legend className="px-1 text-sm font-medium">
                {parameterTitle(p)} <span className="font-normal text-muted-foreground">· {TYPE_LABELS[effectiveType(p)]}</span>
              </legend>
              <div className="flex flex-wrap items-end gap-3">
                <div className="flex flex-col gap-1.5">
                  <Label htmlFor={sourceId} className="text-xs text-muted-foreground">
                    Value comes from
                  </Label>
                  <Select value={row.source} onValueChange={(v) => set(p.name, { source: v as ParameterMappingSource })}>
                    <SelectTrigger id={sourceId} size="sm" className="h-8 w-48 text-xs">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      {(Object.keys(SOURCE_LABELS) as ParameterMappingSource[]).map((s) => (
                        <SelectItem key={s} value={s} className="text-xs">
                          {SOURCE_LABELS[s]}
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>

                {row.source === 'dashboard' && (
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`${sourceId}-dash`} className="text-xs text-muted-foreground">
                      Dashboard parameter
                    </Label>
                    <Select value={row.dashboardParameter} onValueChange={(v) => set(p.name, { dashboardParameter: v })}>
                      <SelectTrigger id={`${sourceId}-dash`} size="sm" className="h-8 w-56 text-xs">
                        <SelectValue placeholder="Choose…" />
                      </SelectTrigger>
                      <SelectContent>
                        {compatible.map((d) => (
                          <SelectItem key={d.name} value={d.name} className="text-xs">
                            {dashboardParameterTitle(d)}
                          </SelectItem>
                        ))}
                        <SelectItem value={NEW_PARAMETER} className="text-xs">
                          New dashboard parameter
                        </SelectItem>
                      </SelectContent>
                    </Select>
                  </div>
                )}

                {row.source === 'tile' && (
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`${sourceId}-label`} className="text-xs text-muted-foreground">
                      Label in the tile (optional)
                    </Label>
                    <Input
                      id={`${sourceId}-label`}
                      className="h-8 w-56 text-xs"
                      maxLength={200}
                      value={row.label}
                      placeholder={parameterTitle(p)}
                      onChange={(e) => set(p.name, { label: e.target.value })}
                    />
                  </div>
                )}

                {row.source === 'fixed' && (
                  <div className="flex flex-col gap-1.5">
                    <Label htmlFor={`${sourceId}-fixed`} className="text-xs text-muted-foreground">
                      Value
                    </Label>
                    <ParameterControl
                      parameter={p}
                      id={`${sourceId}-fixed`}
                      value={row.fixed}
                      onChange={(v) => set(p.name, { fixed: v })}
                      optionsSource={{ kind: 'saved', queryId: tile.chart?.savedQueryId ?? '', name: p.name, context: dashboardContext }}
                    />
                  </div>
                )}
              </div>
              {row.source === 'default' && (
                <p className="text-xs text-muted-foreground">
                  Uses the query&apos;s own default{isBlank(p.defaultValue) ? '' : ` (${p.defaultValue})`}.
                </p>
              )}
            </fieldset>
          )
        })}
      </div>
      <DialogFooter className="items-center sm:justify-between">
        <span className="text-xs text-warning" role="status">
          {problems[0]}
        </span>
        <div className="flex gap-2">
          <Button variant="outline" onClick={onCancel}>
            Cancel
          </Button>
          <Button disabled={problems.length > 0} onClick={save}>
            Done
          </Button>
        </div>
      </DialogFooter>
    </>
  )
}
