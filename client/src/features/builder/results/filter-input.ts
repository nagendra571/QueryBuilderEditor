import type { ColumnDataType } from '@/types'
import { profileKind } from './column-profile.ts'
import type { FilterValue, GridFilter, ValueOption } from './grid-filters.ts'

const DAY_END_MS = 86_399_999

export type Parsed = { ok: true; value: number | null } | { ok: false }

export function parseBound(text: string, dataType: ColumnDataType, end: boolean): Parsed {
  const t = text.trim()
  if (t === '') return { ok: true, value: null }
  if (profileKind(dataType) === 'number') {
    const n = Number(t)
    return Number.isFinite(n) ? { ok: true, value: n } : { ok: false }
  }
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(t)
  if (!m) return { ok: false }
  const ms = Date.UTC(Number(m[1]), Number(m[2]) - 1, Number(m[3]))
  if (Number.isNaN(ms)) return { ok: false }
  // A dateTime "To" day includes the whole day; a date column has no time part to include.
  return { ok: true, value: dataType === 'dateTime' && end ? ms + DAY_END_MS : ms }
}

export function boundInputText(value: number | null, dataType: ColumnDataType): string {
  if (value === null) return ''
  return profileKind(dataType) === 'number' ? String(value) : new Date(value).toISOString().slice(0, 10)
}

export type RangeProblem = 'invalid' | 'order' | null

export function rangeProblem(from: string, to: string, dataType: ColumnDataType): RangeProblem {
  const a = parseBound(from, dataType, false)
  const b = parseBound(to, dataType, true)
  if (!a.ok || !b.ok) return 'invalid'
  if (a.value !== null && b.value !== null && a.value > b.value) return 'order'
  return null
}

/** With nothing typed and empties not kept, a range would remove the filter; Clear is the way to do that. */
export function canApplyRange(from: string, to: string, includeEmpty: boolean): boolean {
  return includeEmpty || from.trim() !== '' || to.trim() !== ''
}

export function buildRangeFilter(
  column: string,
  from: string,
  to: string,
  includeEmpty: boolean,
  dataType: ColumnDataType,
): GridFilter | null {
  const a = parseBound(from, dataType, false)
  const b = parseBound(to, dataType, true)
  if (!a.ok || !b.ok) return null
  return { kind: 'range', column, min: a.value, max: b.value, includeEmpty }
}

export function initialTicks(options: ValueOption[], filter: GridFilter | undefined): Set<FilterValue> {
  if (!filter || filter.kind !== 'values') return new Set(options.map((o) => o.value))
  const listed = new Set(filter.values)
  return new Set(options.filter((o) => (filter.mode === 'include') === listed.has(o.value)).map((o) => o.value))
}

/**
 * null = every listed value ticked and nothing carried over, i.e. no filter. Values in the existing filter that are not
 * listed (past the cap, or removed by other columns' filters) are carried forward so Apply never silently drops them.
 */
export function filterFromTicks(
  column: string,
  options: ValueOption[],
  ticked: Set<FilterValue>,
  existing?: GridFilter,
  truncated = false,
  clearedInSession = false,
): GridFilter | null {
  const listed = new Set(options.map((o) => o.value))
  const unlisted = existing?.kind === 'values' ? existing.values.filter((v) => !listed.has(v)) : []
  const carriedExclude = existing?.kind === 'values' && existing.mode === 'exclude' ? unlisted : []
  const carriedInclude = existing?.kind === 'values' && existing.mode === 'include' ? unlisted : []
  const on = options.filter((o) => ticked.has(o.value)).map((o) => o.value)
  // An include filter that can't see every value must stay an include, or flipping would admit what it kept out.
  if (existing?.kind === 'values' && existing.mode === 'include' && (carriedInclude.length > 0 || truncated)) {
    return { kind: 'values', column, mode: 'include', values: [...new Set([...on, ...carriedInclude])] }
  }
  if (on.length === options.length && carriedExclude.length === 0) return null
  // With a cut-off list, "fewer than half" says nothing about the values that are not listed, so the default keeps them.
  const include = truncated ? clearedInSession : on.length < options.length / 2
  if (include) return { kind: 'values', column, mode: 'include', values: [...on, ...carriedInclude] }
  const off = options.filter((o) => !ticked.has(o.value)).map((o) => o.value)
  return { kind: 'values', column, mode: 'exclude', values: [...off, ...carriedExclude] }
}
