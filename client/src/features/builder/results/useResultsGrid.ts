import { useCallback, useEffect, useMemo, useState } from 'react'
import {
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnSizingState,
  type Row,
  type SortingState,
  type Table,
  type Updater,
} from '@tanstack/react-table'
import type { QueryResultColumnDto, QueryResultDto } from '@/types'
import { useDensityStore, type Density } from '../../../state/density-store.ts'
import { DEFAULT_COL_CAP, FIT_SAMPLE_ROWS, HEADER_FONT, cellFont, createCanvasMeasure, fitWidth } from './column-widths.ts'
import { displayOrder, displayString } from './grid-columns.ts'
import { buildColumnDefs, tableBehaviour, type RowData } from './grid-table.ts'
import { removeFilter, rowsPassing, setFilter, showOnly, hideValue, valueKey, type GridFilter } from './grid-filters.ts'
import { carryOver, defaultView, isDefaultView, type GridView } from './grid-view.ts'
import { HEADER_EXTRA, PINNED_HEADER_EXTRA } from './header-width.ts'

export { HEADER_EXTRA, PINNED_HEADER_EXTRA }

function resolve<T>(updater: Updater<T>, current: T): T {
  return typeof updater === 'function' ? (updater as (old: T) => T)(current) : updater
}

export function useResultsGrid(result: QueryResultDto) {
  const [state, setState] = useState(() => ({
    result,
    view: defaultView(result.columns.map((c) => c.name)),
  }))

  // A fresh run produces a new `result`; carrying over during render avoids a frame of the old view.
  let view = state.view
  if (state.result !== result) {
    view = carryOver(
      state.view,
      state.result.columns.map((c) => c.name),
      result.columns.map((c) => c.name),
    )
    setState({ result, view })
  }

  const names = useMemo(() => result.columns.map((c) => c.name), [result.columns])

  const appDensity = useDensityStore((s) => s.density)
  const density: Density = view.density ?? appDensity

  const setView = useCallback((update: (v: GridView) => GridView) => {
    setState((s) => ({ ...s, view: update(s.view) }))
  }, [])

  // Canvas measures with a fallback font until the web font has loaded, which under-sizes headers.
  const [fontsReady, setFontsReady] = useState(
    () => typeof document === 'undefined' || !document.fonts || document.fonts.status === 'loaded',
  )
  useEffect(() => {
    if (fontsReady) return
    let live = true
    document.fonts.ready.then(() => {
      if (live) setFontsReady(true)
    })
    return () => {
      live = false
    }
  }, [fontsReady])

  const defaultWidths = useMemo(() => {
    void fontsReady
    const measure = createCanvasMeasure({ header: HEADER_FONT, cell: cellFont(density) })
    const plain: Record<string, number> = {}
    const pinned: Record<string, number> = {}
    for (const c of result.columns) {
      const values = result.rows.slice(0, FIT_SAMPLE_ROWS).map((r) => displayString(r[c.name]))
      plain[c.name] = fitWidth(c.name, values, measure, HEADER_EXTRA, DEFAULT_COL_CAP)
      pinned[c.name] = Math.max(
        plain[c.name],
        fitWidth(c.name, [], measure, HEADER_EXTRA + PINNED_HEADER_EXTRA, DEFAULT_COL_CAP),
      )
    }
    return { plain, pinned }
  }, [result, fontsReady, density])

  const defaultWidthOf = useCallback(
    (name: string, pinnedLeft: string[], pinnedRight: string[]) =>
      (pinnedLeft.includes(name) || pinnedRight.includes(name) ? defaultWidths.pinned : defaultWidths.plain)[name],
    [defaultWidths],
  )

  const columnDefs = useMemo(() => buildColumnDefs(result.columns), [result.columns])

  const sizing = useMemo<ColumnSizingState>(
    () =>
      Object.fromEntries(
        names.map((n) => [n, view.widths[n] ?? defaultWidthOf(n, view.pinnedLeft, view.pinnedRight)]),
      ),
    [names, view.widths, view.pinnedLeft, view.pinnedRight, defaultWidthOf],
  )

  const sorting = useMemo<SortingState>(
    () => (view.sort ? [{ id: view.sort.column, desc: view.sort.direction === 'desc' }] : []),
    [view.sort],
  )

  const columnFilters = useMemo(() => view.filters.map((f) => ({ id: f.column, value: f })), [view.filters])

  const columnVisibility = useMemo(() => Object.fromEntries(view.hidden.map((n) => [n, false])), [view.hidden])
  const columnPinning = useMemo(
    () => ({ left: view.pinnedLeft, right: view.pinnedRight }),
    [view.pinnedLeft, view.pinnedRight],
  )

  const table: Table<RowData> = useReactTable<RowData>({
    data: result.rows,
    columns: columnDefs,
    state: {
      columnOrder: view.order,
      columnVisibility,
      columnPinning,
      columnSizing: sizing,
      sorting,
      columnFilters,
      globalFilter: view.search,
    },
    ...tableBehaviour,
    onSortingChange: (updater) => {
      const next = resolve(updater, sorting)[0]
      setView((v) => ({
        ...v,
        sort: next ? { column: next.id, direction: next.desc ? 'desc' : 'asc' } : null,
      }))
    },
    onColumnSizingChange: (updater) => {
      const next = resolve(updater, sizing)
      setView((v) => {
        const widths = { ...v.widths }
        for (const n of names) {
          if (next[n] !== undefined && next[n] !== (v.widths[n] ?? defaultWidthOf(n, v.pinnedLeft, v.pinnedRight))) widths[n] = next[n]
        }
        return { ...v, widths }
      })
    },
    onColumnOrderChange: (updater) => setView((v) => ({ ...v, order: resolve(updater, v.order) })),
    onColumnVisibilityChange: (updater) =>
      setView((v) => {
        const next = resolve(updater, Object.fromEntries(v.hidden.map((n) => [n, false])))
        return { ...v, hidden: names.filter((n) => next[n] === false) }
      }),
    onColumnPinningChange: (updater) =>
      setView((v) => {
        const next = resolve(updater, { left: v.pinnedLeft, right: v.pinnedRight })
        return { ...v, pinnedLeft: next.left ?? [], pinnedRight: next.right ?? [] }
      }),
    onGlobalFilterChange: (updater) => setView((v) => ({ ...v, search: resolve(updater, v.search) ?? '' })),
    getCoreRowModel: getCoreRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getSortedRowModel: getSortedRowModel(),
  })

  const columns = useMemo(() => {
    const byName = new Map(result.columns.map((c) => [c.name, c]))
    return displayOrder(view, names)
      .map((n) => byName.get(n))
      .filter((c): c is QueryResultColumnDto => !!c)
  }, [result.columns, names, view])

  const rows: Row<RowData>[] = table.getRowModel().rows

  const typeOf = useCallback(
    (column: string) => result.columns.find((c) => c.name === column)?.dataType ?? 'unknown',
    [result.columns],
  )
  const setColumnFilter = useCallback((next: GridFilter) => setView((v) => ({ ...v, filters: setFilter(v.filters, next) })), [setView])
  const clearColumnFilter = useCallback(
    (column: string) => setView((v) => ({ ...v, filters: removeFilter(v.filters, column) })),
    [setView],
  )
  const clearFilters = useCallback(() => setView((v) => ({ ...v, filters: [] })), [setView])
  const showOnlyValue = useCallback(
    (column: string, raw: unknown) =>
      setView((v) => ({ ...v, filters: showOnly(v.filters, column, valueKey(raw, typeOf(column))) })),
    [setView, typeOf],
  )
  const hideColumnValue = useCallback(
    (column: string, raw: unknown) =>
      setView((v) => ({ ...v, filters: hideValue(v.filters, column, valueKey(raw, typeOf(column))) })),
    [setView, typeOf],
  )
  const removeFilters = useCallback(
    (cols: string[]) => setView((v) => ({ ...v, filters: v.filters.filter((f) => !cols.includes(f.column)) })),
    [setView],
  )
  const filterRows = useMemo(
    () => rowsPassing(result.rows, view.filters, result.columns),
    [result.rows, result.columns, view.filters],
  )

  const reset = useCallback(() => setView(() => defaultView(names)), [setView, names])

  return {
    table,
    view,
    setView,
    rows,
    columns,
    density,
    isDefault: isDefaultView(view, names),
    reset,
    totalRaw: result.rows.length,
    allColumns: result.columns,
    filters: view.filters,
    setColumnFilter,
    clearColumnFilter,
    clearFilters,
    showOnlyValue,
    hideColumnValue,
    removeFilters,
    filterRows,
  }
}
