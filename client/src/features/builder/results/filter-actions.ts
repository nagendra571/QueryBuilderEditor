import { displayString } from './grid-columns.ts'
import type { Bin } from './column-profile.ts'
import type { GridFilter } from './grid-filters.ts'
import type { ColumnDataType } from '@/types'

const MAX_LABEL = 40
const DAY_MS = 86_400_000

export function valueLabel(raw: unknown): string {
  if (raw === null || raw === undefined || raw === '') return '(empty)'
  const text = displayString(raw)
  return text.length > MAX_LABEL ? `${text.slice(0, MAX_LABEL)}…` : text
}

export const showOnlyText = (raw: unknown) => `Show only "${valueLabel(raw)}"`
export const hideText = (raw: unknown) => `Hide "${valueLabel(raw)}"`

// The server truncates conditions on date columns to the day, so a bar's bounds are snapped to whole days here
// to match what the same filter would return after a re-run.
export function binFilter(column: string, dataType: ColumnDataType, bin: Bin): GridFilter {
  const snap = (ms: number) => Math.floor(ms / DAY_MS) * DAY_MS
  const day = dataType === 'date'
  return {
    kind: 'range',
    column,
    min: day ? snap(bin.from) : bin.from,
    max: day ? snap(bin.to) : bin.to,
    includeEmpty: false,
  }
}
