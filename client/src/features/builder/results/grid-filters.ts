import { toNumber } from '../../visualizations/chart-data.ts'
import { parseDateValue, profileKind } from './column-profile.ts'
import type { ColumnDataType, QueryResultColumnDto } from '@/types'

export type FilterValue = string | number | boolean | null
export type GridFilter =
  | { kind: 'values'; column: string; mode: 'include' | 'exclude'; values: FilterValue[] }
  | { kind: 'range'; column: string; min: number | null; max: number | null; includeEmpty: boolean }
export type FilterUi = 'values' | 'range'

const EMPTY_LABEL = '(empty)'

export function filterUi(dataType: ColumnDataType): FilterUi {
  const kind = profileKind(dataType)
  return kind === 'number' || kind === 'date' ? 'range' : 'values'
}

// null and '' are one value: a blank text cell and a NULL look the same to a business user.
export function valueKey(raw: unknown, dataType: ColumnDataType): FilterValue {
  if (raw === null || raw === undefined || raw === '') return null
  if (profileKind(dataType) === 'number') {
    const n = toNumber(raw)
    if (typeof n === 'number') return n
    return String(raw)
  }
  if (typeof raw === 'boolean') return raw
  return String(raw)
}

export function matchesFilter(raw: unknown, filter: GridFilter, dataType: ColumnDataType): boolean {
  if (filter.kind === 'values') {
    const hit = filter.values.includes(valueKey(raw, dataType))
    return filter.mode === 'include' ? hit : !hit
  }
  if (raw === null || raw === undefined || raw === '') return filter.includeEmpty
  const kind = profileKind(dataType)
  let n: number | null
  if (kind === 'date') n = parseDateValue(raw)
  else {
    const t = toNumber(raw)
    n = typeof t === 'number' ? t : null
  }
  if (n === null) return false
  if (filter.min !== null && n < filter.min) return false
  if (filter.max !== null && n > filter.max) return false
  return true
}

export function isEmptyFilter(f: GridFilter): boolean {
  if (f.kind === 'values') return f.mode === 'include' && f.values.length === 0
  return f.min === null && f.max === null && !f.includeEmpty
}

export function setFilter(filters: GridFilter[], next: GridFilter): GridFilter[] {
  if (isEmptyFilter(next)) return removeFilter(filters, next.column)
  const i = filters.findIndex((f) => f.column === next.column)
  if (i < 0) return [...filters, next]
  return filters.map((f, j) => (j === i ? next : f))
}

export function removeFilter(filters: GridFilter[], column: string): GridFilter[] {
  return filters.filter((f) => f.column !== column)
}

export function showOnly(filters: GridFilter[], column: string, value: FilterValue): GridFilter[] {
  return setFilter(filters, { kind: 'values', column, mode: 'include', values: [value] })
}

export function hideValue(filters: GridFilter[], column: string, value: FilterValue): GridFilter[] {
  const existing = filters.find((f) => f.column === column)
  if (existing && existing.kind === 'values' && existing.mode === 'exclude') {
    if (existing.values.includes(value)) return filters
    return setFilter(filters, { ...existing, values: [...existing.values, value] })
  }
  return setFilter(filters, { kind: 'values', column, mode: 'exclude', values: [value] })
}

export interface ValueOption {
  value: FilterValue
  label: string
  count: number
}

function labelOf(value: FilterValue): string {
  return value === null ? EMPTY_LABEL : String(value)
}

export function valueOptions(
  rows: Record<string, unknown>[],
  column: QueryResultColumnDto,
  cap = 1000,
): { options: ValueOption[]; truncated: boolean } {
  const counts = new Map<FilterValue, number>()
  for (const row of rows) {
    const key = valueKey(row[column.name], column.dataType)
    counts.set(key, (counts.get(key) ?? 0) + 1)
  }
  const all: ValueOption[] = [...counts].map(([value, count]) => ({ value, label: labelOf(value), count }))
  all.sort((a, b) => b.count - a.count || a.label.localeCompare(b.label, undefined, { numeric: true }))
  return { options: all.slice(0, cap), truncated: all.length > cap }
}

export function rowsPassing(
  rows: Record<string, unknown>[],
  filters: GridFilter[],
  columns: QueryResultColumnDto[],
  exceptColumn?: string,
): Record<string, unknown>[] {
  const active = filters.filter((f) => f.column !== exceptColumn)
  if (active.length === 0) return rows
  const types = new Map(columns.map((c) => [c.name, c.dataType]))
  return rows.filter((row) =>
    active.every((f) => matchesFilter(row[f.column], f, types.get(f.column) ?? 'unknown')),
  )
}

function boundText(n: number): string {
  return new Date(n).toISOString().slice(0, 10)
}

export function chipText(f: GridFilter, dataType: ColumnDataType): { column: string; text: string; title: string } {
  if (f.kind === 'values') {
    const labels = f.values.map(labelOf)
    const prefix = f.mode === 'include' ? 'is' : 'is not'
    const title = `${prefix} ${labels.join(', ')}`
    const text =
      labels.length > 2 ? `${prefix} ${labels.slice(0, 2).join(', ')} +${labels.length - 2} more` : title
    return { column: f.column, text, title }
  }
  const isDate = profileKind(dataType) === 'date'
  const show = (n: number) => (isDate ? boundText(n) : String(n))
  let core: string
  if (f.min !== null && f.max !== null) core = `${show(f.min)} – ${show(f.max)}`
  else if (f.min !== null) core = `≥ ${show(f.min)}`
  else if (f.max !== null) core = `≤ ${show(f.max)}`
  else core = EMPTY_LABEL.slice(1, -1)
  const text = f.includeEmpty && (f.min !== null || f.max !== null) ? `${core} + empty` : core
  return { column: f.column, text, title: text }
}
