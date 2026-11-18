type Row = Record<string, unknown>

// Side tables, not model fields: a drawn point must stay a plain value for charts and tests, yet a click has to find
// the result row(s) it came from. Keyed by the model's own objects, so a sorted or re-ordered model keeps the link.
const rowsOfMark = new WeakMap<object, Row[]>()
const rowsOfSeries = new WeakMap<object, Record<string, Row[]>>()

export function tagRows<T extends object>(mark: T, rows: Row[]): T {
  rowsOfMark.set(mark, rows)
  return mark
}

export function tagSeriesRows<T extends object>(point: T, bySeries: Record<string, Row[]>): T {
  rowsOfSeries.set(point, bySeries)
  return point
}

export const rowsOf = (mark: object | undefined): Row[] => (mark ? (rowsOfMark.get(mark) ?? []) : [])
export const seriesRowsOf = (point: object | undefined, seriesKey: string): Row[] =>
  (point ? rowsOfSeries.get(point)?.[seriesKey] : undefined) ?? []

/** One result row identifies the mark. Several rows share it only when the chart summed them, which is
 * safe to drill by the shown columns alone but not when key columns tell those rows apart. */
export function pickRow(rows: Row[], keyColumns?: string[] | null): Row | undefined {
  if (rows.length === 1) return rows[0]
  if (rows.length > 1 && !(keyColumns && keyColumns.length > 0)) return rows[0]
  return undefined
}

/** A click on empty plot space at one X can't say which line it meant. With several series it only resolves when
 * every series carries the same row there; otherwise the dot under the pointer decides instead. */
export function rowAtX(
  point: object | undefined,
  seriesKeys: string[],
  keyColumns?: string[] | null,
): Row | undefined {
  const picked = seriesKeys.map((k) => pickRow(seriesRowsOf(point, k), keyColumns))
  if (picked.length === 0 || picked[0] === undefined) return undefined
  return picked.every((r) => r === picked[0]) ? picked[0] : undefined
}

export const pointIndexOf = (state: { activeTooltipIndex?: unknown } | undefined): number | undefined => {
  const n = typeof state?.activeTooltipIndex === 'string' ? Number(state.activeTooltipIndex) : state?.activeTooltipIndex
  return typeof n === 'number' && Number.isInteger(n) && n >= 0 ? n : undefined
}
