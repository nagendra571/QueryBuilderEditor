import type { ColumnDataType } from '@/types'

export const TYPE_BADGE: Record<ColumnDataType, string> = {
  text: 'ABC',
  number: '123',
  date: 'DATE',
  // A dateTime is usually a date with a midnight time, so TIME read as a time-of-day column.
  dateTime: 'DATE',
  boolean: 'Y/N',
  guid: 'ID',
  unknown: '?',
}
