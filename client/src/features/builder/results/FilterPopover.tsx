import { useMemo } from 'react'
import { PopoverContent } from '@/components/ui/popover'
import type { QueryResultColumnDto } from '@/types'
import { filterUi, rowsPassing, type GridFilter } from './grid-filters.ts'
import { RangeFilter } from './RangeFilter'
import { TypeBadge } from './TypeBadge'
import { ValuesFilter } from './ValuesFilter'

// Mounted only while open, so the memoised rows are computed once per opening.
export function FilterPopover({
  column,
  rows,
  filters,
  columns,
  onApply,
  onClose,
}: {
  column: QueryResultColumnDto
  rows: Record<string, unknown>[]
  filters: GridFilter[]
  /** Every result column, hidden ones included, so each filter's type is known. */
  columns: QueryResultColumnDto[]
  /** null clears the column's filter. */
  onApply: (next: GridFilter | null) => void
  onClose: () => void
}) {
  const others = useMemo(() => rowsPassing(rows, filters, columns, column.name), [rows, filters, columns, column.name])
  const filter = filters.find((f) => f.column === column.name)
  const Body = filterUi(column.dataType) === 'range' ? RangeFilter : ValuesFilter

  return (
    <PopoverContent
      align="start"
      className="w-80"
      aria-label={`Filter ${column.name}`}
      // The portal's React events would otherwise bubble into the grid's key and pointer handlers.
      onKeyDown={(e) => e.stopPropagation()}
      onPointerDown={(e) => e.stopPropagation()}
    >
      <div className="flex items-center gap-2">
        <TypeBadge dataType={column.dataType} />
        <span className="min-w-0 flex-1 truncate font-medium" title={column.name}>
          {column.name}
        </span>
      </div>
      <Body
        column={column}
        rows={others}
        filter={filter}
        onApply={(next) => {
          onApply(next)
          onClose()
        }}
        onCancel={onClose}
      />
    </PopoverContent>
  )
}
