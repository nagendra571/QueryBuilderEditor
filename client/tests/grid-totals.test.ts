import test from 'node:test'
import assert from 'node:assert/strict'
import type { QueryColumn, QueryResultColumnDto } from '../src/types/index.ts'
import {
  defaultTotal,
  totalChoices,
  totalValue,
  withAggregates,
  type TotalFn,
} from '../src/features/builder/results/grid-totals.ts'

const col = (dataType: QueryResultColumnDto['dataType'], aggregate?: QueryResultColumnDto['aggregate']): QueryResultColumnDto => ({
  name: 'X',
  dataType,
  aggregate,
})
const rowsOf = (...vals: unknown[]) => vals.map((X) => ({ X }))

test('number choices and default', () => {
  assert.deepEqual(totalChoices(col('number')), ['sum', 'avg', 'count', 'min', 'max', 'none'])
  assert.equal(defaultTotal(col('number')), 'sum')
  for (const a of ['avg', 'min', 'max', 'countDistinct'] as const) {
    assert.ok(!totalChoices(col('number', a)).includes('sum'), a)
    assert.equal(defaultTotal(col('number', a)), 'none')
  }
  for (const a of ['sum', 'count'] as const) {
    assert.ok(totalChoices(col('number', a)).includes('sum'), a)
    assert.equal(defaultTotal(col('number', a)), 'sum')
  }
})

test('date and text choices and defaults', () => {
  for (const t of ['date', 'dateTime'] as const) {
    assert.deepEqual(totalChoices(col(t)), ['earliest', 'latest', 'count', 'none'])
    assert.equal(defaultTotal(col(t)), 'latest')
  }
  for (const t of ['text', 'guid', 'unknown', 'boolean'] as const) {
    assert.deepEqual(totalChoices(col(t)), ['count', 'distinct', 'none'])
    assert.equal(defaultTotal(col(t)), 'count')
  }
})

test('sum, avg, min, max, count, distinct', () => {
  const rows = rowsOf(1, 2, 2, null, '', undefined, 5)
  const c = col('number')
  assert.equal(totalValue(rows, c, 'sum', String), '10')
  assert.equal(totalValue(rows, c, 'min', String), '1')
  assert.equal(totalValue(rows, c, 'max', String), '5')
  assert.equal(totalValue(rows, c, 'count', String), '4')
  assert.equal(totalValue(rows, c, 'distinct', String), '3')
  assert.equal(totalValue(rows, c, 'none', String), '')
})

test('earliest and latest return the text of the original value', () => {
  const rows = rowsOf('2024-03-01T00:00:00', '2023-01-05T00:00:00', null, '2025-07-09T00:00:00')
  const c = col('dateTime')
  assert.equal(totalValue(rows, c, 'earliest', (v) => `<${v}>`), '<2023-01-05T00:00:00>')
  assert.equal(totalValue(rows, c, 'latest', (v) => `<${v}>`), '<2025-07-09T00:00:00>')
})

test('rounds like selection totals', () => {
  assert.equal(totalValue(rowsOf(0.1, 0.2), col('number'), 'sum', String), '0.3')
  assert.equal(totalValue(rowsOf(1, 2), col('number'), 'avg', String), '1.5')
})

test('totals skip non-numbers', () => {
  assert.equal(totalValue(rowsOf(10, '9999999999999999999999.5', null, 5), col('number'), 'sum', String), '15')
})

test('numeric text is skipped and large real numbers are kept', () => {
  assert.equal(totalValue(rowsOf(10, '12', 5), col('number'), 'sum', String), '15')
  assert.equal(totalValue(rowsOf(1e18, 1), col('number'), 'max', String), String(1e18))
})

test('empty rows', () => {
  const fns: TotalFn[] = ['sum', 'avg', 'min', 'max', 'earliest', 'latest', 'none']
  for (const fn of fns) assert.equal(totalValue([], col('number'), fn, String), '', fn)
  assert.equal(totalValue([], col('number'), 'count', String), '0')
  assert.equal(totalValue([], col('number'), 'distinct', String), '0')
})

test('withAggregates copies the aggregate from the matching visible column', () => {
  const q = (columnName: string, aggregate: QueryColumn['aggregate'], extra: Partial<QueryColumn> = {}): QueryColumn => ({
    tableAlias: 't',
    columnName,
    dataType: 'number',
    aggregate,
    orderIndex: 0,
    isVisible: true,
    ...extra,
  })
  const columns: QueryResultColumnDto[] = [
    { name: 'Total', dataType: 'number' },
    { name: 'Other', dataType: 'number' },
    { name: 'Dup', dataType: 'number' },
  ]
  const out = withAggregates(columns, {
    columns: [
      q('Amount', 'avg', { alias: 'Total' }),
      q('Dup', 'sum'),
      q('Other', 'min', { alias: 'Dup' }),
      q('Other', 'max', { isVisible: false }),
    ],
  })
  assert.equal(out[0].aggregate, 'avg')
  assert.equal(out[1].aggregate, undefined)
  assert.equal(out[2].aggregate, undefined)
  assert.equal(withAggregates(columns, undefined), columns)
})
