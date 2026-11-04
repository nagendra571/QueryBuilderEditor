import type { AggregateFunction, VisualizationOptions } from '@/types'
import { BLANK_LABEL, cellValue, compareCategories, formatCategory, toNumber } from './chart-data.ts'

export interface PivotCell {
  value: number | null
  notCombinable: boolean
}

/** rowTotals is the totals row (one entry per column), columnTotals the totals column (one per row). */
export interface PivotModel {
  kind: 'pivot'
  rowHeaders: string[][]
  columnHeaders: string[]
  values: string[]
  cells: PivotCell[][][]
  rowTotals: PivotCell[][] | null
  columnTotals: PivotCell[][] | null
  grandTotals: PivotCell[] | null
  hasNotCombinable: boolean
  skipped: number
  valueRanges: { min: number; max: number }[]
}

export const MAX_PIVOT_COLUMNS = 50

type Combine = 'add' | 'min' | 'max' | 'single'

/** Re-adding an average or a distinct count gives a number that is silently wrong, so those never combine. */
function combineFor(aggregate: AggregateFunction | null | undefined): Combine {
  if (aggregate === 'min') return 'min'
  if (aggregate === 'max') return 'max'
  if (aggregate === 'avg' || aggregate === 'countDistinct') return 'single'
  return 'add'
}

interface Acc {
  value: number | null
  n: number
}

function accumulate(acc: Acc, combine: Combine, x: number) {
  acc.n += 1
  if (acc.value === null || combine === 'single') acc.value = x
  else if (combine === 'add') acc.value += x
  else if (combine === 'min') acc.value = Math.min(acc.value, x)
  else acc.value = Math.max(acc.value, x)
}

const fresh = (count: number): Acc[] => Array.from({ length: count }, () => ({ value: null, n: 0 }))

function toCell(acc: Acc, combine: Combine, isTotal: boolean): PivotCell {
  if (combine === 'single') {
    if (isTotal || acc.n > 1) return { value: null, notCombinable: true }
    return { value: acc.n === 1 ? acc.value : null, notCombinable: false }
  }
  return { value: acc.n > 0 ? acc.value : null, notCombinable: false }
}

function compareHeader(a: string[], b: string[]): number {
  for (let i = 0; i < a.length; i++) {
    const x = a[i]
    const y = b[i]
    if (x === y) continue
    // The blank bucket sorts last at every level.
    if (x === BLANK_LABEL) return 1
    if (y === BLANK_LABEL) return -1
    const c = compareCategories(x, y)
    if (c !== 0) return c
    // Numerically equal but distinct text ("1", "1.0") must still order consistently, or a parent group splits.
    return x < y ? -1 : 1
  }
  return 0
}

interface RowBucket {
  header: string[]
  cells: Map<string, Acc[]>
  total: Acc[]
}

interface ColumnBucket {
  header: string
  total: Acc[]
}

export function buildPivot(
  options: VisualizationOptions,
  rows: Record<string, unknown>[],
  aggregateOf: (name: string) => AggregateFunction | null | undefined,
): PivotModel {
  const rowNames = options.pivot?.rows ?? []
  const columnName = options.pivot?.column || null
  const values = options.pivot?.values ?? []
  const withTotals = options.pivot?.totals ?? true
  const combines = values.map((v) => combineFor(aggregateOf(v)))

  const rowBuckets = new Map<string, RowBucket>()
  const columnBuckets = new Map<string, ColumnBucket>()
  const grand = fresh(values.length)
  let skipped = 0

  for (const row of rows) {
    const header = rowNames.map((n) => formatCategory(cellValue(row, n)))
    const rowKey = JSON.stringify(header)
    const columnHeader = columnName ? formatCategory(cellValue(row, columnName)) : ''
    const columnKey = JSON.stringify(columnHeader)

    let rowBucket = rowBuckets.get(rowKey)
    if (!rowBucket) {
      rowBucket = { header, cells: new Map(), total: fresh(values.length) }
      rowBuckets.set(rowKey, rowBucket)
    }
    let columnBucket = columnBuckets.get(columnKey)
    if (!columnBucket) {
      columnBucket = { header: columnHeader, total: fresh(values.length) }
      columnBuckets.set(columnKey, columnBucket)
    }
    let cell = rowBucket.cells.get(columnKey)
    if (!cell) {
      cell = fresh(values.length)
      rowBucket.cells.set(columnKey, cell)
    }

    for (let v = 0; v < values.length; v++) {
      const x = toNumber(cellValue(row, values[v]))
      if (x === 'bad') skipped += 1
      if (x === null || x === 'bad') continue
      accumulate(cell[v], combines[v], x)
      accumulate(rowBucket.total[v], combines[v], x)
      accumulate(columnBucket.total[v], combines[v], x)
      accumulate(grand[v], combines[v], x)
    }
  }

  const sortedRows = [...rowBuckets.entries()].sort((a, b) => compareHeader(a[1].header, b[1].header))
  const sortedColumns = columnName
    ? [...columnBuckets.entries()].sort((a, b) => compareHeader([a[1].header], [b[1].header]))
    : [...columnBuckets.entries()]
  if (!columnName && sortedColumns.length === 0) sortedColumns.push([JSON.stringify(''), { header: '', total: fresh(values.length) }])

  const toCells = (accs: Acc[], isTotal: boolean) => accs.map((a, v) => toCell(a, combines[v], isTotal))
  const empty = fresh(values.length)

  const cells = sortedRows.map(([, r]) => sortedColumns.map(([ck]) => toCells(r.cells.get(ck) ?? empty, false)))
  const columnTotals = withTotals && columnName ? sortedRows.map(([, r]) => toCells(r.total, true)) : null
  const rowTotals = withTotals ? sortedColumns.map(([, c]) => toCells(c.total, true)) : null
  const grandTotals = withTotals && columnName ? toCells(grand, true) : null

  const ranges = values.map(() => ({ min: Infinity, max: -Infinity }))
  let hasNotCombinable = false
  const scan = (cell: PivotCell, v: number, inRange: boolean) => {
    if (cell.notCombinable) hasNotCombinable = true
    if (inRange && cell.value !== null) {
      ranges[v].min = Math.min(ranges[v].min, cell.value)
      ranges[v].max = Math.max(ranges[v].max, cell.value)
    }
  }
  for (const r of cells) for (const c of r) c.forEach((cell, v) => scan(cell, v, true))
  for (const group of [columnTotals, rowTotals]) group?.forEach((r) => r.forEach((cell, v) => scan(cell, v, false)))
  grandTotals?.forEach((cell, v) => scan(cell, v, false))

  return {
    kind: 'pivot',
    rowHeaders: sortedRows.map(([, r]) => r.header),
    columnHeaders: sortedColumns.map(([, c]) => c.header),
    values,
    cells,
    rowTotals,
    columnTotals,
    grandTotals,
    hasNotCombinable,
    skipped,
    valueRanges: ranges.map((r) => (r.min === Infinity ? { min: 0, max: 0 } : r)),
  }
}

/** Position of a cell within its value's range, 0 to 1; null where shading would say nothing. */
export function shadeFraction(cell: PivotCell, range: { min: number; max: number }): number | null {
  if (cell.notCombinable || cell.value === null || range.max === range.min) return null
  return (cell.value - range.min) / (range.max - range.min)
}

export const PIVOT_TOO_WIDE = `Too many columns for a pivot table (max ${MAX_PIVOT_COLUMNS}); add filters or choose a different column.`

/** Ceiling for the shade mix: beyond it the number's contrast against the darkest shade gets thin in both themes. */
export const MAX_SHADE_PERCENT = 45
