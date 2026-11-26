import { useMemo, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import { Input } from '@/components/ui/input'
import type { QueryResultColumnDto } from '@/types'
import { filterFromTicks, initialTicks } from './filter-input.ts'
import { valueOptions, type FilterValue, type GridFilter } from './grid-filters.ts'

const CAP = 1000

export function ValuesFilter({
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
  const { options, truncated } = useMemo(() => valueOptions(rows, column, CAP), [rows, column])
  const [ticked, setTicked] = useState(() => initialTicks(options, filter))
  const [search, setSearch] = useState('')
  const [cleared, setCleared] = useState(false)

  const needle = search.trim().toLowerCase()
  const visible = useMemo(
    () => (needle ? options.filter((o) => o.label.toLowerCase().includes(needle)) : options),
    [options, needle],
  )

  function toggle(value: FilterValue, on: boolean) {
    setTicked((prev) => {
      const next = new Set(prev)
      if (on) next.add(value)
      else next.delete(value)
      return next
    })
  }
  function setVisible(on: boolean) {
    setTicked((prev) => {
      const next = new Set(prev)
      for (const o of visible) {
        if (on) next.add(o.value)
        else next.delete(o.value)
      }
      return next
    })
  }

  const noneTicked = options.every((o) => !ticked.has(o.value))

  return (
    <>
      <Input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="Search values"
        aria-label={`Search values in ${column.name}`}
        className="h-8"
      />
      <div className="flex gap-3 text-xs">
        <button type="button" className="text-primary hover:underline focus-visible:underline" onClick={() => setVisible(true)}>
          Select all
        </button>
        <button type="button" className="text-primary hover:underline focus-visible:underline" onClick={() => {
            setCleared(true)
            setVisible(false)
          }}>
          Clear
        </button>
      </div>
      <div role="group" aria-label={`Values in ${column.name}`} className="max-h-64 overflow-y-auto border-y border-border">
        {visible.length === 0 && <p className="px-1 py-2 text-xs text-muted-foreground">No values match.</p>}
        {visible.map((o) => (
          <label key={String(o.value)} className="flex cursor-pointer items-center gap-2 px-1 py-1 text-sm hover:bg-muted">
            <Checkbox checked={ticked.has(o.value)} onCheckedChange={(c) => toggle(o.value, c === true)} />
            <span className="min-w-0 flex-1 truncate" title={o.label}>
              {o.label}
            </span>
            <span className="font-numeric text-xs tabular-nums text-muted-foreground">{o.count.toLocaleString()}</span>
          </label>
        ))}
      </div>
      {truncated && (
        <p className="text-xs text-muted-foreground">Showing the first {CAP.toLocaleString()} values — search to find others</p>
      )}
      <div className="flex justify-end gap-2">
        <Button type="button" variant="outline" size="sm" onClick={onCancel}>
          Cancel
        </Button>
        <Button type="button" size="sm" disabled={noneTicked} onClick={() => onApply(filterFromTicks(column.name, options, ticked, filter, truncated, cleared))}>
          Apply
        </Button>
      </div>
    </>
  )
}
