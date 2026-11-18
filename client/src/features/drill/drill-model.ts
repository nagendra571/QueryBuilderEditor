import type {
  ColumnDataType,
  QueryResultColumnDto,
  VisualizationOptions,
  VisualizationType,
} from '@/types'
import { referencedColumns } from '../visualizations/chart-data.ts'
import type { TrailStep } from './drill-url.ts'

export type DrillStep = TrailStep

export type DrillMenuItem = 'rows' | 'breakdown' | 'filter'

export interface ClickedMark {
  row?: Record<string, unknown>
  pivot?: { rowValues: Record<string, unknown>; columnValue?: { column: string; value: unknown } }
  anchor: { x: number; y: number }
}

/** Dates and numbers go back exactly as the server wrote them: re-parsing a date would change its text. */
export function toWireValue(value: unknown, dataType: ColumnDataType): string | null {
  if (value === null || value === undefined) return null
  if (typeof value === 'boolean') return value ? 'true' : 'false'
  if (dataType === 'boolean' && typeof value === 'number') return value ? 'true' : 'false'
  return typeof value === 'string' ? value : String(value)
}

const isPlain = (c: QueryResultColumnDto) => !c.aggregate || c.aggregate === 'none'

export function isAggregated(columns: QueryResultColumnDto[]): boolean {
  return columns.some((c) => !isPlain(c))
}

export function stepsForMark(
  type: VisualizationType,
  options: VisualizationOptions,
  columns: QueryResultColumnDto[],
  keyColumns: string[],
  mark: ClickedMark,
): DrillStep[] | null {
  const byName = new Map(columns.map((c) => [c.name, c]))
  const typeOf = (name: string): ColumnDataType => byName.get(name)?.dataType ?? 'unknown'

  if (type === 'counter') return []

  if (type === 'pivot') {
    if (!mark.pivot) return null
    const steps: DrillStep[] = []
    const rowNames = options.pivot?.rows ?? Object.keys(mark.pivot.rowValues)
    for (const name of rowNames) {
      if (name in mark.pivot.rowValues) {
        steps.push({ column: name, value: toWireValue(mark.pivot.rowValues[name], typeOf(name)) })
      }
    }
    const col = mark.pivot.columnValue
    if (col) steps.push({ column: col.column, value: toWireValue(col.value, typeOf(col.column)) })
    return steps
  }

  const row = mark.row
  if (!row) return null

  if (type === 'table' && !isAggregated(columns)) return null
  if (type === 'scatter' && !options.label && !options.series) return null

  const referenced = new Set(referencedColumns(type, options))
  const keys = new Set(keyColumns)
  const names: string[] = []
  for (const c of columns) {
    if (isPlain(c) && (referenced.has(c.name) || keys.has(c.name))) names.push(c.name)
  }
  for (const k of keyColumns) if (!byName.has(k) && !names.includes(k)) names.push(k)

  return names.map((name) => ({ column: name, value: toWireValue(row[name], typeOf(name)) }))
}

const NO_BREAKDOWN: VisualizationType[] = ['trend', 'pivot', 'scatter', 'counter', 'table']

export function menuItems(ctx: {
  type: VisualizationType
  aggregated: boolean
  onDashboard: boolean
  clickToFilter: boolean
}): DrillMenuItem[] {
  const items: DrillMenuItem[] = ['rows']
  if (ctx.aggregated && !NO_BREAKDOWN.includes(ctx.type)) items.push('breakdown')
  if (ctx.onDashboard && ctx.clickToFilter) items.push('filter')
  return items
}

export function breakdownChoices(allowed: string[], trail: DrillStep[]): string[] {
  const used = new Set(trail.map((s) => s.column))
  return allowed.filter((c) => !used.has(c))
}
