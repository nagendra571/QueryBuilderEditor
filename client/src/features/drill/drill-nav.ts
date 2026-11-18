import type { QueryResultColumnDto } from '@/types'
import { toWireValue, unusableKey } from './drill-model.ts'
import {
  decodeFilters,
  decodeTrail,
  encodeFilters,
  encodeTrail,
  MAX_TRAIL_STEPS,
  type CrossFilterValue,
  type TrailStep,
} from './drill-url.ts'

const PARAM_PREFIX = 'p_'

export interface DrillLink {
  queryId: string
  vizId?: string | null
  steps: TrailStep[]
  filters?: CrossFilterValue[]
  dashboardId?: string | null
  tileId?: string | null
  params?: Record<string, string | null>
  /** The clicked Count number: shown beside the row count, never sent to the server. */
  n?: number | null
}

export function drillPath(queryId: string, vizId?: string | null): string {
  return vizId ? `/queries/${queryId}/charts/${vizId}/drill` : `/queries/${queryId}/drill`
}

/** A parameter value goes into the address exactly as the server reads it, so a list stays one JSON-array value. */
export function drillSearch(link: Omit<DrillLink, 'queryId' | 'vizId'>): string {
  const search = new URLSearchParams()
  if (link.steps.length > 0) search.set('s', encodeTrail(link.steps))
  if (link.filters && link.filters.length > 0) search.set('f', encodeFilters(link.filters))
  if (link.dashboardId) search.set('d', link.dashboardId)
  if (link.tileId) search.set('t', link.tileId)
  if (link.n !== null && link.n !== undefined && Number.isFinite(link.n)) search.set('n', String(link.n))
  for (const [name, value] of Object.entries(link.params ?? {})) {
    if (value === null || value === undefined || value.trim() === '' || value.trim() === '[]') continue
    search.set(PARAM_PREFIX + name, value)
  }
  return search.toString()
}

export function drillUrl(link: DrillLink): string {
  const search = drillSearch(link)
  return search ? `${drillPath(link.queryId, link.vizId)}?${search}` : drillPath(link.queryId, link.vizId)
}

export interface ParsedDrill {
  /** Null when `s` is present but not a valid trail: the page says so instead of showing every row. */
  steps: TrailStep[] | null
  filters: CrossFilterValue[] | null
  dashboardId: string | null
  tileId: string | null
  params: Record<string, string | null>
  n: number | null
}

export function parseDrillSearch(search: URLSearchParams): ParsedDrill {
  const s = search.get('s')
  const f = search.get('f')
  const params: Record<string, string | null> = {}
  for (const key of new Set(search.keys())) {
    if (!key.startsWith(PARAM_PREFIX) || key.length === PARAM_PREFIX.length) continue
    const all = search.getAll(key)
    // Links made by the query page repeat the key for a list; this page writes one JSON array.
    params[key.slice(PARAM_PREFIX.length)] = all.length > 1 ? JSON.stringify(all) : all[0]
  }
  const rawN = search.get('n')
  const n = rawN !== null && /^\d{1,15}$/.test(rawN) ? Number(rawN) : null
  return {
    steps: s === null || s === '' ? [] : decodeTrail(s),
    filters: f === null || f === '' ? [] : decodeFilters(f),
    dashboardId: search.get('d') || null,
    tileId: search.get('t') || null,
    params,
    n,
  }
}

export function stepLabel(step: TrailStep): string {
  return step.value === null ? `${step.column} is blank` : `${step.column} = ${step.value}`
}

export interface Crumb {
  label: string
}

/** One crumb per step; choosing crumb `i` keeps the first `i + 1` steps, so it always leads to a shorter trail. */
export function stepCrumbs(steps: TrailStep[]): (Crumb & { keep: number })[] {
  return steps.map((step, i) => ({ label: stepLabel(step), keep: i + 1 }))
}

export function sameStep(a: TrailStep, b: TrailStep): boolean {
  return a.column === b.column && a.value === b.value
}

/** Steps carried from a chart that was already narrowed, plus the clicked mark's, without repeating one. */
export function mergeSteps(base: TrailStep[], added: TrailStep[]): TrailStep[] {
  const merged = [...base]
  for (const step of added) if (!merged.some((m) => sameStep(m, step))) merged.push(step)
  return merged
}

export function exceedsTrail(steps: TrailStep[]): boolean {
  return steps.length > MAX_TRAIL_STEPS
}

export interface BreakdownEntry {
  steps: TrailStep[]
  breakdownColumn: string
}

export function pushBreakdown(stack: BreakdownEntry[], entry: BreakdownEntry): BreakdownEntry[] {
  return [...stack, entry]
}

/** Keeps the first `keep` entries; 0 goes back to the chart as drawn. */
export function popBreakdown(stack: BreakdownEntry[], keep: number): BreakdownEntry[] {
  return keep >= stack.length ? stack : stack.slice(0, Math.max(0, keep))
}

export interface StackCrumb {
  label: string
  /** How many entries to keep when this crumb is chosen. */
  keep: number
}

/** The chart's own name, then one crumb per breakdown level naming what that level added. */
export function breakdownCrumbs(rootLabel: string, stack: BreakdownEntry[]): StackCrumb[] {
  const crumbs: StackCrumb[] = [{ label: rootLabel, keep: 0 }]
  stack.forEach((entry, i) => {
    const before = i === 0 ? [] : stack[i - 1].steps
    const added = entry.steps.filter((s) => !before.some((b) => sameStep(b, s)))
    crumbs.push({ label: [...added.map(stepLabel), `by ${entry.breakdownColumn}`].join(' · '), keep: i + 1 })
  })
  return crumbs
}

/** The chart's plain columns, the saved ones it does not draw, and its drill columns: everything a drill may name. */
export function allowedBreakdownColumns(
  columns: QueryResultColumnDto[],
  keyColumns: string[] | null | undefined,
  drillColumns: string[] | null | undefined,
): string[] {
  const out: string[] = []
  const add = (name: string) => {
    if (!out.includes(name)) out.push(name)
  }
  for (const c of columns) if (!c.aggregate || c.aggregate === 'none') add(c.name)
  for (const k of keyColumns ?? []) add(k)
  for (const d of drillColumns ?? []) add(d)
  return out
}

/** Columns already fixed by the trail, or the one the chart is currently grouped by, would only repeat themselves. */
export function breakdownChoicesFor(allowed: string[], steps: TrailStep[], current: BreakdownEntry | undefined): string[] {
  const used = new Set(steps.map((s) => s.column))
  if (current) used.add(current.breakdownColumn)
  return allowed.filter((c) => !used.has(c))
}

/** The number the person clicked, when it is a Count; other measures give no claim about the row total. */
export function clickedCount(columns: QueryResultColumnDto[], row: Record<string, unknown> | undefined): number | null {
  if (!row) return null
  const counts = columns.filter((c) => c.aggregate === 'count' && c.name in row)
  if (counts.length !== 1) return null
  const v = row[counts[0].name]
  return typeof v === 'number' && Number.isSafeInteger(v) && v >= 0 ? v : null
}

/** A results-grid row as a trail: every non-aggregated column. Null when a value cannot be sent back exactly. */
export function rowSteps(columns: QueryResultColumnDto[], row: Record<string, unknown>): TrailStep[] | null {
  const plain = columns.filter((c) => !c.aggregate || c.aggregate === 'none')
  if (plain.length === 0 || plain.length > MAX_TRAIL_STEPS) return null
  if (plain.some((c) => unusableKey(row[c.name]))) return null
  return plain.map((c) => ({ column: c.name, value: toWireValue(row[c.name], c.dataType) }))
}

/** `/queries/:id/charts/:id/drill` stays open to a hub Viewer: the server decides through the dashboard grant. */
export function isChartDrillPath(path: string): boolean {
  return /^\/queries\/[^/]+\/charts\/[^/]+\/drill\/?$/.test(path)
}

/** What a read-only parameter chip says: lists joined, ranges spelled out, anything else as written. */
export function describeParameterValue(value: string | null): string {
  if (value === null) return ''
  if (value.startsWith('[')) {
    try {
      const parsed: unknown = JSON.parse(value)
      if (Array.isArray(parsed)) return parsed.map(String).join(', ')
    } catch {
      /* a single value that merely starts with '[' */
    }
  }
  if (value.includes('~')) {
    const [start, end] = value.split('~')
    return `${start || '…'} to ${end || '…'}`
  }
  return value.replace(/_/g, ' ')
}
