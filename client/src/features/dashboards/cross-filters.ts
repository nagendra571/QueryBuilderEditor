import type { VisualizationOptions, VisualizationType } from '@/types'
import { MAX_CROSS_FILTERS, type CrossFilterValue, type TrailStep } from '../drill/drill-url.ts'

/** Null when the list is full and the filter is on a new column: the caller tells the viewer. */
export function addFilter(list: CrossFilterValue[], filter: CrossFilterValue): CrossFilterValue[] | null {
  const at = list.findIndex((f) => f.column === filter.column)
  if (at >= 0) return list.map((f, i) => (i === at ? filter : f))
  return list.length >= MAX_CROSS_FILTERS ? null : [...list, filter]
}

export function removeFilter(list: CrossFilterValue[], column: string): CrossFilterValue[] {
  return list.filter((f) => f.column !== column)
}

export function clearFilters(): CrossFilterValue[] {
  return []
}

// The server refuses filters while the switch is off, so a link that still carries them must send none.
export function tileFilters(enabled: boolean, list: CrossFilterValue[]): CrossFilterValue[] | undefined {
  if (!enabled || list.length === 0) return undefined
  return list.slice(0, MAX_CROSS_FILTERS)
}

/** A link's filters can name a tile that was since removed (or is not a chart): the server would refuse every tile over it. */
export function liveFilters(list: CrossFilterValue[], chartTileIds: ReadonlySet<string>): CrossFilterValue[] {
  return list.filter((f) => chartTileIds.has(f.sourceTileId))
}

export function chipLabel(f: CrossFilterValue): string {
  return `${f.column} = ${f.value === null ? '(none)' : f.value === '' ? '(blank)' : f.value}`
}

/** The clicked mark's value for the column the chart is split by; null when the chart has no such column. */
export function filterStepFor(type: VisualizationType, options: VisualizationOptions, steps: TrailStep[]): TrailStep | null {
  const find = (column: string | null | undefined) => (column ? steps.find((s) => s.column === column) : undefined)
  if (type === 'pivot') {
    for (const r of options.pivot?.rows ?? []) {
      const s = find(r)
      if (s) return s
    }
    return find(options.pivot?.column) ?? null
  }
  return find(options.x) ?? null
}
