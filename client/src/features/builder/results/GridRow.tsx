import type { Row } from '@tanstack/react-table'
import { ListTree } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto } from '@/types'
import { GridCell } from './GridCell'
import { ROW_NUM_WIDTH, type ColumnLayout } from './grid-layout.ts'
import type { RowData } from './grid-table.ts'
import { normalize, type CellRange } from './selection.ts'

// Sticky cells paint over scrolled ones, so their tints are mixed into an opaque colour.
const SELECTED = 'bg-[color-mix(in_srgb,var(--primary)_10%,var(--background))]'

export function GridRow({
  row,
  index,
  start,
  height,
  wrap,
  width,
  columns,
  layouts,
  range,
  onPointerDown,
  onPointerEnter,
  onAction,
  actionLabel,
  measureRef,
}: {
  row: Row<RowData>
  index: number
  start: number
  height: number
  wrap: boolean
  width: number
  columns: QueryResultColumnDto[]
  layouts: ColumnLayout[]
  range: CellRange | null
  onPointerDown: (row: number, col: number, e: React.PointerEvent) => void
  onPointerEnter: (row: number, col: number) => void
  onAction?: (row: RowData) => void
  actionLabel?: string
  measureRef?: (el: HTMLElement | null) => void
}) {
  const n = range ? normalize(range) : null
  const inRows = !!n && index >= n.top && index <= n.bottom
  const focusRow = range?.focus.row === index

  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      data-index={index}
      ref={measureRef}
      className="group absolute top-0 left-0 flex"
      style={{
        width,
        transform: `translateY(${start}px)`,
        ...(wrap ? { minHeight: height } : { height }),
      }}
    >
      <div
        role="rowheader"
        aria-colindex={1}
        className="sticky left-0 z-10 flex shrink-0 items-center justify-end self-stretch border-r border-b border-border/60 bg-background px-2.5 font-numeric text-xs text-muted-foreground tabular-nums group-hover:bg-muted"
        style={{ width: ROW_NUM_WIDTH }}
      >
        <span className={cn(onAction && 'group-hover:opacity-0', onAction && focusRow && 'opacity-0')}>{index + 1}</span>
        {onAction && (
          <button
            type="button"
            aria-label={actionLabel}
            title={actionLabel}
            onClick={(e) => {
              e.stopPropagation()
              onAction(row.original)
            }}
            className={cn(
              'absolute inset-0 flex items-center justify-center text-foreground opacity-0 group-hover:opacity-100 focus-visible:opacity-100',
              focusRow && 'opacity-100',
            )}
          >
            <ListTree className="size-3.5" />
          </button>
        )}
      </div>
      {columns.map((col, ci) => {
        const l = layouts[ci]
        const pinned = l.group !== 'centre'
        const selected = inRows && !!n && ci >= n.left && ci <= n.right
        const focused = focusRow && range?.focus.col === ci
        const isNumber = col.dataType === 'number'
        return (
          <div
            key={col.name}
            role="gridcell"
            aria-colindex={ci + 2}
            aria-selected={selected}
            onPointerDown={(e) => onPointerDown(index, ci, e)}
            onPointerEnter={() => onPointerEnter(index, ci)}
            className={cn(
              'flex shrink-0 items-center self-stretch overflow-hidden border-r border-b border-border/60 px-2.5 text-(length:--text-body)',
              isNumber && 'justify-end text-right font-numeric tabular-nums',
              wrap && 'py-1.5',
              pinned && 'sticky z-10',
              selected ? SELECTED : pinned ? 'bg-background group-hover:bg-muted' : 'group-hover:bg-muted/40',
              focused && 'outline-2 -outline-offset-2 outline-primary',
            )}
            style={{
              width: l.width,
              ...(pinned ? { left: l.stickyLeft, right: l.stickyRight } : {}),
              ...(l.edge ? { boxShadow: `inset ${l.group === 'left' ? '-' : ''}2px 0 0 var(--foreground)` } : {}),
            }}
          >
            <GridCell value={row.original[col.name]} dataType={col.dataType} wrap={wrap} />
          </div>
        )
      })}
    </div>
  )
}
