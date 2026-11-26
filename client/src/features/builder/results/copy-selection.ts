import type { ColumnDataType } from '@/types'
import { cellToClipboardText, toTsv } from './clipboard-tsv.ts'

export interface CopyColumn {
  name: string
  dataType: ColumnDataType
}

export interface CopyRange {
  top: number
  bottom: number
  left: number
  right: number
}

// Display order in, display order out; `range` null means everything shown.
export function buildCopyText(
  columns: CopyColumn[],
  rows: Record<string, unknown>[],
  range: CopyRange | null,
): string {
  const cols = range ? columns.slice(range.left, range.right + 1) : columns
  const shown = range ? rows.slice(range.top, range.bottom + 1) : rows
  return toTsv(
    cols.map((c) => c.name),
    shown.map((r) => cols.map((c) => cellToClipboardText(r[c.name], c.dataType))),
  )
}

export function buildRecordText(row: Record<string, unknown>, columns: CopyColumn[]): string {
  return toTsv(
    ['Column', 'Value'],
    columns.map((c) => [c.name, cellToClipboardText(row[c.name], c.dataType)]),
  )
}
