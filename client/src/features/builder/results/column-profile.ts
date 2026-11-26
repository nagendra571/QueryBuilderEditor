import { toNumber } from '../../visualizations/chart-data.ts'
import type { ColumnDataType, QueryResultColumnDto } from '@/types'

export type ProfileKind = 'number' | 'date' | 'text' | 'boolean'

export function profileKind(dataType: ColumnDataType): ProfileKind {
  switch (dataType) {
    case 'number':
      return 'number'
    case 'date':
    case 'dateTime':
      return 'date'
    case 'boolean':
      return 'boolean'
    default:
      return 'text'
  }
}

export interface Bin {
  from: number
  to: number
  count: number
}

export function histogram(values: number[], binCount = 20): Bin[] {
  if (values.length === 0) return []
  let min = values[0]
  let max = values[0]
  for (const v of values) {
    if (v < min) min = v
    if (v > max) max = v
  }
  if (min === max) return [{ from: min, to: max, count: values.length }]
  const width = (max - min) / binCount
  const bins: Bin[] = Array.from({ length: binCount }, (_, i) => ({
    from: min + i * width,
    to: i === binCount - 1 ? max : min + (i + 1) * width,
    count: 0,
  }))
  for (const v of values) {
    // The last bin includes max.
    const i = Math.min(binCount - 1, Math.floor((v - min) / width))
    bins[i].count++
  }
  return bins
}

interface ProfileBase {
  rows: number
  empty: number
  distinct: number
  unreadable: number
}
export interface NumberProfile extends ProfileBase {
  kind: 'number'
  min: number | null
  max: number | null
  mean: number | null
  median: number | null
  sum: number | null
  showSum: boolean
  bins: Bin[]
}
export interface DateProfile extends ProfileBase {
  kind: 'date'
  earliest: number | null
  latest: number | null
  bins: Bin[]
}
export interface TextProfile extends ProfileBase {
  kind: 'text'
  shortest: number | null
  longest: number | null
  top: { value: string; count: number }[]
  other: number
}
export interface BooleanProfile extends ProfileBase {
  kind: 'boolean'
  yes: number
  no: number
}
export type ColumnProfile = NumberProfile | DateProfile | TextProfile | BooleanProfile

const NO_SUM = new Set(['avg', 'min', 'max', 'countDistinct'])

export function profileColumn(
  rows: Record<string, unknown>[],
  column: QueryResultColumnDto,
  binCount = 20,
): ColumnProfile {
  const kind = profileKind(column.dataType)
  let empty = 0
  let unreadable = 0
  const base = () => ({ rows: rows.length, empty, unreadable })

  if (kind === 'number' || kind === 'date') {
    const nums: number[] = []
    for (const row of rows) {
      const v = row[column.name]
      if (v === null || v === undefined) {
        empty++
        continue
      }
      if (kind === 'number') {
        const n = toNumber(v)
        if (typeof n === 'number') nums.push(n)
        else unreadable++
      } else {
        const t = v instanceof Date ? v.getTime() : typeof v === 'string' ? Date.parse(v) : NaN
        if (Number.isNaN(t)) unreadable++
        else nums.push(t)
      }
    }
    const distinct = new Set(nums).size
    const bins = histogram(nums, binCount)
    let min: number | null = null
    let max: number | null = null
    for (const n of nums) {
      if (min === null || n < min) min = n
      if (max === null || n > max) max = n
    }
    if (kind === 'date') return { kind, ...base(), distinct, earliest: min, latest: max, bins }
    const showSum = !NO_SUM.has(column.aggregate ?? '')
    const total = nums.reduce((a, b) => a + b, 0)
    const sorted = [...nums].sort((a, b) => a - b)
    const mid = sorted.length >> 1
    const median = sorted.length === 0 ? null : sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2
    return {
      kind,
      ...base(),
      distinct,
      min,
      max,
      mean: nums.length ? total / nums.length : null,
      median,
      sum: showSum && nums.length ? total : null,
      showSum,
      bins,
    }
  }

  if (kind === 'boolean') {
    let yes = 0
    let no = 0
    for (const row of rows) {
      const v = row[column.name]
      if (v === null || v === undefined) empty++
      else if (v === true) yes++
      else if (v === false) no++
      else unreadable++
    }
    return { kind, ...base(), distinct: (yes > 0 ? 1 : 0) + (no > 0 ? 1 : 0), yes, no }
  }

  const counts = new Map<string, number>()
  let shortest: number | null = null
  let longest: number | null = null
  let readable = 0
  for (const row of rows) {
    const v = row[column.name]
    if (v === null || v === undefined || v === '') {
      empty++
      continue
    }
    const s = String(v)
    readable++
    counts.set(s, (counts.get(s) ?? 0) + 1)
    if (shortest === null || s.length < shortest) shortest = s.length
    if (longest === null || s.length > longest) longest = s.length
  }
  const top = [...counts.entries()]
    .map(([value, count]) => ({ value, count }))
    .sort((a, b) => b.count - a.count || a.value.localeCompare(b.value))
    .slice(0, 10)
  const other = readable - top.reduce((a, t) => a + t.count, 0)
  return { kind: 'text', ...base(), distinct: counts.size, shortest, longest, top, other }
}
