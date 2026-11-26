import { useCallback, useEffect, useImperativeHandle, useRef } from 'react'
import { useVirtualizer } from '@tanstack/react-virtual'
import { createCanvasMeasure, fitWidth } from './column-widths.ts'
import { displayString } from './grid-columns.ts'
import { ROW_NUM_WIDTH, layoutColumns, moveColumn, pinColumn } from './grid-layout.ts'
import { nextSort } from './grid-view.ts'
import { GridHeaderRow } from './GridHeaderRow'
import type { HeaderActions } from './GridHeaderCell'
import { GridRow } from './GridRow'
import type { CellPos } from './selection.ts'
import { HEADER_EXTRA, type useResultsGrid } from './useResultsGrid.ts'
import { useGridSelection } from './useGridSelection.ts'
import { cn } from '@/lib/utils'

const ROW_HEIGHT = { comfortable: 44, compact: 32 } as const

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
  /** 0 when nothing is selected. */
  onSelectionChange?: (cells: number) => void
  copyRef?: React.Ref<{ copy: () => void }>
}) {
  const { table, view, setView, rows, columns, density } = grid
  const rowHeight = ROW_HEIGHT[density]
  const scrollRef = useRef<HTMLDivElement>(null)
  const measureRef = useRef<ReturnType<typeof createCanvasMeasure> | null>(null)

  const widths = columns.map((c) => table.getColumn(c.name)?.getSize() ?? 120)
  const layouts = layoutColumns(
    columns.map((c) => c.name),
    widths,
    view.pinnedLeft,
    view.pinnedRight,
  )
  const sizerWidth = ROW_NUM_WIDTH + widths.reduce((a, b) => a + b, 0)

  // Rows-only virtualisation: column widths are capped at 480px, so even 60 columns are a few
  // thousand cells per screenful of rows, which stays cheap without windowing columns too.
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

  function setWidth(name: string, width: number) {
    setView((v) => ({ ...v, widths: { ...v.widths, [name]: width } }))
  }

  function fit(name: string) {
    measureRef.current ??= createCanvasMeasure({
      header: '600 12px "Public Sans Variable"',
      cell: '400 13px "Public Sans Variable"',
    })
    const values = rows.slice(0, 200).map((r) => displayString(r.original[name]))
    setWidth(name, fitWidth(name, values, measureRef.current, HEADER_EXTRA))
  }

  const actionsFor = (name: string): HeaderActions => ({
    toggleSort: () => setView((v) => ({ ...v, sort: nextSort(v.sort, name) })),
    setSort: (direction) => setView((v) => ({ ...v, sort: direction ? { column: name, direction } : null })),
    pin: (side) => setView((v) => pinColumn(v, name, side)),
    hide: () => setView((v) => (v.hidden.includes(name) ? v : { ...v, hidden: [...v.hidden, name] })),
    fit: () => fit(name),
    copyColumn: () => copyColumn(name),
    resize: (width) => setWidth(name, width),
  })

  return (
    <div
      ref={scrollRef}
      role="grid"
      tabIndex={0}
      aria-label="Query results"
      aria-rowcount={rows.length + 1}
      aria-colcount={columns.length + 1}
      aria-multiselectable
      onKeyDown={selection.onKeyDown}
      className={cn(
        'relative h-full min-h-60 overflow-auto bg-background select-none',
        'focus-visible:outline-2 focus-visible:-outline-offset-2 focus-visible:outline-ring',
      )}
    >
      <div className="relative" style={{ width: sizerWidth, height: virtualizer.getTotalSize() }}>
        <GridHeaderRow
          columns={columns}
          layouts={layouts}
          sort={view.sort}
          height={rowHeight}
          width={sizerWidth}
          actionsFor={actionsFor}
          onReorder={(active, over) => setView((v) => moveColumn(v, active, over))}
        />
        {items.map((item) => (
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
            range={range}
            onPointerDown={selection.onCellPointerDown}
            onPointerEnter={selection.onCellPointerEnter}
            onAction={onRowClick}
            actionLabel={rowActionLabel}
            measureRef={view.wrap ? virtualizer.measureElement : undefined}
          />
        ))}
      </div>
    </div>
  )
}
