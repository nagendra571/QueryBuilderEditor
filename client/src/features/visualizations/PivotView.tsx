import type { CSSProperties } from 'react'
import { formatValue } from '@/features/visualizations/format'
import { MAX_SHADE_PERCENT, shadeFraction, type PivotCell, type PivotModel } from '@/features/visualizations/pivot-model'
import type { ValueFormat } from '@/types'

const NOT_COMBINABLE = '—'

function cellText(cell: PivotCell, format?: ValueFormat | null): string {
  if (cell.notCombinable) return NOT_COMBINABLE
  return cell.value === null ? '' : formatValue(cell.value, format)
}

/** For each row-header level, the span of the run it starts (0 where a run above already covers it). */
function rowSpans(headers: string[][]): number[][] {
  const levels = headers[0]?.length ?? 0
  const spans = headers.map(() => Array<number>(levels).fill(1))
  for (let level = 0; level < levels; level++) {
    let start = 0
    for (let i = 1; i <= headers.length; i++) {
      const sameRun =
        i < headers.length && headers[i].slice(0, level + 1).every((part, k) => part === headers[start][k])
      if (sameRun) continue
      spans[start][level] = i - start
      for (let j = start + 1; j < i; j++) spans[j][level] = 0
      start = i
    }
  }
  return spans
}

const HEAD_DATA = 'whitespace-nowrap px-3 py-2 text-xs font-medium text-muted-foreground'
const HEAD = 'map-label whitespace-nowrap px-3 py-2 font-medium text-muted-foreground'
const NUM = 'font-numeric px-3 py-1.5 text-right tabular-nums whitespace-nowrap'

/** Real table semantics, cells as plain text only. The wrapper owns the scrolling so the header can stick. */
export function PivotView({
  model,
  name,
  rowNames,
  columnName,
  format,
  shade,
  fill,
}: {
  model: PivotModel
  name: string
  rowNames: string[]
  columnName?: string | null
  format?: ValueFormat | null
  shade: boolean
  fill: boolean
}) {
  if (model.rowHeaders.length === 0) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No data to show for these filters.</p>
  }

  const valueCount = model.values.length
  const hasColumn = !!columnName
  // With a column set the measure names get their own header row, so a lone value is still labelled.
  const grouped = hasColumn
  const spans = rowSpans(model.rowHeaders)
  const headRowSpan = grouped ? 2 : 1
  const showTotalColumn = model.columnTotals !== null
  const showTotalRow = model.rowTotals !== null

  const shadeStyle = (cell: PivotCell, v: number): CSSProperties | undefined => {
    if (!shade) return undefined
    const fraction = shadeFraction(cell, model.valueRanges[v])
    if (fraction === null) return undefined
    const percent = Math.round(fraction * MAX_SHADE_PERCENT)
    return { backgroundColor: `color-mix(in srgb, var(--chart-1) ${percent}%, var(--background))` }
  }

  const valueCells = (cells: PivotCell[], keyPrefix: string, shaded: boolean, strong = false) =>
    cells.map((cell, v) => (
      <td
        key={`${keyPrefix}-${v}`}
        className={`${NUM} ${strong ? 'font-semibold' : ''}`}
        style={shaded ? shadeStyle(cell, v) : undefined}
      >
        {cellText(cell, format)}
      </td>
    ))

  return (
    <div className={`flex min-w-0 flex-col gap-2 ${fill ? 'h-full' : ''}`}>
      <div
        className={`min-w-0 overflow-auto rounded-md border border-border ${fill ? 'min-h-[96px] flex-1 shrink-0' : 'max-h-[420px]'}`}
        tabIndex={0}
        aria-label={`${name} pivot table`}
      >
        <table className="w-max min-w-full border-collapse text-sm">
          <caption className="sr-only">{name}</caption>
          {grouped && (
            <>
              <colgroup span={Math.max(1, rowNames.length)} />
              {[...model.columnHeaders, ...(showTotalColumn ? ['Total'] : [])].map((_, ci) => (
                <colgroup key={`cg-${ci}`} span={valueCount} />
              ))}
            </>
          )}
          <thead className="sticky top-0 z-10 bg-card">
            <tr className="border-b border-border">
              {rowNames.map((n, i) => (
                <th key={`rh-${i}`} scope="col" rowSpan={headRowSpan} className={`${HEAD} text-left`}>
                  {n}
                </th>
              ))}
              {hasColumn
                ? model.columnHeaders.map((h, ci) => (
                    <th
                      key={`ch-${ci}`}
                      scope="colgroup"
                      colSpan={valueCount}
                      className={`${HEAD_DATA} text-center`}
                    >
                      {h}
                    </th>
                  ))
                : model.values.map((v, vi) => (
                    <th key={`vh-${vi}`} scope="col" className={`${HEAD} text-right`}>
                      {v}
                    </th>
                  ))}
              {showTotalColumn && (
                <th scope={grouped ? 'colgroup' : 'col'} colSpan={valueCount} rowSpan={grouped ? 1 : headRowSpan} className={`${HEAD} text-right`}>
                  Total
                </th>
              )}
            </tr>
            {grouped && (
              <tr className="border-b border-border">
                {[...model.columnHeaders, ...(showTotalColumn ? ['Total'] : [])].flatMap((_, ci) =>
                  model.values.map((v, vi) => (
                    <th key={`sv-${ci}-${vi}`} scope="col" className={`${HEAD} text-right`}>
                      {v}
                    </th>
                  )),
                )}
              </tr>
            )}
          </thead>
          <tbody>
            {model.rowHeaders.map((header, ri) => (
              <tr key={`r-${ri}`} className="border-b border-border last:border-b-0">
                {header.map((part, level) =>
                  spans[ri][level] === 0 ? null : (
                    <th
                      key={`h-${ri}-${level}`}
                      scope="row"
                      rowSpan={spans[ri][level]}
                      title={part}
                      className="max-w-[16rem] truncate px-3 py-1.5 text-left align-top font-medium"
                    >
                      {part}
                    </th>
                  ),
                )}
                {model.cells[ri].map((cells, ci) => valueCells(cells, `c-${ri}-${ci}`, true))}
                {model.columnTotals && valueCells(model.columnTotals[ri], `t-${ri}`, false, true)}
              </tr>
            ))}
            {showTotalRow && (
              <tr className="border-t border-border bg-muted/40">
                <th scope="row" colSpan={Math.max(1, rowNames.length)} className="px-3 py-1.5 text-left font-semibold">
                  Total
                </th>
                {model.rowTotals!.map((cells, ci) => valueCells(cells, `tr-${ci}`, false, true))}
                {model.grandTotals && valueCells(model.grandTotals, 'g', false, true)}
              </tr>
            )}
          </tbody>
        </table>
      </div>
      {model.skipped > 0 && <p className="shrink-0 text-xs text-muted-foreground">{model.skipped} value(s) could not be plotted.</p>}
      {model.hasNotCombinable && (
        <p className="shrink-0 text-xs text-muted-foreground">Averages and distinct counts can't be added together, so these show {NOT_COMBINABLE}.</p>
      )}
    </div>
  )
}
