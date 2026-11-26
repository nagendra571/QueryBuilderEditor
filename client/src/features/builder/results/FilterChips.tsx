import { useRef } from 'react'
import { X } from 'lucide-react'
import { Button } from '@/components/ui/button'
import { chipText, type GridFilter } from '@/features/builder/results/grid-filters'
import type { QueryResultColumnDto } from '@/types'

export function FilterChips({
  filters,
  columns,
  hidden,
  truncated,
  loadedRows,
  onOpen,
  onRemove,
  onClearAll,
  onAddToQuery,
  addDisabledReason,
  onEmptied,
}: {
  filters: GridFilter[]
  columns: QueryResultColumnDto[]
  hidden: string[]
  truncated: boolean
  loadedRows: number
  onOpen: (column: string) => void
  onRemove: (column: string) => void
  onClearAll: () => void
  onAddToQuery?: () => void
  addDisabledReason?: string
  /** Called after the last chip goes, so focus has somewhere to land once this bar unmounts. */
  onEmptied?: () => void
}) {
  const bar = useRef<HTMLDivElement>(null)
  if (filters.length === 0) return null

  // Removing unmounts the focused element, which would drop focus to body.
  const remove = (column: string) => {
    const mains = Array.from(bar.current?.querySelectorAll<HTMLButtonElement>('[data-chip-main]') ?? [])
    const i = mains.findIndex((b) => b.dataset.chipMain === column)
    const next = mains[i + 1] ?? mains[i - 1]
    if (next) next.focus()
    else onEmptied?.()
    onRemove(column)
  }
  return (
    <div ref={bar} className="flex flex-wrap items-center gap-2 border-b border-border px-(--space-row-x) py-1.5 text-xs">
      <span className="font-numeric text-[11px] font-semibold tracking-wider text-muted-foreground uppercase">FILTERS</span>
      {filters.map((f) => {
        const dataType = columns.find((c) => c.name === f.column)?.dataType ?? 'unknown'
        const { column, text, title } = chipText(f, dataType)
        const isHidden = hidden.includes(column)
        return (
          <span
            key={column}
            className="inline-flex items-center border border-primary/30 bg-primary/10 text-primary"
          >
            <button
              type="button"
              data-chip-main={column}
              title={isHidden ? `${title} (hidden)` : title}
              className="flex max-w-xs items-center gap-1 px-2 py-1 focus-visible:outline-2 focus-visible:outline-ring"
              onClick={() => {
                if (!isHidden) onOpen(column)
              }}
              onKeyDown={(e) => {
                if (e.key === 'Delete' || e.key === 'Backspace') {
                  e.preventDefault()
                  remove(column)
                }
              }}
            >
              <span className="shrink-0 font-bold">
                {column}
                {isHidden && ' (hidden)'}
              </span>
              <span className="min-w-0 truncate">{text}</span>
            </button>
            <button
              type="button"
              aria-label={`Remove filter on ${column}`}
              className="px-1.5 py-1 hover:bg-primary/20 focus-visible:outline-2 focus-visible:outline-ring"
              onClick={() => remove(column)}
            >
              <X className="size-3" aria-hidden />
            </button>
          </span>
        )
      })}
      {truncated && (
        <span className="flex items-center gap-1.5 border border-warning/40 bg-warning/20 px-2 py-1 text-foreground">
          <span className="size-2.5 shrink-0 bg-warning" aria-hidden />
          Filtering the first {loadedRows.toLocaleString()} rows
        </span>
      )}
      <span className="ml-auto flex items-center gap-2">
        <Button variant="ghost" size="sm" className="h-7 text-xs" onClick={() => {
            onEmptied?.()
            onClearAll()
          }}>
          Clear all
        </Button>
        {onAddToQuery && (
          <Button
            size="sm"
            className="h-7 text-xs"
            onClick={onAddToQuery}
            disabled={!!addDisabledReason}
            title={addDisabledReason}
          >
            Add to query
          </Button>
        )}
      </span>
    </div>
  )
}
