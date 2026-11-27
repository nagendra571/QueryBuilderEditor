import type { Density } from '../../../state/density-store.ts'
import type { GridFilter } from './grid-filters.ts'
import type { ColumnFormat } from './grid-format.ts'
import type { TotalFn } from './grid-totals.ts'

export interface GridSort {
  column: string
  direction: 'asc' | 'desc'
}

export interface GridTotals {
  on: boolean
  fns: Record<string, TotalFn>
}

export interface GridView {
  order: string[]
  hidden: string[]
  pinnedLeft: string[]
  pinnedRight: string[]
  widths: Record<string, number>
  sort: GridSort | null
  search: string
  density: Density | null
  wrap: boolean
  filters: GridFilter[]
  formats: Record<string, ColumnFormat>
  totals: GridTotals
}

export function defaultView(columns: string[]): GridView {
  return {
    order: [...columns],
    hidden: [],
    pinnedLeft: [],
    pinnedRight: [],
    widths: {},
    sort: null,
    search: '',
    density: null,
    wrap: false,
    filters: [],
    formats: {},
    totals: { on: false, fns: {} },
  }
}

function sameSet(a: string[], b: string[]): boolean {
  if (a.length !== b.length) return false
  const set = new Set(a)
  return set.size === new Set(b).size && b.every((n) => set.has(n))
}

// Any change in the column set resets everything: a stale name in a sort or pin
// would point at a column that no longer exists.
export function carryOver(
  prev: GridView,
  prevColumns: string[],
  nextColumns: string[],
  baseline: GridView = defaultView(nextColumns),
): GridView {
  if (!sameSet(prevColumns, nextColumns)) return baseline
  return {
    order: [...prev.order],
    hidden: [...prev.hidden],
    pinnedLeft: [...prev.pinnedLeft],
    pinnedRight: [...prev.pinnedRight],
    widths: { ...prev.widths },
    sort: prev.sort ? { ...prev.sort } : null,
    search: prev.search,
    density: prev.density,
    wrap: prev.wrap,
    filters: prev.filters.map((f) => ({ ...f })),
    formats: Object.fromEntries(
      Object.entries(prev.formats).map(([name, f]) => [
        name,
        {
          ...f,
          ...(f.number ? { number: { ...f.number } } : {}),
          ...(f.date ? { date: { ...f.date } } : {}),
          rules: f.rules.map((r) => ({ ...r })),
        },
      ]),
    ),
    totals: { on: prev.totals.on, fns: { ...prev.totals.fns } },
  }
}

export function isDefaultView(v: GridView, columns: string[]): boolean {
  return (
    v.hidden.length === 0 &&
    v.pinnedLeft.length === 0 &&
    v.pinnedRight.length === 0 &&
    Object.keys(v.widths).length === 0 &&
    v.sort === null &&
    !v.search.trim() &&
    v.density === null &&
    !v.wrap &&
    v.filters.length === 0 &&
    Object.keys(v.formats).length === 0 &&
    !v.totals.on &&
    v.order.length === columns.length &&
    v.order.every((n, i) => n === columns[i])
  )
}

export function nextSort(current: GridSort | null, column: string): GridSort | null {
  if (!current || current.column !== column) return { column, direction: 'asc' }
  if (current.direction === 'asc') return { column, direction: 'desc' }
  return null
}
