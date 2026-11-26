import { useCallback, useMemo, useState } from 'react'
import {
  getCoreRowModel,
  getFilteredRowModel,
  getSortedRowModel,
  useReactTable,
  type ColumnDef,
  type ColumnSizingState,
  type Row,
  type SortingState,
  type Table,
  type Updater,
} from '@tanstack/react-table'
import type { QueryResultColumnDto, QueryResultDto } from '@/types'
import { useDensityStore, type Density } from '../../../state/density-store.ts'
import { compareCells } from './compare-cells.ts'
import {
  DEFAULT_COL_CAP,
  FIT_SAMPLE_ROWS,
  MAX_COL_WIDTH,
  MIN_COL_WIDTH,
  createCanvasMeasure,
  fitWidth,
} from './column-widths.ts'
import { displayOrder, displayString, matchesSearch } from './grid-columns.ts'
import { carryOver, defaultView, isDefaultView, type GridView } from './grid-view.ts'

type RowData = Record<string, unknown>

// Header padding 20 + type badge ~36 + sort mark ~16 + menu button ~24.
const HEADER_EXTRA = 96

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

  const setView = useCallback((update: (v: GridView) => GridView) => {
    setState((s) => ({ ...s, view: update(s.view) }))
  }, [])

  const defaultWidths = useMemo(() => {
    const measure = createCanvasMeasure({
      header: '600 12px "Public Sans Variable"',
      cell: '400 13px "Public Sans Variable"',
    })
    const out: Record<string, number> = {}
    for (const c of result.columns) {
      const values = result.rows.slice(0, FIT_SAMPLE_ROWS).map((r) => displayString(r[c.name]))
      out[c.name] = fitWidth(c.name, values, measure, HEADER_EXTRA, DEFAULT_COL_CAP)
    }
    return out
  }, [result])

  const columnDefs = useMemo<ColumnDef<RowData>[]>(
    () =>
      result.columns.map((c) => ({
        id: c.name,
        accessorFn: (row) => row[c.name],
        minSize: MIN_COL_WIDTH,
        maxSize: MAX_COL_WIDTH,
        enableMultiSort: false,
        sortDescFirst: false,
        sortUndefined: false,
        sortingFn: (a, b, id) => compareCells(a.getValue(id), b.getValue(id), c.dataType === 'number'),
      })),
    [result.columns],
  )

  const sizing = useMemo<ColumnSizingState>(
    () => Object.fromEntries(names.map((n) => [n, view.widths[n] ?? defaultWidths[n]])),
    [names, view.widths, defaultWidths],
  )

  const sorting = useMemo<SortingState>(
    () => (view.sort ? [{ id: view.sort.column, desc: view.sort.direction === 'desc' }] : []),
    [view.sort],
  )

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
      globalFilter: view.search,
    },
    enableMultiSort: false,
    sortDescFirst: false,
    getColumnCanGlobalFilter: (column) => column.getIsVisible(),
    globalFilterFn: (row, columnId, filterValue) => matchesSearch(row.getValue(columnId), String(filterValue ?? '')),
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
          if (next[n] !== undefined && next[n] !== (v.widths[n] ?? defaultWidths[n])) widths[n] = next[n]
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

  const appDensity = useDensityStore((s) => s.density)
  const density: Density = view.density ?? appDensity

  const rows: Row<RowData>[] = table.getRowModel().rows

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
  }
}
