import { useState } from 'react'
import { AlertTriangle, ChevronLeft, ChevronRight } from 'lucide-react'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  TABLE_PAGE_SIZE,
  cellValue,
  formatCategory,
  isNumberColumn,
  pageRows,
  type TableModel,
} from '@/features/visualizations/chart-data'
import { formatValue } from '@/features/visualizations/format'
import type { ColumnDataType, ValueFormat } from '@/types'

function cellText(value: unknown, numeric: boolean, dataType: ColumnDataType | undefined, format?: ValueFormat | null): string {
  if (value === null || value === undefined) return ''
  if (typeof value === 'number' && numeric) return formatValue(value, format)
  if (dataType === 'date' || dataType === 'dateTime') return formatCategory(value)
  return String(value)
}

/** Real table semantics, cells as plain text only. The wrapper owns the scrolling so the header can stick. */
export function TableView({
  model,
  name,
  columns,
  format,
  truncated,
  rowLimit,
  fill,
}: {
  model: TableModel
  name: string
  columns?: { name: string; dataType?: ColumnDataType }[]
  format?: ValueFormat | null
  truncated?: boolean
  rowLimit?: number
  fill: boolean
}) {
  const [page, setPage] = useState(0)
  const paged = pageRows(model.rows, page, TABLE_PAGE_SIZE)
  const meta = model.columns.map((c) => ({
    name: c,
    numeric: isNumberColumn(c, columns, model.rows),
    dataType: columns?.find((rc) => rc.name === c)?.dataType,
  }))

  if (model.rows.length === 0) {
    return <p className="py-10 text-center text-sm text-muted-foreground">No data to show for these filters.</p>
  }

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
        aria-label={`${name} table`}
      >
        <table className="w-max min-w-full border-collapse text-sm">
          <caption className="sr-only">{name}</caption>
          <thead className="sticky top-0 z-10 bg-card">
            <tr className="border-b border-border">
              {meta.map((c, ci) => (
                <th
                  key={`${c.name}:${ci}`}
                  scope="col"
                  className={`map-label whitespace-nowrap px-3 py-2 font-medium text-muted-foreground ${c.numeric ? 'text-right' : 'text-left'}`}
                >
                  {c.name}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {paged.rows.map((row, i) => (
              <tr key={paged.page * TABLE_PAGE_SIZE + i} className="border-b border-border last:border-b-0">
                {meta.map((c, ci) => {
                  const value = cellValue(row, c.name)
                  const text = cellText(value, c.numeric, c.dataType, format)
                  return (
                    <td
                      key={`${c.name}:${ci}`}
                      title={c.numeric && typeof value === 'number' ? String(value) : text}
                      className={`max-w-[24rem] truncate px-3 py-1.5 ${c.numeric ? 'font-numeric text-right tabular-nums' : 'text-left'}`}
                    >
                      {text}
                    </td>
                  )
                })}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
      {paged.pageCount > 1 && (
        <div className="flex shrink-0 items-center justify-end gap-2 text-xs text-muted-foreground">
          <span aria-live="polite">
            Page {paged.page + 1} of {paged.pageCount}
          </span>
          <Button
            type="button"
            variant="outline"
            size="icon"
            className="size-7"
            aria-label="Previous page"
            disabled={paged.page === 0}
            onClick={() => setPage(paged.page - 1)}
          >
            <ChevronLeft className="size-3.5" />
          </Button>
          <Button
            type="button"
            variant="outline"
            size="icon"
            className="size-7"
            aria-label="Next page"
            disabled={paged.page >= paged.pageCount - 1}
            onClick={() => setPage(paged.page + 1)}
          >
            <ChevronRight className="size-3.5" />
          </Button>
        </div>
      )}
    </div>
  )
}
