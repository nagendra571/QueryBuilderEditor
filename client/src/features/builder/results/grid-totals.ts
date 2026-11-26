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

export type TotalRaw = { kind: 'count'; n: number } | { kind: 'number'; value: number } | { kind: 'date'; raw: unknown }

const rawCache = new WeakMap<readonly Record<string, unknown>[], Map<string, TotalRaw | null>>()

// The O(rows) pass, cached per (rows, column, fn) so format edits and width drags only re-format the result.
export function totalRaw(
  rows: readonly Record<string, unknown>[],
  column: QueryResultColumnDto,
  fn: TotalFn,
): TotalRaw | null {
  if (fn === 'none') return null
  let byKey = rawCache.get(rows)
  if (!byKey) rawCache.set(rows, (byKey = new Map()))
  const key = `${column.name}|${column.dataType}|${fn}`
  if (byKey.has(key)) return byKey.get(key) ?? null
  const result = computeTotal(rows, column.name, fn)
  byKey.set(key, result)
  return result
}

function computeTotal(rows: readonly Record<string, unknown>[], name: string, fn: Exclude<TotalFn, 'none'>): TotalRaw | null {
  if (fn === 'count' || fn === 'distinct') {
    let count = 0
    const seen = new Set<unknown>()
    for (const row of rows) {
      const v = row[name]
      if (isEmpty(v)) continue
      count++
      if (fn === 'distinct') seen.add(v)
    }
    return { kind: 'count', n: fn === 'count' ? count : seen.size }
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
    return bestTime === null ? null : { kind: 'date', raw: best }
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
    if (!Number.isInteger(num)) decimals = Math.max(decimals, decimalsOf(num))
  }
  if (n === 0) return null

  const raw = fn === 'sum' ? sum : fn === 'avg' ? sum / n : fn === 'min' ? min : max
  // Round to the places the inputs carry so float noise (0.1 + 0.2) never shows;
  // an average can legitimately need a few more.
  const places = Math.min(100, fn === 'avg' ? decimals + 4 : decimals)
  return { kind: 'number', value: Number(raw.toFixed(places)) }
}

export function formatTotal(raw: TotalRaw | null, text: (v: unknown) => string, locale?: string): string {
  if (!raw) return ''
  switch (raw.kind) {
    case 'count':
      return raw.n.toLocaleString(locale)
    case 'number':
      return text(raw.value)
    default:
      return text(raw.raw)
  }
}

export function totalValue(
  rows: readonly Record<string, unknown>[],
  column: QueryResultColumnDto,
  fn: TotalFn,
  text: (v: unknown) => string,
): string {
  return formatTotal(totalRaw(rows, column, fn), text)
}

export function withAggregates(
  columns: QueryResultColumnDto[],
  definition: { columns: QueryColumn[] } | undefined,
): QueryResultColumnDto[] {
  if (!definition) return columns
  const byName = new Map<string, QueryColumn | null>()
  for (const c of definition.columns) {
    if (!c.isVisible) continue
    // Same rule as the server's EffectiveAlias: a blank alias names the column by ColumnName.
    const key = c.alias?.trim() ? c.alias : c.columnName
    byName.set(key, byName.has(key) ? null : c)
  }
  return columns.map((col) => {
    const match = byName.get(col.name)
    return match ? { ...col, aggregate: match.aggregate } : col
  })
}
