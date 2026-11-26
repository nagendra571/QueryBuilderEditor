import { memo } from 'react'
import type { Row } from '@tanstack/react-table'
import { ListTree } from 'lucide-react'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto } from '@/types'
import { GridCell } from './GridCell'
import { ROW_NUM_WIDTH, cellId, type ColumnLayout, type ColumnWindow } from './grid-layout.ts'
import type { RowData } from './grid-table.ts'

// Sticky cells paint over scrolled ones, so their tints are mixed into an opaque colour.
const SELECTED = 'bg-[color-mix(in_srgb,var(--primary)_10%,var(--background))]'
// Same tone as the centre cells' `bg-muted/40` over the ground, but opaque.
const STICKY_HOVER = 'group-hover:bg-[color-mix(in_srgb,var(--muted)_40%,var(--background))]'

export interface GridRowProps {
  row: Row<RowData>
  index: number
  start: number
  height: number
  wrap: boolean
  width: number
  columns: QueryResultColumnDto[]
  layouts: ColumnLayout[]
  colWindow: ColumnWindow
  /** This row's slice of the selection (see `rowSlice`); -1 when none. */
  selLeft: number
  selRight: number
  focusCol: number
  idPrefix: string
  onPointerDown: (row: number, col: number, e: React.PointerEvent) => void
  onPointerEnter: (row: number, col: number) => void
  onAction?: (row: RowData) => void
  actionLabel?: string
  measureRef?: (el: HTMLElement | null) => void
}

// Memoised with primitive selection props: a scroll then renders only the rows that came into view.
export const GridRow = memo(function GridRow({
  row,
  index,
  start,
  height,
  wrap,
  width,
  columns,
  layouts,
  colWindow,
  selLeft,
  selRight,
  focusCol,
  idPrefix,
  onPointerDown,
  onPointerEnter,
  onAction,
  actionLabel,
  measureRef,
}: GridRowProps) {
  const focusRow = focusCol >= 0

  const cell = (ci: number) => {
    const col = columns[ci]
    const l = layouts[ci]
    const pinned = l.group !== 'centre'
    const selected = selLeft >= 0 && ci >= selLeft && ci <= selRight
    const focused = focusCol === ci
    const isNumber = col.dataType === 'number'
    return (
      <div
        key={col.name}
        id={cellId(idPrefix, index, ci)}
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
          selected ? SELECTED : pinned ? cn('bg-background', STICKY_HOVER) : 'group-hover:bg-muted/40',
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
  }

  return (
    <div
      role="row"
      aria-rowindex={index + 2}
      data-index={index}
      ref={measureRef}
      className="group absolute top-0 left-0 flex"
      style={{
        width,
        minWidth: '100%',
        transform: `translateY(${start}px)`,
        ...(wrap ? { minHeight: height } : { height }),
      }}
    >
      <div
        role="rowheader"
        aria-colindex={1}
        className={cn(
          'sticky left-0 z-10 flex shrink-0 items-center justify-end self-stretch border-r border-b border-border/60 bg-background px-2.5 font-numeric text-xs text-muted-foreground tabular-nums',
          STICKY_HOVER,
        )}
        style={{ width: ROW_NUM_WIDTH }}
      >
        <span className={cn(onAction && 'group-focus-within:opacity-0 group-hover:opacity-0', onAction && focusRow && 'opacity-0')}>{index + 1}</span>
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
      {colWindow.left.map(cell)}
      {colWindow.before > 0 && <div aria-hidden className="shrink-0" style={{ width: colWindow.before }} />}
      {colWindow.centre.map(cell)}
      {colWindow.after > 0 && <div aria-hidden className="shrink-0" style={{ width: colWindow.after }} />}
      {/* Grows only when the columns are narrower than the viewport, so right-pinned columns hug the right edge. */}
      <div aria-hidden className={cn('min-w-0 flex-1', colWindow.right.length > 0 && 'border-b border-border/60')} />
      {colWindow.right.map(cell)}
    </div>
  )
})
