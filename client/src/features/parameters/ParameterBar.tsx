import { AlertCircle, Loader2, Play, Settings2 } from 'lucide-react'
import { useId } from 'react'
import { Button } from '@/components/ui/button'
import { Label } from '@/components/ui/label'
import { ParameterControl } from '@/features/parameters/controls/ParameterControl'
import {
  missingRequired,
  parameterTitle,
  valuesEqual,
  type ParameterValues,
} from '@/features/parameters/param-values'
import type { OptionsSource } from '@/hooks/useParameterOptions'
import type { QueryParameter } from '@/types'

/**
 * The strip above the results: one labelled control per parameter, and an explicit Apply. Nothing
 * runs while a value is being edited — the grid and charts only change on Apply, and an
 * "unapplied" hint says so when the bar and the results disagree.
 */
export function ParameterBar({
  parameters,
  values,
  appliedValues,
  onChange,
  onApply,
  isApplying,
  applyDisabledReason,
  canEdit,
  onOpenSettings,
  onSaveDefaults,
  optionsSourceFor,
}: {
  parameters: QueryParameter[]
  values: ParameterValues
  /** What the grid and charts currently reflect; null before anything has been applied. */
  appliedValues: ParameterValues | null
  onChange: (name: string, value: string | null) => void
  onApply: () => void
  isApplying: boolean
  /** Why Apply is unavailable for a reason other than missing values (e.g. the query is disabled). */
  applyDisabledReason?: string
  /** Editors can open a parameter's settings and save the current values as its defaults. */
  canEdit: boolean
  onOpenSettings: (name: string) => void
  onSaveDefaults: () => void
  optionsSourceFor: (parameter: QueryParameter) => OptionsSource | null
}) {
  const baseId = useId()
  const missing = missingRequired(parameters, values)
  const unapplied = appliedValues !== null && !valuesEqual(parameters, values, appliedValues)
  const differsFromDefaults = parameters.some((p) => (values[p.name] ?? '') !== (p.defaultValue ?? ''))

  const blockedReason =
    applyDisabledReason ?? (missing.length > 0 ? `Enter ${missing.map(parameterTitle).join(', ')} to apply.` : undefined)

  return (
    <form
      aria-label="Query parameters"
      className="flex flex-wrap items-end gap-x-4 gap-y-2 border-b border-border bg-card px-(--space-row-x) py-2.5"
      onSubmit={(e) => {
        e.preventDefault()
        if (!blockedReason && !isApplying) onApply()
      }}
    >
      {parameters.map((parameter) => {
        const id = `${baseId}-${parameter.name}`
        const title = parameterTitle(parameter)
        const isMissing = missing.some((m) => m.name === parameter.name)
        return (
          <div key={parameter.name} className="flex min-w-0 flex-col gap-1">
            <div className="flex items-center gap-1">
              <Label htmlFor={id} className="map-label text-muted-foreground">
                {title}
                {parameter.isRequired && (
                  <span aria-hidden className="text-destructive">
                    {' '}
                    *
                  </span>
                )}
              </Label>
              {canEdit && (
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="size-5 text-muted-foreground"
                  aria-label={`Settings for ${title}`}
                  onClick={() => onOpenSettings(parameter.name)}
                >
                  <Settings2 className="size-3" />
                </Button>
              )}
            </div>
            <div className={isMissing ? 'rounded-md ring-1 ring-warning/60' : undefined}>
              <ParameterControl
                parameter={parameter}
                id={id}
                value={values[parameter.name] ?? null}
                onChange={(v) => onChange(parameter.name, v)}
                optionsSource={optionsSourceFor(parameter)}
              />
            </div>
          </div>
        )
      })}

      <div className="flex flex-wrap items-center gap-2 pb-px">
        <Button type="submit" size="sm" className="h-7 text-xs" disabled={!!blockedReason || isApplying}>
          {isApplying ? <Loader2 className="size-3.5 animate-spin" /> : <Play className="size-3.5" />}
          Apply
        </Button>
        {canEdit && (
          <Button
            type="button"
            variant="outline"
            size="sm"
            className="h-7 text-xs"
            disabled={!differsFromDefaults}
            title="Make the current values this query's defaults (then Save the query)."
            onClick={onSaveDefaults}
          >
            Save as defaults
          </Button>
        )}
        {blockedReason ? (
          <span className="inline-flex items-center gap-1 text-xs text-warning" role="status">
            <AlertCircle className="size-3.5 shrink-0" />
            {blockedReason}
          </span>
        ) : (
          unapplied && (
            <span className="text-xs text-query" role="status">
              Unapplied changes — press Apply to update the results.
            </span>
          )
        )}
      </div>
    </form>
  )
}
