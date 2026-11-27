import { MAX_COL_WIDTH, MIN_COL_WIDTH } from './column-widths.ts'
import {
  MAX_RULES,
  defaultDateFormat,
  defaultNumberFormat,
  type ColumnFormat,
  type DatePattern,
  type DateFormat,
  type HighlightRule,
  type NumberFormat,
  type NumberStyle,
  type RuleColor,
  type RuleOp,
  type ScaleKind,
} from './grid-format.ts'
import type { TotalFn } from './grid-totals.ts'
import { defaultView, type GridView } from './grid-view.ts'

export interface SavedLayout {
  v: 1
  order: string[]
  hidden: string[]
  pinnedLeft: string[]
  pinnedRight: string[]
  widths: Record<string, number>
  wrap: boolean
  formats: Record<string, ColumnFormat>
  totals: { on: boolean; fns: Record<string, TotalFn> }
}

const NUMBER_STYLES: NumberStyle[] = ['auto', 'number', 'compact', 'currency', 'percent']
const DATE_PATTERNS: DatePattern[] = ['auto', 'weekday', 'medium', 'short', 'iso', 'monthYear', 'year']
const RULE_OPS: RuleOp[] = ['gt', 'lt', 'between', 'eq', 'is', 'contains', 'empty', 'before', 'after']
const RULE_COLORS: RuleColor[] = ['red', 'amber', 'green', 'blue']
const SCALES: ScaleKind[] = ['blue', 'redGreen']
const TOTAL_FNS: TotalFn[] = ['sum', 'avg', 'count', 'distinct', 'min', 'max', 'earliest', 'latest', 'none']

export function toSavedLayout(view: GridView): SavedLayout {
  return JSON.parse(
    JSON.stringify({
      v: 1,
      order: view.order,
      hidden: view.hidden,
      pinnedLeft: view.pinnedLeft,
      pinnedRight: view.pinnedRight,
      widths: view.widths,
      wrap: view.wrap,
      formats: view.formats,
      totals: view.totals,
    }),
  ) as SavedLayout
}

function isObject(x: unknown): x is Record<string, unknown> {
  return typeof x === 'object' && x !== null && !Array.isArray(x)
}

function names(x: unknown): string[] {
  if (!Array.isArray(x)) return []
  return [...new Set(x.filter((n): n is string => typeof n === 'string'))]
}

function oneOf<T extends string>(x: unknown, allowed: readonly T[]): T | null {
  return typeof x === 'string' && (allowed as readonly string[]).includes(x) ? (x as T) : null
}

function parseNumber(x: unknown): NumberFormat | undefined {
  if (!isObject(x)) return undefined
  const d = defaultNumberFormat
  return {
    style: oneOf(x.style, NUMBER_STYLES) ?? d.style,
    decimals:
      typeof x.decimals === 'number' && Number.isInteger(x.decimals) && x.decimals >= 0 && x.decimals <= 20
        ? x.decimals
        : d.decimals,
    currency: typeof x.currency === 'string' && x.currency ? x.currency : d.currency,
    thousands: typeof x.thousands === 'boolean' ? x.thousands : d.thousands,
    fraction: typeof x.fraction === 'boolean' ? x.fraction : d.fraction,
  }
}

function parseDate(x: unknown): DateFormat | undefined {
  if (!isObject(x)) return undefined
  return {
    pattern: oneOf(x.pattern, DATE_PATTERNS) ?? defaultDateFormat.pattern,
    showTime: typeof x.showTime === 'boolean' ? x.showTime : defaultDateFormat.showTime,
  }
}

function parseRules(x: unknown): HighlightRule[] {
  if (!Array.isArray(x)) return []
  const rules: HighlightRule[] = []
  for (const [index, r] of x.entries()) {
    if (rules.length >= MAX_RULES) break
    if (!isObject(r)) continue
    const op = oneOf(r.op, RULE_OPS)
    const color = oneOf(r.color, RULE_COLORS)
    if (!op || !color) continue
    rules.push({
      id: typeof r.id === 'string' ? r.id : `r${index}`,
      op,
      value: typeof r.value === 'string' ? r.value : '',
      value2: typeof r.value2 === 'string' ? r.value2 : '',
      color,
    })
  }
  return rules
}

function parseFormat(x: unknown): ColumnFormat | null {
  if (!isObject(x)) return null
  const format: ColumnFormat = { rules: parseRules(x.rules) }
  const number = parseNumber(x.number)
  if (number) format.number = number
  const date = parseDate(x.date)
  if (date) format.date = date
  if (x.bars === true) format.bars = true
  const scale = oneOf(x.scale, SCALES)
  if (scale) format.scale = scale
  const empty = !format.number && !format.date && !format.bars && !format.scale && format.rules.length === 0
  return empty ? null : format
}

export function parseSavedLayout(raw: unknown): SavedLayout | null {
  if (!isObject(raw) || raw.v !== 1) return null
  const widths: Record<string, number> = {}
  if (isObject(raw.widths)) {
    for (const [name, w] of Object.entries(raw.widths)) {
      if (typeof w === 'number' && Number.isFinite(w)) {
        widths[name] = Math.min(Math.max(Math.round(w), MIN_COL_WIDTH), MAX_COL_WIDTH)
      }
    }
  }
  const formats: Record<string, ColumnFormat> = {}
  if (isObject(raw.formats)) {
    for (const [name, f] of Object.entries(raw.formats)) {
      const parsed = parseFormat(f)
      if (parsed) formats[name] = parsed
    }
  }
  const totalsRaw = isObject(raw.totals) ? raw.totals : {}
  const fns: Record<string, TotalFn> = {}
  if (isObject(totalsRaw.fns)) {
    for (const [name, fn] of Object.entries(totalsRaw.fns)) {
      const known = oneOf(fn, TOTAL_FNS)
      if (known) fns[name] = known
    }
  }
  return {
    v: 1,
    order: names(raw.order),
    hidden: names(raw.hidden),
    pinnedLeft: names(raw.pinnedLeft),
    pinnedRight: names(raw.pinnedRight),
    widths,
    wrap: raw.wrap === true,
    formats,
    totals: { on: totalsRaw.on === true, fns },
  }
}

function keepKnown<T>(record: Record<string, T>, known: Set<string>): Record<string, T> {
  return Object.fromEntries(Object.entries(record).filter(([name]) => known.has(name)))
}

export function applySavedLayout(layout: SavedLayout | null, columns: string[]): GridView {
  const view = defaultView(columns)
  if (!layout) return view
  const known = new Set(columns)
  const saved = layout.order.filter((n) => known.has(n))
  const placed = new Set(saved)
  view.order = [...saved, ...columns.filter((n) => !placed.has(n))]
  const hidden = layout.hidden.filter((n) => known.has(n))
  view.hidden = hidden.length >= columns.length ? [] : hidden
  view.pinnedLeft = layout.pinnedLeft.filter((n) => known.has(n))
  const left = new Set(view.pinnedLeft)
  view.pinnedRight = layout.pinnedRight.filter((n) => known.has(n) && !left.has(n))
  view.widths = keepKnown(layout.widths, known)
  view.wrap = layout.wrap
  view.formats = keepKnown(layout.formats, known)
  view.totals = { on: layout.totals.on, fns: keepKnown(layout.totals.fns, known) }
  return view
}

function sameList(a: string[], b: string[]): boolean {
  return a.length === b.length && a.every((n, i) => n === b[i])
}

// Key order in a parsed object must not matter, so compare through a sorted-key form.
function canonical(x: unknown): string {
  return JSON.stringify(x, (_k, v) =>
    isObject(v) ? Object.fromEntries(Object.entries(v).sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) : v,
  )
}

export function layoutPartsEqual(view: GridView, baseline: GridView): boolean {
  return (
    sameList(view.order, baseline.order) &&
    sameList(view.pinnedLeft, baseline.pinnedLeft) &&
    sameList(view.pinnedRight, baseline.pinnedRight) &&
    view.hidden.length === baseline.hidden.length &&
    new Set(view.hidden).size === new Set(baseline.hidden).size &&
    view.hidden.every((n) => baseline.hidden.includes(n)) &&
    view.wrap === baseline.wrap &&
    canonical(view.widths) === canonical(baseline.widths) &&
    canonical(view.formats) === canonical(baseline.formats) &&
    canonical(view.totals) === canonical(baseline.totals)
  )
}

export function isPlainLayout(view: GridView, columns: string[]): boolean {
  return layoutPartsEqual(view, defaultView(columns))
}
