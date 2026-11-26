import type { ColumnDataType } from '@/types'
import { parseDateValue } from './column-profile.ts'
import { displayString } from './grid-columns.ts'

export type NumberStyle = 'auto' | 'number' | 'compact' | 'currency' | 'percent'
export interface NumberFormat {
  style: NumberStyle
  decimals: number | null
  currency: string
  thousands: boolean
  fraction: boolean
}
export type DatePattern = 'auto' | 'weekday' | 'medium' | 'short' | 'iso' | 'monthYear' | 'year'
export interface DateFormat {
  pattern: DatePattern
  showTime: boolean
}
export type RuleColor = 'red' | 'amber' | 'green' | 'blue'
export type RuleOp = 'gt' | 'lt' | 'between' | 'eq' | 'is' | 'contains' | 'empty' | 'before' | 'after'
export interface HighlightRule {
  id: string
  op: RuleOp
  value: string
  value2: string
  color: RuleColor
}
export type ScaleKind = 'blue' | 'redGreen'
export interface ColumnFormat {
  number?: NumberFormat
  date?: DateFormat
  bars?: boolean
  scale?: ScaleKind | null
  rules: HighlightRule[]
}

export const MAX_RULES = 5
export const defaultNumberFormat: NumberFormat = { style: 'auto', decimals: null, currency: 'USD', thousands: true, fraction: false }
export const defaultDateFormat: DateFormat = { pattern: 'auto', showTime: false }

export function rulesFor(dataType: ColumnDataType): RuleOp[] {
  switch (dataType) {
    case 'number':
      return ['gt', 'lt', 'between', 'eq']
    case 'date':
    case 'dateTime':
      return ['before', 'after', 'between']
    case 'boolean':
      return []
    default:
      return ['is', 'contains', 'empty']
  }
}

const numFormats = new Map<string, Intl.NumberFormat>()
const dateFormats = new Map<string, Intl.DateTimeFormat>()

// Building an Intl formatter costs far more than using one, and a grid formats thousands of cells.
function numFmt(locale: string | undefined, o: Intl.NumberFormatOptions): Intl.NumberFormat {
  const key = `${locale ?? ''}|${JSON.stringify(o)}`
  let f = numFormats.get(key)
  if (!f) numFormats.set(key, (f = new Intl.NumberFormat(locale, o)))
  return f
}

function dateFmt(locale: string | undefined, o: Intl.DateTimeFormatOptions): Intl.DateTimeFormat {
  const key = `${locale ?? ''}|${JSON.stringify(o)}`
  let f = dateFormats.get(key)
  if (!f) dateFormats.set(key, (f = new Intl.DateTimeFormat(locale, o)))
  return f
}

// Intl throws RangeError on a malformed currency code; a bad saved format must still render a number.
function currencyCode(f: NumberFormat): string | null {
  const code = f.currency?.trim().toUpperCase()
  return code && /^[A-Z]{3}$/.test(code) ? code : null
}

export function formatNumber(n: number, f: NumberFormat, locale?: string): string {
  if (f.style === 'auto') return n.toLocaleString(locale)
  const useGrouping = f.thousands ? undefined : false
  const d = f.decimals ?? undefined
  const pinned = d === undefined ? {} : { minimumFractionDigits: d, maximumFractionDigits: d }
  switch (f.style) {
    case 'compact':
      return numFmt(locale, { notation: 'compact', maximumFractionDigits: d ?? 1, useGrouping }).format(n)
    case 'currency': {
      const currency = currencyCode(f)
      if (currency) {
        try {
          return numFmt(locale, { style: 'currency', currency, useGrouping, ...pinned }).format(n)
        } catch {
          // fall through to the plain number
        }
      }
      return formatNumber(n, { ...f, style: 'number' }, locale)
    }
    case 'percent':
      // Chart parity: 42 means 42%, so Intl's own x100 only applies when the data holds fractions.
      return numFmt(locale, {
        style: 'percent',
        useGrouping,
        minimumFractionDigits: d ?? 0,
        maximumFractionDigits: d ?? 2,
      }).format(f.fraction ? n : n / 100)
    default: {
      const places = d ?? (Number.isInteger(n) ? 0 : 2)
      return numFmt(locale, { useGrouping, minimumFractionDigits: places, maximumFractionDigits: places }).format(n)
    }
  }
}

const HAS_TIME = /[T ]\d{2}:\d{2}/
const HAS_ZONE = /(Z|[+-]\d{2}(:?\d{2})?)$/i

// Zoneless and date-only text is a wall-clock value (shown in UTC so the date never shifts); an explicit zone
// or a Date object is an instant, shown in the viewer's zone.
function isWallClock(raw: unknown): boolean {
  if (typeof raw !== 'string') return false
  const s = raw.trim()
  return !HAS_TIME.test(s) || !HAS_ZONE.test(s)
}

const pad = (n: number) => String(n).padStart(2, '0')

export function formatDate(raw: unknown, dataType: 'date' | 'dateTime', f: DateFormat, locale?: string): string | null {
  const t = parseDateValue(raw)
  if (t === null) return null
  if (f.pattern === 'auto') return displayString(raw)
  const utc = isWallClock(raw)
  const timeZone = utc ? 'UTC' : undefined
  const d = new Date(t)
  const y = utc ? d.getUTCFullYear() : d.getFullYear()
  const m = (utc ? d.getUTCMonth() : d.getMonth()) + 1
  const day = utc ? d.getUTCDate() : d.getDate()

  let text: string
  switch (f.pattern) {
    case 'weekday':
      text = dateFmt(locale, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone }).format(d)
      break
    case 'short':
      text = dateFmt(locale, { day: '2-digit', month: '2-digit', year: 'numeric', timeZone }).format(d)
      break
    case 'iso':
      text = `${y}-${pad(m)}-${pad(day)}`
      break
    case 'monthYear':
      text = dateFmt(locale, { month: 'short', year: 'numeric', timeZone }).format(d)
      break
    case 'year':
      text = String(y)
      break
    default:
      text = dateFmt(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone }).format(d)
  }
  if (f.showTime && dataType === 'dateTime') {
    text += ` ${dateFmt(locale, { hour: '2-digit', minute: '2-digit', timeZone }).format(d)}`
  }
  return text
}

export type RuleProblem = 'incomplete' | 'notNumber'

const NUMBER_OPS: ReadonlySet<RuleOp> = new Set(['gt', 'lt', 'between', 'eq'])

// "15,000" or "$100" would otherwise make a rule that silently matches nothing.
export function ruleProblem(rule: HighlightRule, dataType: ColumnDataType): RuleProblem | null {
  if (!isComplete(rule)) return 'incomplete'
  if (dataType === 'number' && NUMBER_OPS.has(rule.op)) {
    const bad = (v: string) => v.trim() !== '' && !Number.isFinite(Number(v.trim()))
    if (bad(rule.value) || (rule.op === 'between' && bad(rule.value2))) return 'notNumber'
  }
  return null
}

export function isComplete(rule: HighlightRule): boolean {
  if (rule.op === 'empty') return true
  if (rule.value.trim() === '') return false
  return rule.op !== 'between' || rule.value2.trim() !== ''
}

export function ruleLabel(column: string, rule: HighlightRule, fmt: (v: string) => string): string {
  const a = fmt(rule.value)
  switch (rule.op) {
    case 'gt':
      return `${column} > ${a}`
    case 'lt':
      return `${column} < ${a}`
    case 'between':
      return `${column} between ${a} and ${fmt(rule.value2)}`
    case 'eq':
      return `${column} = ${a}`
    case 'is':
      return `${column} is ${a}`
    case 'contains':
      return `${column} contains ${a}`
    case 'empty':
      return `${column} is empty`
    case 'before':
      return `${column} before ${a}`
    default:
      return `${column} after ${a}`
  }
}

export interface CellLook {
  text: string
  title: string | null
  bar: { from: number; to: number; negative: boolean } | null
  scale: { kind: ScaleKind; t: number } | null
  highlight: RuleColor | null
}
export interface CompiledFormat {
  formatted: boolean
  look(value: unknown): CellLook
  text(value: unknown): string
}

export function isFormatted(f: ColumnFormat | undefined): boolean {
  if (!f) return false
  return (
    (f.number !== undefined && f.number.style !== 'auto') ||
    (f.date !== undefined && f.date.pattern !== 'auto') ||
    !!f.bars ||
    !!f.scale ||
    f.rules.length > 0
  )
}

/** What in a format can change a column's measured width: its text settings and the header's "formatted" mark. */
export function widthKey(f: ColumnFormat | undefined): string {
  return `${JSON.stringify(f?.number ?? null)}|${JSON.stringify(f?.date ?? null)}|${isFormatted(f)}`
}

const isNum = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v)

// The displayed calendar day, as a UTC midnight, so two values compare by the day the viewer reads.
function dayOf(raw: unknown): number | null {
  const t = parseDateValue(raw)
  if (t === null) return null
  const d = new Date(t)
  return isWallClock(raw)
    ? Date.UTC(d.getUTCFullYear(), d.getUTCMonth(), d.getUTCDate())
    : Date.UTC(d.getFullYear(), d.getMonth(), d.getDate())
}

function blank(v: unknown): boolean {
  return v === null || v === undefined || String(v).trim() === ''
}

type Matcher = (value: unknown) => boolean

// Bounds are parsed once per edit, not per cell; the number path only applies to number columns and the day path
// only to date columns, so numeric-looking text never falls through to Date.parse.
function compileRule(rule: HighlightRule, dataType: ColumnDataType): Matcher | null {
  if (ruleProblem(rule, dataType) !== null) return null
  const isNumberColumn = dataType === 'number'
  const isDateColumn = dataType === 'date' || dataType === 'dateTime'
  switch (rule.op) {
    case 'empty':
      return blank
    case 'is': {
      const want = rule.value
      return (value) => value !== null && value !== undefined && String(value) === want
    }
    case 'contains': {
      const want = rule.value.toLowerCase()
      return (value) => value !== null && value !== undefined && String(value).toLowerCase().includes(want)
    }
    case 'gt':
    case 'lt':
    case 'eq': {
      if (!isNumberColumn) return null
      const a = Number(rule.value.trim())
      if (rule.op === 'gt') return (value) => isNum(value) && value > a
      if (rule.op === 'lt') return (value) => isNum(value) && value < a
      return (value) => isNum(value) && value === a
    }
    case 'between': {
      if (isNumberColumn) {
        const a = Number(rule.value.trim())
        const b = Number(rule.value2.trim())
        const lo = Math.min(a, b)
        const hi = Math.max(a, b)
        return (value) => isNum(value) && value >= lo && value <= hi
      }
      if (!isDateColumn) return null
      const a = dayOf(rule.value)
      const b = dayOf(rule.value2)
      if (a === null || b === null) return null
      const lo = Math.min(a, b)
      const hi = Math.max(a, b)
      return (value) => {
        const v = dayOf(value)
        return v !== null && v >= lo && v <= hi
      }
    }
    default: {
      if (!isDateColumn) return null
      const a = dayOf(rule.value)
      if (a === null) return null
      const before = rule.op === 'before'
      return (value) => {
        const v = dayOf(value)
        return v !== null && (before ? v < a : v > a)
      }
    }
  }
}

export interface ColumnStats {
  min: number
  max: number
  median: number
  /** min and max widened to include zero: the span bars are drawn on. */
  lo: number
  hi: number
}

const statsCache = new WeakMap<readonly Record<string, unknown>[], Map<string, ColumnStats>>()

// Statistics depend only on the rows and the column, never on the format, so a rule or currency keystroke reuses them.
export function columnStats(rows: readonly Record<string, unknown>[], name: string): ColumnStats {
  let byName = statsCache.get(rows)
  if (!byName) statsCache.set(rows, (byName = new Map()))
  const hit = byName.get(name)
  if (hit) return hit
  let count = 0
  for (const row of rows) if (isNum(row[name])) count++
  const nums = new Float64Array(count)
  let i = 0
  for (const row of rows) {
    const v = row[name]
    if (isNum(v)) nums[i++] = v
  }
  // A typed array sorts numerically without a JS comparator, several times faster at 100,000 rows.
  nums.sort()
  const stats: ColumnStats = { min: 0, max: 0, median: 0, lo: 0, hi: 0 }
  if (count > 0) {
    stats.min = nums[0]
    stats.max = nums[count - 1]
    const mid = count >> 1
    stats.median = count % 2 ? nums[mid] : (nums[mid - 1] + nums[mid]) / 2
    stats.lo = Math.min(0, stats.min)
    stats.hi = Math.max(0, stats.max)
  }
  byName.set(name, stats)
  return stats
}

/** The cell text a format produces, without bars, scales or rules: what column widths are measured from. */
export function formatText(
  column: { dataType: ColumnDataType },
  f: ColumnFormat | undefined,
  locale?: string,
): (value: unknown) => string {
  const isNumberColumn = column.dataType === 'number'
  const isDateColumn = column.dataType === 'date' || column.dataType === 'dateTime'
  const number = isNumberColumn ? f?.number : undefined
  const date = isDateColumn && f?.date && f.date.pattern !== 'auto' ? f.date : undefined
  if (number) return (value) => (isNum(value) ? formatNumber(value, number, locale) : displayString(value))
  if (date) {
    const dataType = column.dataType as 'date' | 'dateTime'
    return (value) => formatDate(value, dataType, date, locale) ?? displayString(value)
  }
  return displayString
}

const cache = new WeakMap<
  readonly Record<string, unknown>[],
  Map<string, { dataType: ColumnDataType; f: ColumnFormat | undefined; locale: string | undefined; c: CompiledFormat }>
>()

export function compileFormat(
  rows: readonly Record<string, unknown>[],
  column: { name: string; dataType: ColumnDataType },
  f: ColumnFormat | undefined,
  locale?: string,
): CompiledFormat {
  let byName = cache.get(rows)
  if (!byName) cache.set(rows, (byName = new Map()))
  const hit = byName.get(column.name)
  if (hit && hit.dataType === column.dataType && hit.f === f && hit.locale === locale) return hit.c
  const c = build(rows, column, f, locale)
  byName.set(column.name, { dataType: column.dataType, f, locale, c })
  return c
}

function build(
  rows: readonly Record<string, unknown>[],
  column: { name: string; dataType: ColumnDataType },
  f: ColumnFormat | undefined,
  locale?: string,
): CompiledFormat {
  const isNumberColumn = column.dataType === 'number'
  const isDateColumn = column.dataType === 'date' || column.dataType === 'dateTime'
  const rules = (f?.rules ?? []).flatMap((rule) => {
    const test = compileRule(rule, column.dataType)
    return test ? [{ rule, test }] : []
  })
  const wantBars = isNumberColumn && !!f?.bars
  const scaleKind = isNumberColumn ? f?.scale ?? null : null
  const { lo, hi, min, max, median } =
    wantBars || scaleKind ? columnStats(rows, column.name) : { lo: 0, hi: 0, min: 0, max: 0, median: 0 }
  const text = formatText(column, f, locale)

  const fmtRuleValue = (v: string): string => {
    if (isNumberColumn && f?.number) {
      const n = Number(v.trim())
      return v.trim() !== '' && Number.isFinite(n) ? formatNumber(n, f.number, locale) : v
    }
    if (isDateColumn && f?.date && f.date.pattern !== 'auto') {
      return formatDate(v, column.dataType as 'date' | 'dateTime', f.date, locale) ?? v
    }
    return v
  }

  const barFor = (v: number): CellLook['bar'] => {
    const span = hi - lo
    if (span <= 0 || v === 0) return null
    const zero = (0 - lo) / span
    const at = (v - lo) / span
    return v < 0 ? { from: at, to: zero, negative: true } : { from: zero, to: at, negative: false }
  }

  const scaleFor = (v: number, kind: ScaleKind): number => {
    if (max === min) return 0.5
    if (kind === 'blue') return (v - min) / (max - min)
    if (v === median) return 0.5
    if (v < median) return 0.5 * ((v - min) / (median - min))
    return 0.5 + 0.5 * ((v - median) / (max - median))
  }

  return {
    formatted: isFormatted(f),
    text,
    look(value) {
      const shown = text(value)
      const raw = displayString(value)
      let highlight: RuleColor | null = null
      let label: string | null = null
      for (const { rule, test } of rules) {
        if (test(value)) {
          highlight = rule.color
          label = ruleLabel(column.name, rule, fmtRuleValue)
          break
        }
      }
      const parts: string[] = []
      if (raw !== shown) parts.push(raw)
      if (label) parts.push(label)
      const numeric = isNumberColumn && isNum(value)
      return {
        text: shown,
        title: parts.length ? parts.join(' · ') : null,
        bar: wantBars && numeric ? barFor(value as number) : null,
        scale: scaleKind && numeric ? { kind: scaleKind, t: scaleFor(value as number, scaleKind) } : null,
        highlight,
      }
    },
  }
}
