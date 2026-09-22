import type { AggregateFunction, ColumnDataType } from '@/types'

export const AGGREGATE_LABELS: Record<AggregateFunction, string> = {
  none: 'No total',
  sum: 'Sum',
  avg: 'Average',
  count: 'Count',
  countDistinct: 'Distinct count',
  min: 'Min',
  max: 'Max',
}

const NUMERIC_ONLY: AggregateFunction[] = ['sum', 'avg']
const ALWAYS_AVAILABLE: AggregateFunction[] = ['count', 'countDistinct', 'min', 'max']

export function aggregatesForDataType(dataType: ColumnDataType): AggregateFunction[] {
  if (dataType === 'boolean') {
    return ['none', 'count', 'countDistinct']
  }
  if (dataType === 'number') {
    return ['none', ...NUMERIC_ONLY, ...ALWAYS_AVAILABLE]
  }
  return ['none', ...ALWAYS_AVAILABLE]
}

/** What data type the aggregated value itself is — Sum/Avg/Count always produce a number, Min/Max keep the source type. */
export function aggregateResultDataType(sourceDataType: ColumnDataType, aggregate: AggregateFunction): ColumnDataType {
  if (aggregate === 'none' || aggregate === 'min' || aggregate === 'max') return sourceDataType
  return 'number'
}

export function describeAggregate(aggregate: AggregateFunction, columnName: string): string {
  if (aggregate === 'none') return columnName
  return `${AGGREGATE_LABELS[aggregate]} of ${columnName}`
}
