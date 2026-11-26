import { useId, useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import { toNumber } from '../../visualizations/chart-data.ts'
import type { QueryResultColumnDto } from '@/types'
import { histogram, parseDateValue, profileKind } from './column-profile.ts'
import { boundInputText, buildRangeFilter, canApplyRange, rangeProblem } from './filter-input.ts'
import type { GridFilter } from './grid-filters.ts'
import { Histogram } from './Histogram'

export function RangeFilter({
  column,
  rows,
  filter,
  onApply,
  onCancel,
}: {
  column: QueryResultColumnDto
  /** Rows that pass every other column's filter. */
  rows: Record<string, unknown>[]
  filter: GridFilter | undefined
  onApply: (next: GridFilter | null) => void
  onCancel: () => void
}) {
  const current = filter?.kind === 'range' ? filter : undefined
  const isDate = profileKind(column.dataType) === 'date'
  const [from, setFrom] = useState(boundInputText(current?.min ?? null, column.dataType))
  const [to, setTo] = useState(boundInputText(current?.max ?? null, column.dataType))
  const [includeEmpty, setIncludeEmpty] = useState(current?.includeEmpty ?? false)
  const ids = useId()

  const bins = useMemo(() => {
    const nums: number[] = []
    for (const r of rows) {
      const raw = r[column.name]
      const n = isDate ? parseDateValue(raw) : toNumber(raw)
      if (typeof n === 'number' && Number.isFinite(n)) nums.push(n)
    }
    return histogram(nums, 20)
  }, [rows, column.name, isDate])

  const problem = rangeProblem(from, to, column.dataType)
  const message =
    problem === 'invalid' ? (isDate ? 'Enter a date' : 'Enter a number') : problem === 'order' ? 'From must be before To' : null
  const formatBound = (n: number) => (isDate ? new Date(n).toISOString().slice(0, 10) : String(Math.round(n * 100) / 100))
  const inputProps = isDate ? ({ type: 'date' } as const) : ({ inputMode: 'decimal' } as const)

  return (
    <>
      {bins.length > 0 && <Histogram bins={bins} label={`Distribution of ${column.name}`} formatBound={formatBound} />}
      <div className="grid grid-cols-2 gap-2">
        <div className="flex flex-col gap-1">
          <label htmlFor={`${ids}-from`} className="text-xs text-muted-foreground">
            From
          </label>
          <Input id={`${ids}-from`} value={from} onChange={(e) => setFrom(e.target.value)} aria-invalid={problem !== null} {...inputProps} />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${ids}-to`} className="text-xs text-muted-foreground">
            To
          </label>
          <Input id={`${ids}-to`} value={to} onChange={(e) => setTo(e.target.value)} aria-invalid={problem !== null} {...inputProps} />
        </div>
      </div>
      {message && (
        <p role="alert" className="text-xs text-destructive">
          {message}
        </p>
      )}
      <label className="flex cursor-pointer items-center gap-2 text-sm">
        <Checkbox checked={includeEmpty} onCheckedChange={(c) => setIncludeEmpty(c === true)} />
        Include empty values
      </label>
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" size="sm" onClick={() => onApply(null)}>
          Clear
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
        <Button
          type="button"
          size="sm"
          disabled={problem !== null || !canApplyRange(from, to, includeEmpty)}
          onClick={() => onApply(buildRangeFilter(column.name, from, to, includeEmpty, column.dataType))}
        >
          Apply
        </Button>
      </div>
    </>
  )
}
