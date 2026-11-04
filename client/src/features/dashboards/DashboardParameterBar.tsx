import { Play, Settings2 } from 'lucide-react'
import { useId } from 'react'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { dashboardParameterTitle, toQueryParameter } from '@/features/dashboards/dashboard-params'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import { valuesEqual, type ParameterValues } from '@/features/parameters/param-values'
import type { DashboardParameterDto } from '@/types'

/**
 * The strip above a dashboard's tiles. Same contract as a query's parameter bar: nothing is drawn
 * again while a value is being edited; Apply redraws every tile that uses it.
 */
export function DashboardParameterBar({
  parameters,
  values,
  appliedValues,
  onChange,
  onApply,
  onReset,
  editing,
  onManage,
}: {
  parameters: DashboardParameterDto[]
  values: ParameterValues
  appliedValues: ParameterValues
  onChange: (name: string, value: string | null) => void
  onApply: () => void
  /** Back to the saved defaults. */
  onReset: () => void
  editing: boolean
  onManage: (name: string) => void
}) {
  const baseId = useId()
  const queryParameters = parameters.map(toQueryParameter)
  const unapplied = !valuesEqual(queryParameters, values, appliedValues)
  const differsFromDefaults = queryParameters.some((p) => (values[p.name] ?? '') !== (p.defaultValue ?? ''))

  if (parameters.length === 0) return null

  return (
    <form
      aria-label="Dashboard parameters"
      className="flex flex-wrap items-end gap-x-4 gap-y-2 rounded-md border border-border bg-card px-3 py-2.5"
      onSubmit={(e) => {
        e.preventDefault()
        onApply()
      }}
    >
      {parameters.map((parameter, index) => {
        const id = `${baseId}-${parameter.name}`
        const title = dashboardParameterTitle(parameter)
        return (
          <div key={parameter.name} className="flex min-w-0 flex-col gap-1">
            <div className="flex items-center gap-1">
              <Label htmlFor={id} className="map-label text-muted-foreground">
                {title}
              </Label>
              {editing && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="size-5 text-muted-foreground"
                  aria-label={`Settings for dashboard parameter ${title}`}
                  onClick={() => onManage(parameter.name)}
                >
                  <Settings2 className="size-3" />
                </Button>
              )}
            </div>
            <ParameterControl
              parameter={queryParameters[index]}
              id={id}
              value={values[parameter.name] ?? null}
              onChange={(v) => onChange(parameter.name, v)}
              optionsSource={parameter.type === 'queryList' ? { kind: 'draft', parameter: queryParameters[index] } : null}
            />
          </div>
        )
      })}

      <div className="flex flex-wrap items-center gap-2 pb-px">
        <Button type="submit" size="sm" className="h-7 text-xs" disabled={!unapplied}>
          <Play className="size-3.5" />
          Apply
        </Button>
        <Button type="button" variant="ghost" size="sm" className="h-7 text-xs" disabled={!differsFromDefaults} onClick={onReset}>
          Reset
        </Button>
        {unapplied && (
          <span className="text-xs text-query" role="status">
            Unapplied changes — press Apply to redraw the charts.
          </span>
        )}
      </div>
    </form>
  )
}

