import type { VisualizationOptions } from '@/types'
import { cellValue, compareBlankLast, compareCategories, formatCategory, parseDateX, toNumber } from './chart-data.ts'

// Same limits and wording as VisualizationDataChecks on the server, so the preview refuses what a render would.
export const MAX_FUNNEL_STEPS = 20
export const MAX_WATERFALL_BARS = 30
export const FUNNEL_TOO_MANY = `Too many steps for a funnel (max ${MAX_FUNNEL_STEPS}).`
export const FUNNEL_NEGATIVE = 'A funnel cannot show negative values.'
export const WATERFALL_TOO_MANY = `Too many bars for a waterfall (max ${MAX_WATERFALL_BARS}); add filters or totals.`

export interface FunnelStep {
  name: string
  value: number | null
  pctOfFirst: number | null
  pctOfPrevious: number | null
}

export interface FunnelModel {
  kind: 'funnel'
  steps: FunnelStep[]
  skipped: number
  problem: string | null
}

export interface WaterfallBar {
  name: string
  /** Low end of the bar; size runs upward from it, so a bar that crosses zero still has a non-negative size. */
  base: number
  size: number
  kind: 'up' | 'down' | 'total'
  value: number
}

export interface WaterfallModel {
  kind: 'waterfall'
  bars: WaterfallBar[]
  skipped: number
  problem: string | null
}

export interface TrendPoint {
  x: string
  y: number | null
}

export interface TrendModel {
  kind: 'trend'
  latest: number | null
  latestLabel: string
  previous: number | null
  previousLabel: string | null
  change: number | null
  changePct: number | null
  points: TrendPoint[]
  skipped: number
}

export const TOTAL_LABEL = 'Total'

export const KIND_LABEL: Record<WaterfallBar['kind'], string> = {
  up: 'Increase',
  down: 'Decrease',
  total: 'Total',
}

type Rows = Record<string, unknown>[]

interface Entry {
  name: string
  amount: number | null
  bad: boolean
}

function entries(options: VisualizationOptions, rows: Rows): Entry[] {
  const measure = options.measures[0]
  const list = rows.map((row) => {
    const raw = measure ? toNumber(cellValue(row, measure)) : null
    return {
      name: formatCategory(options.x ? cellValue(row, options.x) : null),
      amount: raw === 'bad' ? null : raw,
      bad: raw === 'bad',
    }
  })
  if (options.sortX === 'none') return list
  const direction = options.sortX === 'asc' ? 1 : -1
  return list.sort((p, q) => compareBlankLast(p.name, q.name, direction))
}

const percent = (part: number | null, whole: number | null) =>
  part === null || whole === null || whole === 0 ? null : (part / whole) * 100

export function buildFunnel(options: VisualizationOptions, rows: Rows): FunnelModel {
  const list = entries(options, rows)
  const first = list.length > 0 ? list[0].amount : null
  const steps = list.map((e, i) => ({
    name: e.name,
    value: e.amount,
    pctOfFirst: percent(e.amount, first),
    pctOfPrevious: i === 0 ? null : percent(e.amount, list[i - 1].amount),
  }))
  const problem =
    rows.length > MAX_FUNNEL_STEPS
      ? FUNNEL_TOO_MANY
      : list.some((e) => e.amount !== null && e.amount < 0)
        ? FUNNEL_NEGATIVE
        : null
  return { kind: 'funnel', steps, skipped: list.filter((e) => e.bad).length, problem }
}

// Sorted on the raw value: display labels drop a midnight time, and Date's own parsing would shift
// date-only and no-offset text by the viewer's time zone.
function sortKey(raw: unknown): number | null {
  const n = toNumber(raw)
  return typeof n === 'number' ? n : parseDateX(raw)
}

export function buildTrend(options: VisualizationOptions, rows: Rows): TrendModel {
  const measure = options.measures[0]
  let skipped = 0
  const keyed: { point: TrendPoint; key: number | null }[] = []
  for (const row of rows) {
    const rawX = options.x ? cellValue(row, options.x) : null
    if (rawX === null || rawX === undefined || rawX === '') {
      skipped += 1
      continue
    }
    const raw = measure ? toNumber(cellValue(row, measure)) : null
    if (raw === 'bad') skipped += 1
    keyed.push({ point: { x: formatCategory(rawX), y: raw === 'bad' ? null : raw }, key: sortKey(rawX) })
  }
  keyed.sort((p, q) =>
    p.key !== null && q.key !== null && p.key !== q.key ? p.key - q.key : compareCategories(p.point.x, q.point.x),
  )
  const points = keyed.map((k) => k.point)

  const filled = points.filter((p) => p.y !== null)
  const last = filled.length > 0 ? filled[filled.length - 1] : null
  const before = filled.length > 1 ? filled[filled.length - 2] : null
  const change = last && before ? (last.y as number) - (before.y as number) : null
  return {
    kind: 'trend',
    latest: last ? last.y : null,
    latestLabel: last ? last.x : '',
    previous: before ? before.y : null,
    previousLabel: before ? before.x : null,
    change,
    changePct: change !== null && before!.y !== 0 ? (change / Math.abs(before!.y as number)) * 100 : null,
    points,
    skipped,
  }
}

export function buildWaterfall(options: VisualizationOptions, rows: Rows): WaterfallModel {
  const bars: WaterfallBar[] = []
  let running = 0
  let skipped = 0
  for (const e of entries(options, rows)) {
    if (e.bad) skipped += 1
    if (e.amount === null) {
      if (!e.bad) skipped += 1
      continue
    }
    const end = running + e.amount
    bars.push({
      name: e.name,
      base: Math.min(running, end),
      size: Math.abs(e.amount),
      kind: e.amount < 0 ? 'down' : 'up',
      value: e.amount,
    })
    running = end
  }
  if (bars.length > 0) {
    bars.push({ name: TOTAL_LABEL, base: Math.min(0, running), size: Math.abs(running), kind: 'total', value: running })
  }
  return { kind: 'waterfall', bars, skipped, problem: rows.length > MAX_WATERFALL_BARS ? WATERFALL_TOO_MANY : null }
}
