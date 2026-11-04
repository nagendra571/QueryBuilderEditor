import { AlertTriangle } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { formatValue } from '@/features/visualizations/format'
import type { FunnelModel } from '@/features/visualizations/story-models'
import type { ValueFormat } from '@/types'

const PERCENT: ValueFormat = { style: 'number', decimals: 1 }

const percentText = (v: number | null) => (v === null ? '—' : `${formatValue(v, PERCENT)}%`)

/** A real table so each step's number is text; the in-cell bar only adds shape and is hidden from assistive tech. */
export function FunnelView({
  model,
  name,
  format,
  truncated,
  rowLimit,
  fill,
}: {
  model: FunnelModel
  name: string
  format?: ValueFormat | null
  truncated?: boolean
  rowLimit?: number
  fill: boolean
}) {
  if (model.steps.length === 0) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No data to show for these filters.</p>
  }
  const max = model.steps.reduce((m, s) => (s.value !== null && s.value > m ? s.value : m), 0)

  return (
    <div className={`flex min-w-0 flex-col gap-2 ${fill ? 'h-full' : ''}`}>
      {truncated && (
        <Alert className="w-auto shrink-0 border-warning/40 py-2 text-warning">
          <AlertTriangle className="size-4" />
          <AlertDescription className="text-warning">
            {rowLimit
              ? `This query returns more than ${rowLimit.toLocaleString()} records. Only the first ${rowLimit.toLocaleString()} are shown. Add filters to narrow down the results.`
              : 'This query returns more records than can be shown. Only the first rows are shown. Add filters to narrow down the results.'}
          </AlertDescription>
        </Alert>
      )}
      <div
        className={`min-w-0 overflow-auto rounded-md border border-border ${fill ? 'min-h-[96px] flex-1 shrink-0' : 'max-h-[420px]'}`}
        tabIndex={0}
        aria-label={`${name} funnel`}
      >
        <table className="w-full min-w-[22rem] border-collapse text-sm">
          <caption className="sr-only">{name}</caption>
          <thead className="sticky top-0 z-10 bg-card">
            <tr className="border-b border-border">
              <th scope="col" className="map-label whitespace-nowrap px-3 py-2 text-left font-medium text-muted-foreground">
                Step
              </th>
              <th scope="col" className="map-label whitespace-nowrap px-3 py-2 text-left font-medium text-muted-foreground">
                Value
              </th>
              <th scope="col" className="map-label whitespace-nowrap px-3 py-2 text-right font-medium text-muted-foreground">
                % of first
              </th>
              <th scope="col" className="map-label whitespace-nowrap px-3 py-2 text-right font-medium text-muted-foreground">
                % of previous
              </th>
            </tr>
          </thead>
          <tbody>
            {model.steps.map((s, i) => {
              const width = s.value !== null && max > 0 ? Math.max(0, (s.value / max) * 100) : 0
              const text = s.value === null ? '' : formatValue(s.value, format)
              return (
                <tr key={i} className="border-b border-border last:border-b-0">
                  <td title={s.name} className="max-w-[12rem] truncate px-3 py-1.5 text-left">
                    {s.name}
                  </td>
                  <td className="px-3 py-1.5">
                    <div className="flex min-w-0 items-center gap-2">
                      <div className="h-3 min-w-0 flex-1 rounded-sm bg-muted" aria-hidden="true">
                        <div className="h-full rounded-sm bg-chart-1" style={{ width: `${width}%` }} />
                      </div>
                      <span className="font-numeric shrink-0 tabular-nums">{text}</span>
                    </div>
                  </td>
                  <td className="font-numeric px-3 py-1.5 text-right tabular-nums">{percentText(s.pctOfFirst)}</td>
                  <td className="font-numeric px-3 py-1.5 text-right tabular-nums">{percentText(s.pctOfPrevious)}</td>
                </tr>
              )
            })}
          </tbody>
        </table>
      </div>
      {model.skipped > 0 && <p className="text-xs text-muted-foreground">{model.skipped} value(s) could not be plotted.</p>}
    </div>
  )
}
