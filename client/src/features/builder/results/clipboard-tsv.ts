import type { ColumnDataType } from '@/types'

export function cellToClipboardText(value: unknown, dataType: ColumnDataType): string {
  if (value === null || value === undefined) return ''
  if (dataType === 'boolean' && typeof value === 'boolean') return value ? 'TRUE' : 'FALSE'
  return String(value)
}

function field(text: string): string {
  return /[\t\n\r"]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text
}

export function toTsv(headers: string[], rows: string[][]): string {
  return [headers, ...rows].map((line) => line.map(field).join('\t')).join('\r\n')
}
