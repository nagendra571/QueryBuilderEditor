import type {
  AggregateFunction,
  ColumnDataType,
  QueryColumn,
  ValueFormat,
  VisualizationOptions,
  VisualizationSort,
  VisualizationType,
} from '@/types'
import { formatPlainNumber } from './format.ts'
import { counterText, targetText } from './single-number.ts'

export const BLANK_LABEL = '(blank)'
export const MAX_PIE_SLICES = 12
export const MAX_MEASURES = 5

export interface OutputColumn {
  name: string
  dataType: ColumnDataType
  isAggregate: boolean
  aggregate: AggregateFunction
}

/** Mirrors the server's output-column rule: visible columns, counts/sums/averages are numeric. */
export function outputColumns(columns: QueryColumn[]): OutputColumn[] {
  return columns
    .filter((c) => c.isVisible)
    .map((c) => ({
      name: c.alias?.trim() ? c.alias : c.columnName,
      dataType: ['count', 'countDistinct', 'sum', 'avg'].includes(c.aggregate) ? 'number' : c.dataType,
      isAggregate: c.aggregate !== 'none',
      aggregate: c.aggregate,
    }))
}

/** Default chart roles: the grouping column on X and a total as the measure. Numeric IDs
 * (e.g. ProgramId) are numbers too, so "first numeric column" would wrongly pick them. */
export function defaultRoles(columns: OutputColumn[]): { x: string; measure: string | null } {
  const x = columns.find((c) => !c.isAggregate) ?? columns.find((c) => c.dataType !== 'number') ?? columns[0]
  const measure =
    columns.find((c) => c.isAggregate && c.dataType === 'number') ??
    columns.find((c) => c.dataType === 'number' && c.name !== x?.name)
  return { x: x?.name ?? '', measure: measure?.name ?? null }
}

/** Series keys are positional so a column name containing "." never gets read as a data path. */
export interface SeriesInfo {
  key: string
  label: string
}

export interface CartesianModel {
  kind: 'cartesian'
  series: SeriesInfo[]
  points: Array<Record<string, string | number | null>>
  skipped: number
}

export interface PieModel {
  kind: 'pie'
  slices: Array<{ name: string; value: number }>
  skipped: number
  problem: string | null
}

export interface CounterModel {
  kind: 'counter'
  value: number | null
  label: string
  target: number | null
  pctOfTarget: number | null
}

export interface TableModel {
  kind: 'table'
  columns: string[]
  rows: Record<string, unknown>[]
}

export type ChartModel = CartesianModel | PieModel | CounterModel | TableModel

type Rows = Record<string, unknown>[]

/** A missing value stays missing — never coerced to zero, which would draw a number that isn't there. */
export function toNumber(value: unknown): number | null | 'bad' {
  if (value === null || value === undefined) return null
  if (typeof value === 'number') return Number.isFinite(value) ? value : 'bad'
  if (typeof value === 'string' && value.trim() !== '') {
    const n = Number(value)
    return Number.isFinite(n) ? n : 'bad'
  }
  return 'bad'
}

export function formatCategory(value: unknown): string {
  if (value === null || value === undefined || value === '') return BLANK_LABEL
  if (typeof value === 'string') {
    // Date columns arrive as ISO timestamps; a midnight time-of-day is noise.
    return value.replace(/^(\d{4}-\d{2}-\d{2})T00:00:00(?:\.0+)?(?:Z|[+-]00:00)?$/, '$1')
  }
  return String(value)
}

export function compareCategories(a: string, b: string): number {
  const na = Number(a)
  const nb = Number(b)
  if (a !== '' && b !== '' && Number.isFinite(na) && Number.isFinite(nb)) return na - nb
  return a.localeCompare(b, undefined, { numeric: true })
}

/** The blank bucket always goes last, whichever way the rest is sorted. */
export function compareBlankLast(a: string, b: string, direction: 1 | -1): number {
  if (a === BLANK_LABEL) return b === BLANK_LABEL ? 0 : 1
  if (b === BLANK_LABEL) return -1
  return compareCategories(a, b) * direction
}

function sortPoints<T extends { x: string | number | null }>(points: T[], sort: VisualizationOptions['sortX']): T[] {
  if (sort === 'none') return points
  const direction = sort === 'asc' ? 1 : -1
  return [...points].sort((p, q) => compareBlankLast(String(p.x), String(q.x), direction))
}

export function buildCartesian(options: VisualizationOptions, rows: Rows): CartesianModel {
  const x = options.x ?? ''
  let skipped = 0
  const read = (value: unknown): number | null => {
    const n = toNumber(value)
    if (n === 'bad') {
      skipped += 1
      return null
    }
    return n
  }

  if (options.series) {
    const seriesColumn = options.series
    const measure = options.measures[0]
    const labels: string[] = []
    const byX = new Map<string, { x: string } & Record<string, string | number | null>>()
    for (const row of rows) {
      const label = formatCategory(row[seriesColumn])
      if (!labels.includes(label)) labels.push(label)
      const key = `s${labels.indexOf(label)}`
      const xLabel = formatCategory(row[x])
      const point = byX.get(xLabel) ?? { x: xLabel }
      byX.set(xLabel, point)
      const value = read(row[measure])
      const existing = point[key]
      point[key] = value === null ? (existing ?? null) : (typeof existing === 'number' ? existing : 0) + value
    }
    const series = labels.map((label, i) => ({ key: `s${i}`, label }))
    const points = [...byX.values()].map((p) => {
      for (const s of series) if (!(s.key in p)) p[s.key] = null
      return p
    })
    return { kind: 'cartesian', series, points: sortPoints(points, options.sortX), skipped }
  }

  const series = options.measures.map((measure, i) => ({ key: `s${i}`, label: measure }))
  const points = rows.map((row) => {
    const point: { x: string } & Record<string, string | number | null> = { x: formatCategory(row[x]) }
    options.measures.forEach((measure, i) => {
      point[`s${i}`] = read(row[measure])
    })
    return point
  })
  return { kind: 'cartesian', series, points: sortPoints(points, options.sortX), skipped }
}

export function buildPie(options: VisualizationOptions, rows: Rows): PieModel {
  const x = options.x ?? ''
  const measure = options.measures[0]
  let skipped = 0
  const totals = new Map<string, number>()
  let negative = false
  for (const row of rows) {
    const n = toNumber(row[measure])
    if (n === 'bad') {
      skipped += 1
      continue
    }
    if (n === null) continue
    if (n < 0) negative = true
    const name = formatCategory(row[x])
    totals.set(name, (totals.get(name) ?? 0) + n)
  }
  let slices = [...totals.entries()].map(([name, value]) => ({ name, value }))
  if (options.sortX !== 'none') {
    slices = sortPoints(slices.map((s) => ({ ...s, x: s.name })), options.sortX).map(({ name, value }) => ({ name, value }))
  }
  const problem = negative
    ? 'A pie chart cannot show negative values — use a bar chart instead.'
    : slices.length > MAX_PIE_SLICES
      ? `Too many slices for a pie chart (max ${MAX_PIE_SLICES}) — use a bar chart instead.`
      : null
  return { kind: 'pie', slices, skipped, problem }
}

export function buildCounter(options: VisualizationOptions, rows: Rows): CounterModel {
  const counter = options.counter
  const read = (column: string | null | undefined) => {
    const n = column && rows.length > 0 ? toNumber(rows[0][column]) : null
    return n === 'bad' ? null : n
  }
  const value = read(counter?.value)
  const target = read(counter?.target)
  return {
    kind: 'counter',
    value,
    label: counter?.label?.trim() || counter?.value || '',
    target,
    pctOfTarget: value !== null && target !== null && target > 0 ? (value / target) * 100 : null,
  }
}

export function buildModel(type: VisualizationType, options: VisualizationOptions, rows: Rows): ChartModel {
  if (type === 'pie') return buildPie(options, rows)
  if (type === 'counter') return buildCounter(options, rows)
  if (type === 'table') return { kind: 'table', columns: options.table?.columns ?? [], rows }
  return buildCartesian(options, rows)
}

export const TABLE_PAGE_SIZE = 25

/** A page past the end (rows shrank after a refresh) lands on the last page instead of showing nothing. */
export function pageRows<T>(rows: T[], page: number, size: number): { rows: T[]; page: number; pageCount: number } {
  const pageCount = Math.max(1, Math.ceil(rows.length / size))
  const current = Math.min(Math.max(0, Math.trunc(page) || 0), pageCount - 1)
  return { rows: rows.slice(current * size, (current + 1) * size), page: current, pageCount }
}

/** An absent column named like an Object.prototype member must read as missing, not as the inherited property. */
export function cellValue(row: Record<string, unknown>, name: string): unknown {
  return Object.hasOwn(row, name) ? row[name] : undefined
}

/** The render response's column type wins; without it (older callers) every non-null value must be a number. */
export function isNumberColumn(
  name: string,
  columns: { name: string; dataType?: ColumnDataType }[] | undefined,
  rows: Record<string, unknown>[],
): boolean {
  const declared = columns?.find((c) => c.name === name)?.dataType
  if (declared) return declared === 'number'
  let seen = false
  for (const row of rows) {
    const v = cellValue(row, name)
    if (v === null || v === undefined) continue
    if (typeof v !== 'number') return false
    seen = true
  }
  return seen
}

export const MAX_TABLE_COLUMNS = 30

export function defaultTableColumns(columns: OutputColumn[]): string[] {
  return columns.slice(0, MAX_TABLE_COLUMNS).map((c) => c.name)
}

const TYPE_LABEL: Record<VisualizationType, string> = {
  bar: 'Bar',
  line: 'Line',
  pie: 'Pie',
  counter: 'Counter',
  area: 'Area',
  horizontalBar: 'Horizontal bar',
  combo: 'Combo',
  scatter: 'Scatter',
  table: 'Table',
  pivot: 'Pivot',
  funnel: 'Funnel',
  waterfall: 'Waterfall',
  trend: 'Trend',
}
export const typeLabel = (type: VisualizationType) => TYPE_LABEL[type]

/** "Table chart" and "Pivot chart" read wrong, so those two are named for what they are. */
export function typeNoun(type: VisualizationType): string {
  if (type === 'table') return 'Table'
  if (type === 'pivot') return 'Pivot table'
  return `${TYPE_LABEL[type]} chart`
}

export const isPercentStack = (type: VisualizationType, options: VisualizationOptions) =>
  canStack(type) && !!options.stacked && !!options.percent

/** Screen-reader summary for role="img". */
export function describeChart(type: VisualizationType, options: VisualizationOptions, model: ChartModel): string {
  if (model.kind === 'counter') {
    const shown = model.value === null ? 'no value' : counterText(model.value, options.counter?.decimals ?? 0, options.format)
    const target = targetText(model.pctOfTarget)
    return `Counter: ${model.label}, ${shown}${target ? `, ${target}` : ''}`
  }
  if (model.kind === 'pie') {
    return `Pie chart: ${options.measures[0]} by ${options.x}, ${model.slices.length} categories`
  }
  if (model.kind === 'table') return `Table: ${model.columns.length} columns, ${model.rows.length} rows`
  const measures = options.series ? options.measures[0] : options.measures.join(', ')
  const by = options.series ? `${options.x} and ${options.series}` : options.x
  const kind = isPercentStack(type, options) ? `100% stacked ${typeLabel(type).toLowerCase()}` : typeLabel(type)
  return `${kind} chart: ${measures} by ${by}, ${model.points.length} categories`
}

const hasNegative = (point: Record<string, string | number | null>, series: SeriesInfo[]) =>
  series.some((s) => {
    const v = point[s.key]
    return typeof v === 'number' && v < 0
  })

/** Each category's values become a percent of that category's total. A zero total leaves the whole
 * category empty rather than dividing by zero, and a missing value stays missing. A category holding
 * a negative value has no meaningful share (50 and -30 would read 250% and -150%), so it stays empty too. */
export function toPercentStack<T extends Record<string, string | number | null>>(points: T[], series: SeriesInfo[]): T[] {
  return points.map((point) => {
    const refuse = hasNegative(point, series)
    let total = 0
    for (const s of series) {
      const v = point[s.key]
      if (typeof v === 'number') total += v
    }
    const next: Record<string, string | number | null> = { ...point }
    for (const s of series) {
      const v = point[s.key]
      next[s.key] = typeof v === 'number' && total !== 0 && !refuse ? (v / total) * 100 : null
    }
    return next as T
  })
}

export function negativeCategoryCount(points: Record<string, string | number | null>[], series: SeriesInfo[]): number {
  return points.filter((p) => hasNegative(p, series)).length
}

/** Rows that share an X with another row. Without a series each X should be one row. */
export function duplicateXCount(options: VisualizationOptions, rows: Rows): number {
  if (options.series || !options.x) return 0
  const counts = new Map<string, number>()
  for (const row of rows) {
    const label = formatCategory(row[options.x])
    counts.set(label, (counts.get(label) ?? 0) + 1)
  }
  let shared = 0
  for (const n of counts.values()) if (n > 1) shared += n
  return shared
}

export function hasStackGap(model: CartesianModel): boolean {
  return model.points.some((p) => model.series.some((s) => p[s.key] === null || p[s.key] === undefined))
}

export const MAX_PIVOT_ROWS = 3
export const MAX_PIVOT_VALUES = 5

/** Client-side shape check for the live preview; the server re-validates on save and is the source of truth. */
export function optionsProblem(
  type: VisualizationType,
  options: VisualizationOptions,
  columns: OutputColumn[],
): string | null {
  const problem = rolesProblem(type, options, columns)
  if (problem) return problem
  const shared = referencedColumns(type, options).filter((n) => columns.filter((c) => c.name === n).length > 1)
  return shared.length > 0 ? `More than one column is named ${shared.join(', ')}; give them different names.` : null
}

function rolesProblem(
  type: VisualizationType,
  options: VisualizationOptions,
  columns: OutputColumn[],
): string | null {
  const byName = new Map(columns.map((c) => [c.name, c]))
  const isNumber = (name: string) => byName.get(name)?.dataType === 'number'
  const missing = (name: string) => !byName.has(name)

  const format = options.format
  if (format) {
    if (format.decimals != null && (format.decimals < 0 || format.decimals > 4)) {
      return 'Number format decimals must be between 0 and 4.'
    }
    if (format.style === 'currency' && !/^[A-Z]{3}$/.test(format.currency ?? '')) {
      return 'A currency format needs a three-letter code such as USD.'
    }
  }

  if (type === 'counter') {
    if (!options.counter?.value) return 'Choose the number to show.'
    if (!isNumber(options.counter.value)) return 'The counter needs a number column.'
    if (options.counter.target && !isNumber(options.counter.target)) return 'The target needs a number column.'
    return null
  }

  if (type === 'table') {
    const cols = options.table?.columns ?? []
    if (cols.length < 1 || cols.length > MAX_TABLE_COLUMNS) return `Choose between 1 and ${MAX_TABLE_COLUMNS} table columns.`
    if (cols.some(missing)) return 'A table column is no longer in this query.'
    if (new Set(cols).size !== cols.length) return 'A table column can only be used once.'
    return null
  }

  if (type === 'pivot') {
    const pivot = options.pivot
    const rows = pivot?.rows ?? []
    const values = pivot?.values ?? []
    if (rows.length < 1 || rows.length > MAX_PIVOT_ROWS) return `Choose between 1 and ${MAX_PIVOT_ROWS} row columns.`
    if (values.length < 1 || values.length > MAX_PIVOT_VALUES) return `Choose between 1 and ${MAX_PIVOT_VALUES} value columns.`
    const roles = [...rows, ...values, ...(pivot?.column ? [pivot.column] : [])]
    if (roles.some(missing)) return 'A pivot column is no longer in this query.'
    if (values.some((v) => !isNumber(v))) return 'Pivot values must be number columns.'
    if (new Set(roles).size !== roles.length) return 'A column cannot be used in two pivot roles.'
    return null
  }

  if (!options.x) return 'Choose a category (X) column.'
  const x = byName.get(options.x)
  if (!x) return `'${options.x}' is no longer a column of this query.`
  if (type === 'scatter' || type === 'trend') {
    if (!['number', 'date', 'dateTime'].includes(x.dataType)) return 'The X column must be a number or a date.'
  }

  if (options.measures.length < 1 || options.measures.length > MAX_MEASURES) {
    return `Choose between 1 and ${MAX_MEASURES} measure columns.`
  }
  if (options.measures.some((m) => !isNumber(m))) return 'Measures must be number columns.'
  if (new Set(options.measures).size !== options.measures.length) return 'A measure column can only be used once.'

  if (options.series) {
    if (type === 'pie') return 'A pie chart cannot have a series column.'
    if (missing(options.series)) return `'${options.series}' is no longer a column of this query.`
    if (options.measures.length !== 1) return 'A series needs exactly one measure.'
  }

  if (type === 'pie' && options.measures.length !== 1) return 'A pie chart needs exactly one measure.'
  if (type === 'scatter' && options.measures.length !== 1) return 'A scatter chart needs exactly one measure.'

  if ((type === 'funnel' || type === 'waterfall' || type === 'trend') && (options.measures.length !== 1 || options.series)) {
    return 'This chart needs exactly one measure and no series column.'
  }

  if (options.stacked && !['bar', 'horizontalBar', 'area'].includes(type)) return 'Only bar and area charts can be stacked.'
  if (options.percent && !options.stacked) return 'A 100% chart must also be stacked.'

  if (type === 'combo') {
    if (options.series) return 'A combo chart cannot have a series column.'
    const lines = [...new Set(options.lineMeasures ?? [])]
    if (lines.some((l) => !options.measures.includes(l))) return 'Every line measure must also be one of the measures.'
    if (lines.length < 1 || lines.length >= options.measures.length) return 'A combo chart needs at least one line and one bar measure.'
  }

  if (type === 'scatter') {
    if (options.size && !isNumber(options.size)) return 'The size column must be a number.'
    if (options.label && missing(options.label)) return `'${options.label}' is no longer a column of this query.`
  }

  return null
}

/** Mirrors the server's ReferencedColumns: roles left over from another type are ignored. */
export function referencedColumns(type: VisualizationType, options: VisualizationOptions): string[] {
  let names: (string | null | undefined)[]
  switch (type) {
    case 'counter':
      names = [options.counter?.value, options.counter?.target]
      break
    case 'table':
      names = options.table?.columns ?? []
      break
    case 'pivot':
      names = [...(options.pivot?.rows ?? []), options.pivot?.column, ...(options.pivot?.values ?? [])]
      break
    default:
      names = [
        options.x,
        options.series,
        ...options.measures,
        ...(type === 'combo' ? (options.lineMeasures ?? []) : []),
        ...(type === 'scatter' ? [options.size, options.label] : []),
      ]
  }
  return [...new Set(names.filter((c): c is string => !!c))]
}

export interface ScatterPoint {
  x: number
  y: number
  size: number | null
  label: string | null
}

export interface ScatterModel {
  kind: 'scatter'
  groups: { name: string; points: ScatterPoint[] }[]
  skipped: number
  xIsDate: boolean
  problem: string | null
}

export const SCATTER_NEGATIVE_SIZE = 'Bubble size cannot be negative.'

const DAY_MS = 86_400_000

export const scatterHasTime = (model: ScatterModel) =>
  model.xIsDate && model.groups.some((g) => g.points.some((p) => p.x % DAY_MS !== 0))

const DAY_ONLY = /^(\d{4})-(\d{2})-(\d{2})$/
const DATE_TIME = /^\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}(?::\d{2}(?:\.\d+)?)?(Z|[+-]\d{2}:?\d{2})?$/i

/** Date-only is UTC midnight and a time with no offset is read as written (UTC), so the same calendar day
 * comes out on every machine — Date's own parsing would shift both by the viewer's time zone. */
export function parseDateX(value: unknown): number | null {
  if (typeof value !== 'string') return null
  const text = value.trim()
  const day = DAY_ONLY.exec(text)
  if (day) {
    const [y, m, d] = [Number(day[1]), Number(day[2]), Number(day[3])]
    const ms = Date.UTC(y, m - 1, d)
    const back = new Date(ms)
    return back.getUTCFullYear() === y && back.getUTCMonth() === m - 1 && back.getUTCDate() === d ? ms : null
  }
  const match = DATE_TIME.exec(text)
  if (!match) return null
  const iso = text.replace(' ', 'T')
  const ms = Date.parse(match[1] ? iso : `${iso}Z`)
  return Number.isFinite(ms) ? ms : null
}

export function buildScatter(options: VisualizationOptions, rows: Rows): ScatterModel {
  const xColumn = options.x ?? ''
  const yColumn = options.measures[0] ?? ''

  let numericX = 0
  let dateX = 0
  for (const row of rows) {
    const raw = cellValue(row, xColumn)
    if (raw === null || raw === undefined) continue
    if (typeof toNumber(raw) === 'number') numericX += 1
    else if (parseDateX(raw) !== null) dateX += 1
  }
  const xIsDate = dateX > 0 && numericX === 0

  const names: string[] = []
  const groups: ScatterModel['groups'] = []
  let skipped = 0
  const negativeSize =
    !!options.size &&
    rows.some((row) => {
      const n = toNumber(cellValue(row, options.size!))
      return typeof n === 'number' && n < 0
    })
  for (const row of rows) {
    const rawX = cellValue(row, xColumn)
    const x = xIsDate ? parseDateX(rawX) : toNumber(rawX)
    const y = toNumber(cellValue(row, yColumn))
    if (x === null || x === 'bad' || y === null || y === 'bad') {
      skipped += 1
      continue
    }
    const sizeValue = options.size ? toNumber(cellValue(row, options.size)) : null
    const size = typeof sizeValue === 'number' && sizeValue >= 0 ? sizeValue : null
    const labelValue = options.label ? cellValue(row, options.label) : null
    const label = labelValue === null || labelValue === undefined || labelValue === '' ? null : formatCategory(labelValue)

    const name = options.series ? formatCategory(cellValue(row, options.series)) : ''
    let index = names.indexOf(name)
    if (index < 0) {
      names.push(name)
      groups.push({ name, points: [] })
      index = groups.length - 1
    }
    groups[index].points.push({ x, y, size, label })
  }
  if (groups.length === 0) groups.push({ name: '', points: [] })
  return { kind: 'scatter', groups, skipped, xIsDate, problem: negativeSize ? SCATTER_NEGATIVE_SIZE : null }
}

/** Dates are shown in UTC to match how they were read; numbers stay plain because the format belongs to the measure. */
export function formatScatterX(value: number, isDate: boolean, withTime: boolean): string {
  if (!isDate) return formatPlainNumber(value)
  return new Intl.DateTimeFormat(undefined, {
    timeZone: 'UTC',
    dateStyle: 'medium',
    ...(withTime ? { timeStyle: 'short' } : {}),
  }).format(value)
}

const STACKABLE: VisualizationType[] = ['bar', 'horizontalBar', 'area']
const NO_SPLIT: VisualizationType[] = ['pie', 'combo', 'funnel', 'waterfall', 'trend']
const SINGLE_MEASURE: VisualizationType[] = ['pie', 'funnel', 'waterfall', 'scatter', 'trend']

export function canStack(type: VisualizationType): boolean {
  return STACKABLE.includes(type)
}

export function canSplit(type: VisualizationType): boolean {
  return !NO_SPLIT.includes(type)
}

export function measureLimit(type: VisualizationType, series: string): number {
  return SINGLE_MEASURE.includes(type) || (canSplit(type) && !!series) ? 1 : MAX_MEASURES
}

/** Everything the editor form holds; {@link buildOptions} picks the part that belongs to the chosen type. */
export interface ChartRoles {
  x: string
  measures: string[]
  series: string
  stacked: boolean
  percent: boolean
  lineMeasures: string[]
  sortX: VisualizationSort
  counter: { value: string; label: string; decimals: number; target: string }
  tableColumns: string[]
  pivot: { rows: string[]; column: string; values: string[]; totals: boolean; shade: boolean }
  size: string
  label: string
  format: ValueFormat | null
}

/** Only fields that belong to the chosen type are saved, so roles left over from another type never persist. */
export function buildOptions(type: VisualizationType, roles: ChartRoles): VisualizationOptions {
  const format = roles.format
    ? {
        style: roles.format.style,
        decimals: roles.format.decimals,
        currency: roles.format.style === 'currency' ? (roles.format.currency ?? '').trim().toUpperCase() : null,
      }
    : null
  const none: Pick<VisualizationOptions, 'x' | 'measures' | 'series' | 'stacked' | 'sortX'> = {
    x: null,
    measures: [],
    series: null,
    stacked: false,
    sortX: 'none',
  }

  if (type === 'pivot') {
    const p = roles.pivot
    return {
      version: 1,
      ...none,
      percent: false,
      lineMeasures: [],
      counter: null,
      table: null,
      pivot: { rows: p.rows, column: p.column || null, values: p.values, totals: p.totals, shade: p.shade },
      format,
    }
  }
  if (type === 'table') {
    return {
      version: 1,
      ...none,
      percent: false,
      lineMeasures: [],
      counter: null,
      table: { columns: roles.tableColumns },
      format,
    }
  }
  if (type === 'counter') {
    const c = roles.counter
    return {
      version: 1,
      ...none,
      counter: { value: c.value, label: c.label.trim() || null, decimals: c.decimals, target: c.target || null },
      format,
    }
  }

  const x = roles.x || null
  const measures = roles.measures.slice(0, measureLimit(type, canSplit(type) ? roles.series : ''))
  if (type === 'funnel' || type === 'waterfall') {
    return {
      version: 1,
      x,
      measures,
      series: null,
      stacked: false,
      percent: false,
      lineMeasures: [],
      sortX: roles.sortX,
      counter: null,
      format,
    }
  }
  if (type === 'trend') {
    return { version: 1, x, measures, series: null, stacked: false, percent: false, lineMeasures: [], sortX: 'none', counter: null, format }
  }
  const series = canSplit(type) && roles.series ? roles.series : null
  if (type === 'scatter') {
    return {
      version: 1,
      x,
      measures,
      series,
      stacked: false,
      percent: false,
      lineMeasures: [],
      sortX: 'none',
      counter: null,
      size: roles.size || null,
      label: roles.label || null,
      format,
    }
  }
  const stacked = canStack(type) && roles.stacked
  return {
    version: 1,
    x,
    measures,
    series,
    stacked,
    percent: stacked && roles.percent,
    lineMeasures: type === 'combo' ? roles.lineMeasures.filter((m) => measures.includes(m)) : [],
    sortX: roles.sortX,
    counter: null,
    format,
  }
}
