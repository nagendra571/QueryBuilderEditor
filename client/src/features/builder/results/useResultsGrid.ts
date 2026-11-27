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
import {
  DEFAULT_COL_CAP,
  FIT_SAMPLE_ROWS,
  HEADER_FONT,
  cachedWidth,
  cellFont,
  createCanvasMeasure,
  fitWidth,
  type MeasureText,
} from './column-widths.ts'
import { displayOrder } from './grid-columns.ts'
import { compileFormat, formatText, isFormatted, widthKey, type ColumnFormat, type CompiledFormat } from './grid-format.ts'
import type { TotalFn } from './grid-totals.ts'
import { buildColumnDefs, tableBehaviour, type RowData } from './grid-table.ts'
import { removeFilter, rowsPassing, setFilter, showOnly, hideValue, valueKey, type GridFilter } from './grid-filters.ts'
import { carryOver, type GridView } from './grid-view.ts'
import { applySavedLayout, layoutPartsEqual, parseSavedLayout, shouldAdoptBaseline, toSavedLayout } from './saved-layout.ts'
import { headerExtra } from './header-width.ts'


function resolve<T>(updater: Updater<T>, current: T): T {
  return typeof updater === 'function' ? (updater as (old: T) => T)(current) : updater
}

export function useResultsGrid(result: QueryResultDto, savedLayout?: unknown) {
  const names = useMemo(() => result.columns.map((c) => c.name), [result.columns])
  const parsedLayout = useMemo(() => parseSavedLayout(savedLayout), [savedLayout])
  const baseline = useMemo(() => applySavedLayout(parsedLayout, names), [parsedLayout, names])

  const [state, setState] = useState(() => ({ result, savedLayout, baseline, view: baseline }))

  // A fresh run produces a new `result`, and a layout can arrive or change after the first render; both are
  // handled during render so the grid never paints a frame of the old view.
  let view = state.view
  if (state.result !== result || state.savedLayout !== savedLayout) {
    if (state.result !== result) {
      view = carryOver(
        state.view,
        state.result.columns.map((c) => c.name),
        names,
        baseline,
      )
    }
    if (state.savedLayout !== savedLayout && shouldAdoptBaseline(view, state.baseline, baseline)) {
      view = { ...baseline, sort: view.sort, search: view.search, filters: view.filters, density: view.density }
    }
    setState({ result, savedLayout, baseline, view })
  }

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

  // Per column, so a rule, bar or scale edit (which can't change text) measures nothing, and a number-format edit
  // measures only its own column.
  const defaultWidths = useMemo(() => {
    let measure: MeasureText | null = null
    const plain: Record<string, number> = {}
    const pinned: Record<string, number> = {}
    for (const c of result.columns) {
      const format = view.formats[c.name]
      const key = `${density}|${fontsReady}|${widthKey(format)}`
      const entry = cachedWidth(result.rows, c, key, () => {
        measure ??= createCanvasMeasure({ header: HEADER_FONT, cell: cellFont(density) })
        const formatted = isFormatted(format)
        const text = formatText(c, format)
        const values = result.rows.slice(0, FIT_SAMPLE_ROWS).map((r) => text(r[c.name]))
        const p = fitWidth(c.name, values, measure, headerExtra({ pinned: false, formatted }), DEFAULT_COL_CAP)
        return {
          plain: p,
          pinned: Math.max(p, fitWidth(c.name, [], measure, headerExtra({ pinned: true, formatted }), DEFAULT_COL_CAP)),
        }
      })
      plain[c.name] = entry.plain
      pinned[c.name] = entry.pinned
    }
    return { plain, pinned }
  }, [result, fontsReady, density, view.formats])

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

  const { order, hidden, pinnedLeft, pinnedRight } = view
  // Narrow on purpose: format edits and width drags must not rebuild the cell formats, totals input or store columns.
  const columns = useMemo(() => {
    const byName = new Map(result.columns.map((c) => [c.name, c]))
    return displayOrder({ order, hidden, pinnedLeft, pinnedRight }, names)
      .map((n) => byName.get(n))
      .filter((c): c is QueryResultColumnDto => !!c)
  }, [result.columns, names, order, hidden, pinnedLeft, pinnedRight])

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

  const setColumnFormat = useCallback(
    (column: string, next: ColumnFormat | null) =>
      setView((v) => {
        const formats = { ...v.formats }
        if (next) formats[column] = next
        else delete formats[column]
        return { ...v, formats }
      }),
    [setView],
  )
  const setTotals = useCallback(
    (on: boolean) => setView((v) => ({ ...v, totals: { ...v.totals, on } })),
    [setView],
  )
  const setTotalFn = useCallback(
    (column: string, fn: TotalFn) => setView((v) => ({ ...v, totals: { ...v.totals, fns: { ...v.totals.fns, [column]: fn } } })),
    [setView],
  )
  // Compiled over the rows that pass the column filters, so bars and colour scales span what the grid can show.
  const compiled = useCallback(
    (column: string): CompiledFormat =>
      compileFormat(filterRows, { name: column, dataType: typeOf(column) }, view.formats[column]),
    [filterRows, typeOf, view.formats],
  )

  const reset = useCallback(() => setView(() => baseline), [setView, baseline])
  const layoutDirty = !layoutPartsEqual(view, baseline)
  const currentLayout = useCallback(() => toSavedLayout(view), [view])
  const isDefault =
    !layoutDirty && view.sort === null && !view.search.trim() && view.filters.length === 0 && view.density === null

  return {
    table,
    view,
    setView,
    rows,
    columns,
    density,
    isDefault,
    baselineIsSaved: parsedLayout !== null,
    layoutDirty,
    currentLayout,
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
    formats: view.formats,
    setColumnFormat,
    setTotals,
    setTotalFn,
    compiled,
  }
}
