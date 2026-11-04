import type { VisualizationType } from '@/types'

/** A type joins its group in the task that builds its view; the select hides groups with no types. */
export const TYPE_GROUPS: { label: string; types: VisualizationType[] }[] = [
  { label: 'Compare', types: ['bar', 'horizontalBar', 'combo', 'waterfall'] },
  { label: 'Trend', types: ['line', 'area', 'trend'] },
  { label: 'Part of whole', types: ['pie', 'funnel'] },
  { label: 'Relationship', types: ['scatter'] },
  { label: 'Table', types: ['table', 'pivot'] },
  { label: 'Single number', types: ['counter'] },
]

// Beyond the five theme tokens, lighter tints keep slices apart; legend text carries the identity
// so colour is never the only cue.
export function paletteColor(index: number): string {
  const base = `var(--chart-${(index % 5) + 1})`
  const round = Math.floor(index / 5)
  if (round === 0) return base
  return `color-mix(in oklab, ${base}, var(--background) ${Math.min(round * 35, 70)}%)`
}
