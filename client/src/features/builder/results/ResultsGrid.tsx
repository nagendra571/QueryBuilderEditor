import { useCallback, useEffect, useId, useImperativeHandle, useLayoutEffect, useMemo, useRef, useState } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { HEADER_FONT, MAX_COL_WIDTH, MIN_COL_WIDTH, cellFont, createCanvasMeasure, fitWidth } from './column-widths.ts'
import { displayString } from './grid-columns.ts'
import { ROW_NUM_WIDTH, cellId, columnWindow, layoutColumns, moveColumn, pinColumn } from './grid-layout.ts'
import { nextSort } from './grid-view.ts'
import { GridHeaderRow } from './GridHeaderRow'
import type { HeaderActions } from './GridHeaderCell'
import { GridRow } from './GridRow'
import { rangeContains, rowSlice, type CellPos } from './selection.ts'
import { restoredScroll, type GridMemory } from './grid-memory.ts'
import { HEADER_EXTRA, PINNED_HEADER_EXTRA, type useResultsGrid } from './useResultsGrid.ts'
import { useGridSelection } from './useGridSelection.ts'
import type { SelectionStore } from './selection-store.ts'
import { FilterPopover } from './FilterPopover'
import { CellContextMenu } from './CellContextMenu'
import type { QueryResultColumnDto } from '@/types'
import { cn } from '@/lib/utils'

export interface GridHandle {
  copy(): void
  moveTo(pos: CellPos): void
  focus(): void
}

const ROW_HEIGHT = { comfortable: 44, compact: 32 } as const
// Below this many centre columns every cell is rendered; above it only the ones in view (plus overscan).
const COLUMN_VIRTUALISATION_THRESHOLD = 16

export function ResultsGrid({
  grid,
  onRowClick,
  rowActionLabel,
  onVisibleRangeChange,
  store,
  gridRef,
  memory,
  filterOpenFor,
  onFilterOpenChange,
  onOpenRecord,
}: {
  grid: ReturnType<typeof useResultsGrid>
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  /** 1-based display rows in view, for the footer. */
  onVisibleRangeChange?: (first: number, last: number) => void
  /** Receives the selection, displayed rows and visible columns; reset to no selection when the grid unmounts. */
  store?: SelectionStore
  gridRef?: React.Ref<GridHandle>
  /** Keeps the scroll position and selection across the remount that full screen causes. */
  memory?: GridMemory
  /** Column whose filter popover is open; lives in the parent so a chip can open it. */
  filterOpenFor?: string | null
  onFilterOpenChange?: (column: string | null) => void
  /** Shows the Details panel on its Row tab. */
  onOpenRecord?: () => void
}) {
  const { table, view, setView, rows, columns, density, filters, allColumns, setColumnFilter, clearColumnFilter, showOnlyValue, hideColumnValue } = grid
  const rowHeight = ROW_HEIGHT[density]
  const scrollRef = useRef<HTMLDivElement>(null)
  const idPrefix = useId().replace(/:/g, '')
  const data = table.options.data

  const resetKey = [
    view.search,
    view.sort ? `${view.sort.column}:${view.sort.direction}` : '',
    view.order.join('\u0001'),
    view.hidden.join('\u0001'),
    view.pinnedLeft.join('\u0001'),
    JSON.stringify(view.filters),
    view.pinnedRight.join('\u0001'),
  ].join('\u0002')

  const [initialScroll] = useState(() => restoredScroll(memory, resetKey, data))

  const sizing = table.getState().columnSizing
  // Same clamp as TanStack's getSize, read from the memoised sizing state so the result is stable across scrolls.
  const widths = useMemo(
    () => columns.map((c) => Math.min(MAX_COL_WIDTH, Math.max(MIN_COL_WIDTH, sizing[c.name] ?? 150))),
    [columns, sizing],
  )
  const layouts = useMemo(
    () =>
      layoutColumns(
        columns.map((c) => c.name),
        widths,
        view.pinnedLeft,
        view.pinnedRight,
      ),
    [columns, widths, view.pinnedLeft, view.pinnedRight],
  )
  const sizerWidth = useMemo(() => ROW_NUM_WIDTH + widths.reduce((a, b) => a + b, 0), [widths])

  const virtualizer = useVirtualizer({
    count: rows.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: () => rowHeight,
    overscan: 10,
    paddingStart: rowHeight,
    scrollPaddingStart: rowHeight,
    measureElement: view.wrap ? undefined : () => rowHeight,
    initialOffset: initialScroll.top,
  })

  useEffect(() => {
    virtualizer.measure()
  }, [virtualizer, rowHeight, view.wrap])

  const centre = useMemo(() => layouts.filter((l) => l.group === 'centre'), [layouts])
  // Wrapped rows are as tall as their tallest rendered cell, so a moving column window would change row heights.
  const virtualiseColumns = !view.wrap && centre.length > COLUMN_VIRTUALISATION_THRESHOLD
  const columnVirtualizer = useVirtualizer({
    horizontal: true,
    enabled: virtualiseColumns,
    count: centre.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: (i) => centre[i]?.width ?? 120,
    // Starts the centre group at its real x, so the virtualiser's item positions equal the layout's.
    paddingStart: centre[0]?.x ?? ROW_NUM_WIDTH,
    overscan: 3,
    initialOffset: initialScroll.left,
  })

  useEffect(() => {
    columnVirtualizer.measure()
  }, [columnVirtualizer, centre])

  const colItems = virtualiseColumns ? columnVirtualizer.getVirtualItems() : []
  const colFrom = virtualiseColumns && colItems.length ? colItems[0].index : null
  const colTo = virtualiseColumns && colItems.length ? colItems[colItems.length - 1].index : null
  const colWindow = useMemo(() => columnWindow(layouts, colFrom, colTo), [layouts, colFrom, colTo])

  const reveal = useCallback(
    (pos: CellPos) => {
      virtualizer.scrollToIndex(pos.row, { align: 'auto' })
      const el = scrollRef.current
      const l = layouts[pos.col]
      if (!el || !l || l.group !== 'centre') return
      const leftInset = ROW_NUM_WIDTH + layouts.filter((x) => x.group === 'left').reduce((a, x) => a + x.width, 0)
      const rightInset = layouts.filter((x) => x.group === 'right').reduce((a, x) => a + x.width, 0)
      if (l.x < el.scrollLeft + leftInset) el.scrollLeft = l.x - leftInset
      else if (l.x + l.width > el.scrollLeft + el.clientWidth - rightInset) {
        el.scrollLeft = l.x + l.width - el.clientWidth + rightInset
      }
    },
    [virtualizer, layouts],
  )

  const pageRows = useCallback(
    () => Math.max(1, Math.floor((scrollRef.current?.clientHeight ?? rowHeight * 10) / rowHeight) - 1),
    [rowHeight],
  )

  // The virtualisers only apply their initial offset to the axis they own, and the column one may be disabled.
  useLayoutEffect(() => {
    const el = scrollRef.current
    if (!el) return
    el.scrollTop = initialScroll.top
    el.scrollLeft = initialScroll.left
  }, [initialScroll])

  const saveScroll = useCallback(() => {
    const el = scrollRef.current
    if (el) memory?.saveScroll({ resetKey, data, top: el.scrollTop, left: el.scrollLeft })
  }, [memory, resetKey, data])
  // Re-keyed on every view change, so the saved position is always the one the mounted grid shows.
  useEffect(() => {
    saveScroll()
  }, [saveScroll])

  const [menuCell, setMenuCell] = useState<CellPos | null>(null)
  const openMenuAtActive = useRef<() => void>(() => {})
  const onContextMenuKey = useCallback(() => openMenuAtActive.current(), [])

  const selection = useGridSelection({
    rows,
    columns,
    data,
    resetKey,
    pageRows,
    reveal,
    onEnter: onRowClick,
    onContextMenuKey,
    memory,
  })
  const { range, copy, copyCell, copyColumn, moveTo } = selection

  // One menu for the whole body: the cell is found from the event target, so cells carry no menu of their own.
  const anchorToCell = useCallback((pos: CellPos): boolean => {
    const el = document.getElementById(cellId(idPrefix, pos.row, pos.col))
    if (!el) return false
    const r = el.getBoundingClientRect()
    el.dispatchEvent(
      new MouseEvent('contextmenu', {
        bubbles: true,
        cancelable: true,
        clientX: r.left + Math.min(r.width / 2, 40),
        clientY: r.bottom - 4,
      }),
    )
    return true
  }, [idPrefix])
  useEffect(() => {
    openMenuAtActive.current = () => {
      if (range) anchorToCell(range.focus)
    }
  }, [range, anchorToCell])

  const onContextMenuCapture = (e: React.MouseEvent) => {
    const el = (e.target as Element).closest('[data-row][data-col]') as HTMLElement | null
    if (!el) {
      // Shift+F10 or the menu key arrives with the grid as target; re-aim it at the active cell.
      if (e.nativeEvent.isTrusted && range && e.target === e.currentTarget && anchorToCell(range.focus)) {
        e.preventDefault()
        e.stopPropagation()
        return
      }
      // Header and row numbers keep the browser's own menu.
      e.stopPropagation()
      setMenuCell(null)
      return
    }
    const pos = { row: Number(el.dataset.row), col: Number(el.dataset.col) }
    if (!range || !rangeContains(range, pos)) moveTo(pos)
    setMenuCell(pos)
  }
  const menuColumn = menuCell ? columns[menuCell.col] : undefined
  const menuRow = menuCell ? rows[menuCell.row] : undefined
  const menuValue = menuColumn && menuRow ? menuRow.original[menuColumn.name] : undefined

  useImperativeHandle(
    gridRef,
    () => ({ copy, moveTo, focus: () => scrollRef.current?.focus({ preventScroll: true }) }),
    [copy, moveTo],
  )

  const displayed = useMemo(() => rows.map((r) => r.original), [rows])
  useEffect(() => {
    store?.publish({ rows: displayed, columns })
  }, [store, displayed, columns])
  useEffect(() => {
    store?.publish({ range })
  }, [store, range])
  useEffect(() => () => store?.publish({ range: null }), [store])

  const items = virtualizer.getVirtualItems()
  const offset = virtualizer.scrollOffset ?? 0
  const viewport = virtualizer.scrollRect?.height ?? 0
  const inView = items.filter((i) => i.end > offset + rowHeight && i.start < offset + viewport)
  const first = inView.length ? inView[0].index + 1 : 0
  const last = inView.length ? inView[inView.length - 1].index + 1 : 0
  const lastRange = useRef('')
  useEffect(() => {
    const key = `${first}:${last}`
    if (lastRange.current === key) return
    lastRange.current = key
    onVisibleRangeChange?.(first, last)
  }, [first, last, onVisibleRangeChange])

  const setWidth = useCallback(
    (name: string, width: number) => setView((v) => ({ ...v, widths: { ...v.widths, [name]: width } })),
    [setView],
  )

  const measure = useMemo(() => createCanvasMeasure({ header: HEADER_FONT, cell: cellFont(density) }), [density])

  const fit = useCallback(
    (name: string) => {
      const values = rows.slice(0, 200).map((r) => displayString(r.original[name]))
      const pinned = view.pinnedLeft.includes(name) || view.pinnedRight.includes(name)
      setWidth(name, fitWidth(name, values, measure, HEADER_EXTRA + (pinned ? PINNED_HEADER_EXTRA : 0)))
    },
    [rows, view.pinnedLeft, view.pinnedRight, setWidth, measure],
  )

  const canHide = columns.length > 1

  const actionsFor = useCallback(
    (name: string): HeaderActions => ({
      toggleSort: () => setView((v) => ({ ...v, sort: nextSort(v.sort, name) })),
      setSort: (direction) => setView((v) => ({ ...v, sort: direction ? { column: name, direction } : null })),
      pin: (side) => setView((v) => pinColumn(v, name, side)),
      hide: canHide
        ? () => setView((v) => (v.hidden.includes(name) ? v : { ...v, hidden: [...v.hidden, name] }))
        : undefined,
      fit: () => fit(name),
      copyColumn: () => copyColumn(name),
      resize: (width) => setWidth(name, width),
      filter: {
        active: filters.some((f) => f.column === name),
        open: () => onFilterOpenChange?.(name),
        clear: () => clearColumnFilter(name),
      },
    }),
    [setView, fit, copyColumn, setWidth, canHide, filters, onFilterOpenChange, clearColumnFilter],
  )

  const onColumnFilterOpen = useCallback(
    (column: string, open: boolean) => onFilterOpenChange?.(open ? column : null),
    [onFilterOpenChange],
  )
  const renderFilter = useCallback(
    (column: QueryResultColumnDto) => (
      <FilterPopover
        column={column}
        rows={data as Record<string, unknown>[]}
        filters={filters}
        columns={allColumns}
        onApply={(next) => (next ? setColumnFilter(next) : clearColumnFilter(column.name))}
        onClose={() => onFilterOpenChange?.(null)}
      />
    ),
    [data, filters, allColumns, setColumnFilter, clearColumnFilter, onFilterOpenChange],
  )

  const onReorder = useCallback(
    (active: string, over: string) => setView((v) => moveColumn(v, active, over)),
    [setView],
  )

  const focusRendered =
    !!range && items.some((i) => i.index === range.focus.row) &&
    (layouts[range.focus.col]?.group !== 'centre' || colWindow.centre.includes(range.focus.col))

  return (
    <CellContextMenu
      value={menuValue}
      hasTarget={!!menuColumn && !!menuRow}
      onShowOnly={() => menuColumn && showOnlyValue(menuColumn.name, menuValue)}
      onHide={() => menuColumn && hideColumnValue(menuColumn.name, menuValue)}
      onCopyCell={() => menuCell && copyCell(menuCell)}
      onOpenRecord={onOpenRecord}
    >
    <div
      ref={scrollRef}
      role="grid"
      tabIndex={0}
      aria-label="Query results"
      aria-rowcount={rows.length + 1}
      aria-colcount={columns.length + 1}
      aria-multiselectable
      aria-activedescendant={focusRendered && range ? cellId(idPrefix, range.focus.row, range.focus.col) : undefined}
      onKeyDown={selection.onKeyDown}
      onContextMenuCapture={onContextMenuCapture}
      onScroll={saveScroll}
      className={cn(
        'relative h-full min-h-60 overflow-auto bg-background select-none',
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
      )}
    >
      <div className="relative" style={{ width: sizerWidth, minWidth: '100%', height: virtualizer.getTotalSize() }}>
        <GridHeaderRow
          columns={columns}
          layouts={layouts}
          sort={view.sort}
          height={rowHeight}
          width={sizerWidth}
          actionsFor={actionsFor}
          onReorder={onReorder}
          filterOpenFor={filterOpenFor ?? null}
          onFilterOpenChange={onColumnFilterOpen}
          renderFilter={renderFilter}
        />
        {items.map((item) => {
          const slice = rowSlice(range, item.index)
          return (
            <GridRow
              key={item.key}
              row={rows[item.index]}
              index={item.index}
              start={item.start}
              height={rowHeight}
              wrap={view.wrap}
              width={sizerWidth}
              columns={columns}
              layouts={layouts}
              colWindow={colWindow}
              selLeft={slice.selLeft}
              selRight={slice.selRight}
              focusCol={slice.focusCol}
              idPrefix={idPrefix}
              onPointerDown={selection.onCellPointerDown}
              onPointerEnter={selection.onCellPointerEnter}
              onAction={onRowClick}
              actionLabel={rowActionLabel}
              measureRef={view.wrap ? virtualizer.measureElement : undefined}
            />
          )
        })}
      </div>
    </div>
    </CellContextMenu>
  )
}
