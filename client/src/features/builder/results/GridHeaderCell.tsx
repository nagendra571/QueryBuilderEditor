import { useSortable } from '@dnd-kit/sortable'
import { CSS } from '@dnd-kit/utilities'
import { useRef } from 'react'
import { ArrowDown, ArrowUp, ChevronsUpDown, Filter, GripVertical, MoreVertical, Pin } from 'lucide-react'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Popover, PopoverAnchor, PopoverTrigger } from '@/components/ui/popover'
import { cn } from '@/lib/utils'
import type { QueryResultColumnDto } from '@/types'
import { MAX_COL_WIDTH, MIN_COL_WIDTH } from './column-widths.ts'
import type { ColumnLayout } from './grid-layout.ts'
import { TypeBadge } from './TypeBadge'

const KEY_STEP = 16
const clampWidth = (w: number) => Math.min(MAX_COL_WIDTH, Math.max(MIN_COL_WIDTH, Math.round(w)))

export interface HeaderActions {
  toggleSort: () => void
  setSort: (direction: 'asc' | 'desc' | null) => void
  pin: (side: 'left' | 'right' | null) => void
  /** Absent when this is the only visible column. */
  hide?: () => void
  fit: () => void
  copyColumn: () => void
  resize: (width: number) => void
  filter: { active: boolean; open: () => void; clear: () => void }
  /** Shows the blue mark; true when the column has a number/date format, bars, scale or highlight rules. */
  formatted: boolean
  /** Opens the formatting panel for this column. The menu item is hidden when absent. */
  format?: () => void
  /** Absent hides the item; a reason shows it disabled. */
  chart?: { onSelect: () => void; disabledReason?: string }
}

export function GridHeaderCell({
  column,
  layout,
  colIndex,
  sortDirection,
  actions,
  filterOpen,
  onFilterOpenChange,
  filterContent,
}: {
  column: QueryResultColumnDto
  layout: ColumnLayout
  colIndex: number
  sortDirection: 'asc' | 'desc' | null
  actions: HeaderActions
  filterOpen: boolean
  onFilterOpenChange: (open: boolean) => void
  /** Rendered only while open. */
  filterContent: React.ReactNode
}) {
  const { attributes, listeners, setNodeRef, transform, transition, isDragging } = useSortable({ id: column.name })
  const pinned = layout.group !== 'centre'
  // Opening the popover from the menu waits for the menu's own focus return, which would otherwise dismiss it.
  const openFilterAfterMenu = useRef(false)

  const style: React.CSSProperties = {
    width: layout.width,
    minWidth: layout.width,
    transform: CSS.Transform.toString(transform),
    transition,
    ...(pinned ? { position: 'sticky', left: layout.stickyLeft, right: layout.stickyRight, zIndex: isDragging ? 30 : 15 } : {}),
    // An inset box-shadow is the 2px ink edge here, not an elevation shadow.
    ...(layout.edge ? { boxShadow: `inset ${layout.group === 'left' ? '-' : ''}2px 0 0 var(--foreground)` } : {}),
  }

  function onResizePointerDown(e: React.PointerEvent<HTMLDivElement>) {
    if (e.button !== 0) return
    e.preventDefault()
    e.stopPropagation()
    const handle = e.currentTarget
    handle.setPointerCapture(e.pointerId)
    const startX = e.clientX
    const startWidth = layout.width
    const move = (ev: PointerEvent) => actions.resize(clampWidth(startWidth + ev.clientX - startX))
    const end = () => {
      handle.removeEventListener('pointermove', move)
      handle.removeEventListener('pointerup', end)
      handle.removeEventListener('pointercancel', end)
    }
    handle.addEventListener('pointermove', move)
    handle.addEventListener('pointerup', end)
    handle.addEventListener('pointercancel', end)
  }

  function onResizeKeyDown(e: React.KeyboardEvent) {
    if (e.key !== 'ArrowLeft' && e.key !== 'ArrowRight') return
    e.preventDefault()
    e.stopPropagation()
    actions.resize(clampWidth(layout.width + (e.key === 'ArrowRight' ? KEY_STEP : -KEY_STEP)))
  }

  return (
    <Popover open={filterOpen} onOpenChange={onFilterOpenChange}>
    <PopoverAnchor asChild>
    <div
      ref={setNodeRef}
      role="columnheader"
      aria-colindex={colIndex + 2}
      aria-sort={sortDirection === 'asc' ? 'ascending' : sortDirection === 'desc' ? 'descending' : 'none'}
      style={style}
      className={cn(
        'group/h relative flex shrink-0 items-center gap-1 border-r border-border/60 bg-muted px-2.5 text-xs font-semibold whitespace-nowrap',
        isDragging && 'bg-background opacity-90 shadow-md',
      )}
    >
      <button
        type="button"
        className="flex w-3.5 shrink-0 cursor-grab touch-none justify-center text-muted-foreground/40 hover:text-foreground active:cursor-grabbing"
        aria-label={`Reorder ${column.name}`}
        {...attributes}
        {...listeners}
      >
        <GripVertical className="size-3.5" />
      </button>
      <TypeBadge dataType={column.dataType} className="w-8 px-0 text-center" />
      <button
        type="button"
        onClick={actions.toggleSort}
        className="flex min-w-0 flex-1 items-center gap-1 text-left hover:text-primary"
        title={column.name}
      >
        <span className="min-w-0 truncate">{column.name}</span>
        {sortDirection === 'asc' && <ArrowUp className="size-3 shrink-0" />}
        {sortDirection === 'desc' && <ArrowDown className="size-3 shrink-0" />}
        {!sortDirection && <ChevronsUpDown className="size-3 shrink-0 opacity-30" />}
      </button>
      {actions.formatted && <span className="size-1.5 shrink-0 rounded-full bg-primary" role="img" aria-label="Formatted" title="Formatted" />}
      <PopoverTrigger asChild>
        <button
          type="button"
          aria-label={actions.filter.active ? `Filter ${column.name} (active)` : `Filter ${column.name}`}
          className={cn(
            'flex size-[18px] shrink-0 items-center justify-center rounded-sm hover:text-foreground focus-visible:outline-2 focus-visible:outline-ring',
            actions.filter.active ? 'text-primary' : 'text-muted-foreground/60',
          )}
        >
          <Filter className={cn('size-3.5', actions.filter.active && 'fill-current')} />
        </button>
      </PopoverTrigger>
      {pinned && <Pin className="size-3 shrink-0 text-muted-foreground" aria-label="Pinned" />}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button
            type="button"
            aria-label={`Column options for ${column.name}`}
            className="flex size-[18px] shrink-0 items-center justify-center rounded-sm text-muted-foreground opacity-0 hover:text-foreground focus-visible:opacity-100 group-focus-within/h:opacity-100 group-hover/h:opacity-100 data-[state=open]:opacity-100"
          >
            <MoreVertical className="size-3.5" />
          </button>
        </DropdownMenuTrigger>
        <DropdownMenuContent
          align="start"
          onCloseAutoFocus={(e) => {
            if (!openFilterAfterMenu.current) return
            openFilterAfterMenu.current = false
            e.preventDefault()
            actions.filter.open()
          }}
        >
          <DropdownMenuItem onSelect={() => actions.setSort('asc')}>Sort ascending</DropdownMenuItem>
          <DropdownMenuItem onSelect={() => actions.setSort('desc')}>Sort descending</DropdownMenuItem>
          {sortDirection && <DropdownMenuItem onSelect={() => actions.setSort(null)}>Clear sort</DropdownMenuItem>}
          <DropdownMenuSeparator />
          <DropdownMenuItem
            onSelect={() => {
              openFilterAfterMenu.current = true
            }}
          >
            Filter…
          </DropdownMenuItem>
          {actions.filter.active && <DropdownMenuItem onSelect={actions.filter.clear}>Clear filter</DropdownMenuItem>}
          {actions.format && <DropdownMenuItem onSelect={actions.format}>Format…</DropdownMenuItem>}
          {actions.chart && (
            // The reason is text inside the item: a disabled Radix item swallows pointer events, so a tooltip would never show.
            <DropdownMenuItem
              disabled={!!actions.chart.disabledReason}
              onSelect={actions.chart.onSelect}
              className={actions.chart.disabledReason ? 'flex-col items-start gap-0' : undefined}
            >
              Chart this column
              {actions.chart.disabledReason && (
                <span className="text-[11px] font-normal leading-snug text-muted-foreground">{actions.chart.disabledReason}</span>
              )}
            </DropdownMenuItem>
          )}
          <DropdownMenuSeparator />
          {layout.group !== 'left' && <DropdownMenuItem onSelect={() => actions.pin('left')}>Pin to left</DropdownMenuItem>}
          {layout.group !== 'right' && <DropdownMenuItem onSelect={() => actions.pin('right')}>Pin to right</DropdownMenuItem>}
          {pinned && <DropdownMenuItem onSelect={() => actions.pin(null)}>Unpin</DropdownMenuItem>}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={actions.fit}>Fit to content</DropdownMenuItem>
          {actions.hide && <DropdownMenuItem onSelect={actions.hide}>Hide column</DropdownMenuItem>}
          <DropdownMenuSeparator />
          <DropdownMenuItem onSelect={actions.copyColumn}>Copy column values</DropdownMenuItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <div
        role="separator"
        aria-orientation="vertical"
        aria-label={`Resize ${column.name}`}
        aria-valuenow={layout.width}
        aria-valuemin={MIN_COL_WIDTH}
        aria-valuemax={MAX_COL_WIDTH}
        tabIndex={0}
        onPointerDown={onResizePointerDown}
        onDoubleClick={(e) => {
          e.stopPropagation()
          actions.fit()
        }}
        onKeyDown={onResizeKeyDown}
        className="absolute inset-y-0 right-0 w-1.5 cursor-col-resize touch-none hover:bg-primary/40 focus-visible:bg-primary focus-visible:outline-none"
      />
    </div>
    </PopoverAnchor>
    {filterOpen && filterContent}
    </Popover>
  )
}
