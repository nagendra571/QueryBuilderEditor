import type { QueryColumn, QueryResultColumnDto } from '@/types'
import { parseDateValue, profileKind } from './column-profile.ts'
import { decimalsOf } from './selection-totals.ts'

export type TotalFn = 'sum' | 'avg' | 'count' | 'distinct' | 'min' | 'max' | 'earliest' | 'latest' | 'none'

export const TOTAL_LABEL: Record<TotalFn, string> = {
  sum: 'SUM',
  avg: 'AVG',
  count: 'COUNT',
  distinct: 'DISTINCT',
  min: 'MIN',
  max: 'MAX',
  earliest: 'EARLIEST',
  latest: 'LATEST',
  none: '',
}

// A column the query already aggregated: adding those up again would be wrong
// for everything except a sum or a count.
function summable(column: QueryResultColumnDto): boolean {
  const a = column.aggregate
  return !a || a === 'none' || a === 'sum' || a === 'count'
}

export function totalChoices(column: QueryResultColumnDto): TotalFn[] {
  switch (profileKind(column.dataType)) {
    case 'number':
      return summable(column)
        ? ['sum', 'avg', 'count', 'min', 'max', 'none']
        : ['avg', 'count', 'min', 'max', 'none']
    case 'date':
      return ['earliest', 'latest', 'count', 'none']
    default:
      return ['count', 'distinct', 'none']
  }
}

export function defaultTotal(column: QueryResultColumnDto): TotalFn {
  switch (profileKind(column.dataType)) {
    case 'number':
      return summable(column) ? 'sum' : 'none'
    case 'date':
      return 'latest'
    default:
      return 'count'
  }
}

function isEmpty(v: unknown): boolean {
  return v === null || v === undefined || v === ''
}

// Only real numbers count: numeric text in a number column is not a number
// (it may be a value beyond double precision that would add up silently wrong).
function numberOf(v: unknown): number | null {
  return typeof v === 'number' && Number.isFinite(v) ? v : null
}

export function totalValue(
  rows: readonly Record<string, unknown>[],
  column: QueryResultColumnDto,
  fn: TotalFn,
  text: (v: unknown) => string,
): string {
  if (fn === 'none') return ''
  const name = column.name

  if (fn === 'count' || fn === 'distinct') {
    let count = 0
    const seen = new Set<unknown>()
    for (const row of rows) {
      const v = row[name]
      if (isEmpty(v)) continue
      count++
      if (fn === 'distinct') seen.add(v)
    }
    return String(fn === 'count' ? count : seen.size)
  }

  if (fn === 'earliest' || fn === 'latest') {
    let best: unknown
    let bestTime: number | null = null
    for (const row of rows) {
      const v = row[name]
      const t = parseDateValue(v)
      if (t === null) continue
      if (bestTime === null || (fn === 'earliest' ? t < bestTime : t > bestTime)) {
        bestTime = t
        best = v
      }
    }
    return bestTime === null ? '' : text(best)
  }

  let n = 0
  let sum = 0
  let min = Infinity
  let max = -Infinity
  let decimals = 0
  for (const row of rows) {
    const num = numberOf(row[name])
    if (num === null) continue
    n++
    sum += num
    if (num < min) min = num
    if (num > max) max = num
    decimals = Math.max(decimals, decimalsOf(num))
  }
  if (n === 0) return ''

  const raw = fn === 'sum' ? sum : fn === 'avg' ? sum / n : fn === 'min' ? min : max
  // Round to the places the inputs carry so float noise (0.1 + 0.2) never shows;
  // an average can legitimately need a few more.
  const places = Math.min(100, fn === 'avg' ? decimals + 4 : decimals)
  return text(Number(raw.toFixed(places)))
}

export function withAggregates(
  columns: QueryResultColumnDto[],
  definition: { columns: QueryColumn[] } | undefined,
): QueryResultColumnDto[] {
  if (!definition) return columns
  const byName = new Map<string, QueryColumn | null>()
  for (const c of definition.columns) {
    if (!c.isVisible) continue
    const key = c.alias || c.columnName
    byName.set(key, byName.has(key) ? null : c)
  }
  return columns.map((col) => {
    const match = byName.get(col.name)
    return match ? { ...col, aggregate: match.aggregate } : col
  })
}
