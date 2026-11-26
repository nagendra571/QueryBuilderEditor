import { useCallback, useEffect, useId, useImperativeHandle, useMemo, useRef } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { MAX_COL_WIDTH, MIN_COL_WIDTH, createCanvasMeasure, fitWidth } from './column-widths.ts'
import { displayString } from './grid-columns.ts'
import { ROW_NUM_WIDTH, cellId, columnWindow, layoutColumns, moveColumn, pinColumn } from './grid-layout.ts'
import { nextSort } from './grid-view.ts'
import { GridHeaderRow } from './GridHeaderRow'
import type { HeaderActions } from './GridHeaderCell'
import { GridRow } from './GridRow'
import { rowSlice, type CellPos } from './selection.ts'
import { CELL_FONT, HEADER_EXTRA, HEADER_FONT, PINNED_HEADER_EXTRA, type useResultsGrid } from './useResultsGrid.ts'
import { useGridSelection } from './useGridSelection.ts'
import { cn } from '@/lib/utils'

const ROW_HEIGHT = { comfortable: 44, compact: 32 } as const
// Below this many centre columns every cell is rendered; above it only the ones in view (plus overscan).
const COLUMN_VIRTUALISATION_THRESHOLD = 16

export function ResultsGrid({
  grid,
  onRowClick,
  rowActionLabel,
  onVisibleRangeChange,
  onSelectionChange,
  copyRef,
}: {
  grid: ReturnType<typeof useResultsGrid>
  onRowClick?: (row: Record<string, unknown>) => void
  rowActionLabel?: string
  /** 1-based display rows in view, for the footer. */
  onVisibleRangeChange?: (first: number, last: number) => void
  /** 0 when nothing is selected, and again when the grid unmounts. */
  onSelectionChange?: (cells: number) => void
  copyRef?: React.Ref<{ copy: () => void }>
}) {
  const { table, view, setView, rows, columns, density } = grid
  const rowHeight = ROW_HEIGHT[density]
  const scrollRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<ReturnType<typeof createCanvasMeasure> | null>(null)
  const idPrefix = useId().replace(/:/g, '')

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
  })

  useEffect(() => {
    virtualizer.measure()
  }, [virtualizer, rowHeight, view.wrap])

  const centre = useMemo(() => layouts.filter((l) => l.group === 'centre'), [layouts])
  const virtualiseColumns = centre.length > COLUMN_VIRTUALISATION_THRESHOLD
  const columnVirtualizer = useVirtualizer({
    horizontal: true,
    enabled: virtualiseColumns,
    count: centre.length,
    getScrollElement: () => scrollRef.current,
    estimateSize: (i) => centre[i]?.width ?? 120,
    // Starts the centre group at its real x, so the virtualiser's item positions equal the layout's.
    paddingStart: centre[0]?.x ?? ROW_NUM_WIDTH,
    overscan: 3,
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

  const resetKey = [
    view.search,
    view.sort ? `${view.sort.column}:${view.sort.direction}` : '',
    view.order.join('\u0001'),
    view.hidden.join('\u0001'),
    view.pinnedLeft.join('\u0001'),
    view.pinnedRight.join('\u0001'),
  ].join('\u0002')

  const selection = useGridSelection({
    rows,
    columns,
    data: table.options.data,
    resetKey,
    pageRows,
    reveal,
    onEnter: onRowClick,
    onSelectionChange,
  })
  const { range, copy, copyColumn } = selection

  useImperativeHandle(copyRef, () => ({ copy }), [copy])

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

  const fit = useCallback(
    (name: string) => {
      measureRef.current ??= createCanvasMeasure({ header: HEADER_FONT, cell: CELL_FONT })
      const values = rows.slice(0, 200).map((r) => displayString(r.original[name]))
      const pinned = view.pinnedLeft.includes(name) || view.pinnedRight.includes(name)
      setWidth(name, fitWidth(name, values, measureRef.current, HEADER_EXTRA + (pinned ? PINNED_HEADER_EXTRA : 0)))
    },
    [rows, view.pinnedLeft, view.pinnedRight, setWidth],
  )

  const actionsFor = useCallback(
    (name: string): HeaderActions => ({
      toggleSort: () => setView((v) => ({ ...v, sort: nextSort(v.sort, name) })),
      setSort: (direction) => setView((v) => ({ ...v, sort: direction ? { column: name, direction } : null })),
      pin: (side) => setView((v) => pinColumn(v, name, side)),
      hide: () => setView((v) => (v.hidden.includes(name) ? v : { ...v, hidden: [...v.hidden, name] })),
      fit: () => fit(name),
      copyColumn: () => copyColumn(name),
      resize: (width) => setWidth(name, width),
    }),
    [setView, fit, copyColumn, setWidth],
  )

  const onReorder = useCallback(
    (active: string, over: string) => setView((v) => moveColumn(v, active, over)),
    [setView],
  )

  const focusRendered =
    !!range && items.some((i) => i.index === range.focus.row) &&
    (layouts[range.focus.col]?.group !== 'centre' || colWindow.centre.includes(range.focus.col))

  return (
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
  )
}
