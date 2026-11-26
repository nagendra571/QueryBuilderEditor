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

// Intl throws RangeError on a malformed currency code; a bad saved format must still render a number.
function currencyCode(f: NumberFormat): string | null {
  const code = f.currency?.trim().toUpperCase()
  return code && /^[A-Z]{3}$/.test(code) ? code : null
}

export function formatNumber(n: number, f: NumberFormat, locale?: string): string {
  if (f.style === 'auto') return n.toLocaleString(locale)
  const useGrouping = f.thousands
  const d = f.decimals ?? undefined
  const pinned = d === undefined ? {} : { minimumFractionDigits: d, maximumFractionDigits: d }
  switch (f.style) {
    case 'compact':
      return new Intl.NumberFormat(locale, { notation: 'compact', maximumFractionDigits: d ?? 1, useGrouping }).format(n)
    case 'currency': {
      const currency = currencyCode(f)
      if (currency) {
        try {
          return new Intl.NumberFormat(locale, { style: 'currency', currency, useGrouping, ...pinned }).format(n)
        } catch {
          // fall through to the plain number
        }
      }
      return formatNumber(n, { ...f, style: 'number' }, locale)
    }
    case 'percent':
      // Chart parity: 42 means 42%, so Intl's own x100 only applies when the data holds fractions.
      return new Intl.NumberFormat(locale, {
        style: 'percent',
        useGrouping,
        minimumFractionDigits: d ?? 0,
        maximumFractionDigits: d ?? 2,
      }).format(f.fraction ? n : n / 100)
    default: {
      const places = d ?? (Number.isInteger(n) ? 0 : 2)
      return new Intl.NumberFormat(locale, { useGrouping, minimumFractionDigits: places, maximumFractionDigits: places }).format(n)
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
      text = new Intl.DateTimeFormat(locale, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone }).format(d)
      break
    case 'short':
      text = new Intl.DateTimeFormat(locale, { day: '2-digit', month: '2-digit', year: 'numeric', timeZone }).format(d)
      break
    case 'iso':
      text = `${y}-${pad(m)}-${pad(day)}`
      break
    case 'monthYear':
      text = new Intl.DateTimeFormat(locale, { month: 'short', year: 'numeric', timeZone }).format(d)
      break
    case 'year':
      text = String(y)
      break
    default:
      text = new Intl.DateTimeFormat(locale, { day: 'numeric', month: 'short', year: 'numeric', timeZone }).format(d)
  }
  if (f.showTime && dataType === 'dateTime') {
    text += ` ${new Intl.DateTimeFormat(locale, { hour: '2-digit', minute: '2-digit', timeZone }).format(d)}`
  }
  return text
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

function matches(rule: HighlightRule, value: unknown): boolean {
  if (!isComplete(rule)) return false
  switch (rule.op) {
    case 'empty':
      return blank(value)
    case 'is':
      return value !== null && value !== undefined && String(value) === rule.value
    case 'contains':
      return value !== null && value !== undefined && String(value).toLowerCase().includes(rule.value.toLowerCase())
    case 'gt':
    case 'lt':
    case 'eq': {
      if (!isNum(value)) return false
      const a = Number(rule.value.trim())
      if (Number.isNaN(a)) return false
      return rule.op === 'gt' ? value > a : rule.op === 'lt' ? value < a : value === a
    }
    case 'between': {
      if (isNum(value)) {
        const a = Number(rule.value.trim())
        const b = Number(rule.value2.trim())
        if (Number.isNaN(a) || Number.isNaN(b)) return false
        return value >= Math.min(a, b) && value <= Math.max(a, b)
      }
      const v = dayOf(value)
      const a = dayOf(rule.value)
      const b = dayOf(rule.value2)
      if (v === null || a === null || b === null) return false
      return v >= Math.min(a, b) && v <= Math.max(a, b)
    }
    default: {
      const v = dayOf(value)
      const a = dayOf(rule.value)
      if (v === null || a === null) return false
      return rule.op === 'before' ? v < a : v > a
    }
  }
}

const cache = new WeakMap<readonly Record<string, unknown>[], Map<string, { f: ColumnFormat | undefined; locale: string | undefined; c: CompiledFormat }>>()

export function compileFormat(
  rows: readonly Record<string, unknown>[],
  column: { name: string; dataType: ColumnDataType },
  f: ColumnFormat | undefined,
  locale?: string,
): CompiledFormat {
  let byName = cache.get(rows)
  if (!byName) cache.set(rows, (byName = new Map()))
  const hit = byName.get(column.name)
  if (hit && hit.f === f && hit.locale === locale) return hit.c
  const c = build(rows, column, f, locale)
  byName.set(column.name, { f, locale, c })
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
  const rules = (f?.rules ?? []).filter(isComplete)
  const wantBars = isNumberColumn && !!f?.bars
  const scaleKind = isNumberColumn ? f?.scale ?? null : null

  let lo = 0
  let hi = 0
  let min = 0
  let max = 0
  let median = 0
  if (wantBars || scaleKind) {
    const nums: number[] = []
    for (const row of rows) {
      const v = row[column.name]
      if (isNum(v)) nums.push(v)
    }
    if (nums.length > 0) {
      nums.sort((a, b) => a - b)
      min = nums[0]
      max = nums[nums.length - 1]
      const mid = nums.length >> 1
      median = nums.length % 2 ? nums[mid] : (nums[mid - 1] + nums[mid]) / 2
      lo = Math.min(0, min)
      hi = Math.max(0, max)
    }
  }

  const formatText = (value: unknown): string => {
    if (isNumberColumn && f?.number && isNum(value)) return formatNumber(value, f.number, locale)
    if (isDateColumn && f?.date && f.date.pattern !== 'auto') {
      return formatDate(value, column.dataType as 'date' | 'dateTime', f.date, locale) ?? displayString(value)
    }
    return displayString(value)
  }

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
    text: formatText,
    look(value) {
      const text = formatText(value)
      const raw = displayString(value)
      let highlight: RuleColor | null = null
      let label: string | null = null
      for (const rule of rules) {
        if (matches(rule, value)) {
          highlight = rule.color
          label = ruleLabel(column.name, rule, fmtRuleValue)
          break
        }
      }
      const parts: string[] = []
      if (raw !== text) parts.push(raw)
      if (label) parts.push(label)
      const numeric = isNumberColumn && isNum(value)
      return {
        text,
        title: parts.length ? parts.join(' · ') : null,
        bar: wantBars && numeric ? barFor(value as number) : null,
        scale: scaleKind && numeric ? { kind: scaleKind, t: scaleFor(value as number, scaleKind) } : null,
        highlight,
      }
    },
  }
}
