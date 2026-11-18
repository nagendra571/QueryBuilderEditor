import type { QueryResultColumnDto, VisualizationOptions, VisualizationType } from '@/types'
import { buildModel, buildScatter, formatCategory } from '../visualizations/chart-data.ts'
import { formatValue } from '../visualizations/format.ts'
import { pickRow, rowsOf, seriesRowsOf } from '../visualizations/mark-sources.ts'
import { counterText } from '../visualizations/single-number.ts'
import { buildFunnel, buildTrend, buildWaterfall } from '../visualizations/story-models.ts'
import { buildPivot, MAX_PIVOT_COLUMNS } from '../visualizations/pivot-model.ts'
import { isAggregated, stepsForMark } from './drill-model.ts'
import type { MarkWithoutAnchor } from './mark-click.ts'

export interface ExploreItem {
  id: string
  label: string
  value: string
  mark: MarkWithoutAnchor
}

export const MAX_EXPLORE_ITEMS = 200

type Row = Record<string, unknown>

/** The chart's marks as a list, for people who cannot aim a pointer. Only marks a click could also drill. */
export function exploreItems(
  type: VisualizationType,
  options: VisualizationOptions,
  rows: Row[],
  columns: QueryResultColumnDto[],
  keyColumns: string[] = [],
): { items: ExploreItem[]; more: number } {
  const all: ExploreItem[] = []
  const format = options.format
  const add = (id: string, label: string, value: string, mark: MarkWithoutAnchor) => {
    if (stepsForMark(type, options, columns, keyColumns, { ...mark, anchor: { x: 0, y: 0 } }) !== null) {
      all.push({ id, label, value, mark })
    }
  }
  const none = { items: [], more: 0 }
  // Where a renderer shows a message instead of the chart there are no marks to explore.
  if (rows.length === 0 && type !== 'counter') return none
  const num = (v: number | null) => (v === null ? '' : formatValue(v, format))

  if (type === 'pivot') {
    const model = buildPivot(options, rows, (n) => columns.find((c) => c.name === n)?.aggregate)
    if (model.columnHeaders.length > MAX_PIVOT_COLUMNS) return none
    const rowNames = options.pivot?.rows ?? []
    const columnName = options.pivot?.column || null
    model.rowHeaders.forEach((header, ri) => {
      const rowValues: Record<string, unknown> = {}
      rowNames.forEach((n, k) => {
        rowValues[n] = model.rowRaw[ri][k]
      })
      model.cells[ri].forEach((cells, ci) => {
        const first = cells[0]
        if (!first || (first.value === null && !first.notCombinable)) return
        const columnValue = columnName ? { column: columnName, value: model.columnRaw[ci] } : undefined
        const label = [header.join(' / '), columnName ? model.columnHeaders[ci] : ''].filter(Boolean).join(' · ')
        add(`p${ri}:${ci}`, label || 'All', first.notCombinable ? '—' : num(first.value), { pivot: { rowValues, columnValue } })
      })
    })
  } else if (type === 'scatter') {
    const model = buildScatter(options, rows)
    if (model.problem) return none
    model.groups.forEach((g, gi) =>
      g.points.forEach((p, i) => {
        const row = pickRow(rowsOf(p), keyColumns)
        if (!row) return
        const label = [p.label, options.series ? g.name : null].filter((s): s is string => !!s).join(' · ')
        add(`s${gi}:${i}`, label || `Point ${i + 1}`, num(p.y), { row })
      }),
    )
  } else if (type === 'funnel') {
    const funnel = buildFunnel(options, rows)
    if (funnel.problem) return none
    funnel.steps.forEach((s, i) => {
      const row = pickRow(rowsOf(s), keyColumns)
      if (row) add(`f${i}`, s.name, num(s.value), { row })
    })
  } else if (type === 'waterfall') {
    const waterfall = buildWaterfall(options, rows)
    if (waterfall.problem) return none
    waterfall.bars.forEach((b, i) => {
      const row = pickRow(rowsOf(b), keyColumns)
      if (row) add(`w${i}`, b.name, num(b.value), { row })
    })
  } else if (type === 'trend') {
    buildTrend(options, rows).points.forEach((p, i) => {
      const row = pickRow(rowsOf(p), keyColumns)
      if (row) add(`t${i}`, p.x, num(p.y), { row })
    })
  } else {
    const model = buildModel(type, options, rows)
    if (model.kind === 'counter') {
      add('c', model.label || 'Value', counterText(model.value, options.counter?.decimals ?? 0, format), {})
    } else if (model.kind === 'pie') {
      if (model.problem) return none
      model.slices.forEach((s, i) => {
        const row = pickRow(rowsOf(s), keyColumns)
        if (row) add(`pie${i}`, s.name, num(s.value), { row })
      })
    } else if (model.kind === 'cartesian') {
      model.points.forEach((p, i) => {
        // Without a split every measure's bar is the same row, so one entry per point is enough.
        const shown = options.series ? model.series : model.series.slice(0, 1)
        for (const s of shown) {
          const v = p[s.key]
          if (v === null || v === undefined) continue
          const row = pickRow(seriesRowsOf(p, s.key), keyColumns)
          if (!row) continue
          const label = options.series ? `${p.x} · ${s.label}` : String(p.x)
          add(`c${i}:${s.key}`, label, typeof v === 'number' ? num(v) : String(v), { row })
        }
      })
    } else if (model.kind === 'table' && isAggregated(columns)) {
      const shown = columns.filter((c) => !c.aggregate || c.aggregate === 'none').slice(0, 3)
      model.rows.forEach((row, i) => {
        const label = shown.map((c) => formatCategory(row[c.name])).join(' · ')
        add(`r${i}`, label || `Row ${i + 1}`, '', { row })
      })
    }
  }

  return { items: all.slice(0, MAX_EXPLORE_ITEMS), more: Math.max(0, all.length - MAX_EXPLORE_ITEMS) }
}
