import assert from 'node:assert/strict'
import { test } from 'node:test'
import { withSavedAggregates, breakdownChoices, menuItems, stepsForMark, toWireValue } from '../src/features/drill/drill-model.ts'
import type { QueryResultColumnDto, VisualizationOptions } from '../src/types/index.ts'

const anchor = { x: 1, y: 2 }
const opts = (o: Partial<VisualizationOptions>): VisualizationOptions => ({
  version: 1,
  measures: [],
  stacked: false,
  sortX: 'none',
  ...o,
})
const dim = (name: string, dataType: QueryResultColumnDto['dataType'] = 'text'): QueryResultColumnDto => ({
  name,
  dataType,
  aggregate: 'none',
})
const agg = (name: string): QueryResultColumnDto => ({ name, dataType: 'number', aggregate: 'sum' })

test('toWireValue sends values as the server wrote them', () => {
  assert.equal(toWireValue(null, 'text'), null)
  assert.equal(toWireValue(undefined, 'text'), null)
  assert.equal(toWireValue('', 'text'), '')
  assert.equal(toWireValue('2026-01-02T03:04:05.1234567', 'dateTime'), '2026-01-02T03:04:05.1234567')
  assert.equal(toWireValue('2026-01-02', 'date'), '2026-01-02')
  assert.equal(toWireValue(12.5, 'number'), '12.5')
  assert.equal(toWireValue(1e21, 'number'), '1e+21')
  assert.equal(toWireValue(true, 'boolean'), 'true')
  assert.equal(toWireValue(false, 'boolean'), 'false')
})

test('bar: x, series and the row key columns of the clicked row', () => {
  const columns = [dim('Program'), dim('Status'), dim('Site'), agg('Total')]
  const o = opts({ x: 'Program', series: 'Status', measures: ['Total'] })
  const row = { Program: 20, Status: 'Active', Site: 'N', Total: 140 }
  assert.deepEqual(stepsForMark('bar', o, columns, ['Site'], { row, anchor }), [
    { column: 'Program', value: '20' },
    { column: 'Status', value: 'Active' },
    { column: 'Site', value: 'N' },
  ])
  assert.equal(stepsForMark('bar', o, columns, ['Site'], { anchor }), null)
})

test('line, area, combo, trend, horizontal bar, pie and funnel use the same row rule', () => {
  const columns = [dim('Month', 'date'), agg('Total')]
  const row = { Month: '2026-03-01', Total: 5 }
  for (const t of ['line', 'area', 'combo', 'trend', 'horizontalBar', 'pie', 'funnel'] as const) {
    assert.deepEqual(stepsForMark(t, opts({ x: 'Month', measures: ['Total'] }), columns, [], { row, anchor }), [
      { column: 'Month', value: '2026-03-01' },
    ])
  }
})

test('waterfall total bar (no row) is not clickable', () => {
  const columns = [dim('Step'), agg('Total')]
  const o = opts({ x: 'Step', measures: ['Total'] })
  assert.equal(stepsForMark('waterfall', o, columns, [], { anchor }), null)
  assert.deepEqual(stepsForMark('waterfall', o, columns, [], { row: { Step: 'A', Total: 1 }, anchor }), [
    { column: 'Step', value: 'A' },
  ])
})

test('scatter needs a label or series', () => {
  const columns = [dim('Name'), agg('X'), agg('Y')]
  const row = { Name: 'n', X: 1, Y: 2 }
  assert.equal(stepsForMark('scatter', opts({ x: 'X', measures: ['Y'] }), [agg('X'), agg('Y')], [], { row, anchor }), null)
  assert.deepEqual(stepsForMark('scatter', opts({ x: 'X', measures: ['Y'], label: 'Name' }), columns, [], { row, anchor }), [
    { column: 'Name', value: 'n' },
  ])
})

test('pivot: row values plus column value; grand total has no steps', () => {
  const columns = [dim('Program'), dim('Status'), agg('Total')]
  const o = opts({ pivot: { rows: ['Program'], column: 'Status', values: ['Total'], totals: true, shade: false } })
  assert.deepEqual(
    stepsForMark('pivot', o, columns, [], {
      pivot: { rowValues: { Program: 20 }, columnValue: { column: 'Status', value: 'Active' } },
      anchor,
    }),
    [
      { column: 'Program', value: '20' },
      { column: 'Status', value: 'Active' },
    ],
  )
  assert.deepEqual(stepsForMark('pivot', o, columns, [], { pivot: { rowValues: { Program: null } }, anchor }), [
    { column: 'Program', value: null },
  ])
  assert.deepEqual(stepsForMark('pivot', o, columns, [], { pivot: { rowValues: {} }, anchor }), [])
  assert.equal(stepsForMark('pivot', o, columns, [], { anchor }), null)
})

test('counter has no steps', () => {
  const o = opts({ counter: { value: 'Total', decimals: 0 } })
  assert.deepEqual(stepsForMark('counter', o, [agg('Total')], [], { anchor }), [])
})

test('table: every non-aggregated column of the row; not drillable on a non-aggregated query', () => {
  const o = opts({ table: { columns: ['Program', 'Total'] } })
  const columns = [dim('Program'), dim('Site'), agg('Total')]
  assert.deepEqual(stepsForMark('table', o, columns, ['Site'], { row: { Program: 'P', Site: 'S', Total: 1 }, anchor }), [
    { column: 'Program', value: 'P' },
    { column: 'Site', value: 'S' },
  ])
  assert.equal(
    stepsForMark('table', o, [dim('Program'), dim('Total', 'number')], [], { row: { Program: 'P', Total: 1 }, anchor }),
    null,
  )
})

test('a mark whose key is unsafe or missing is not clickable', () => {
  const columns = [dim('Id', 'number'), agg('Total')]
  const o = opts({ x: 'Id', measures: ['Total'] })
  assert.equal(stepsForMark('bar', o, columns, [], { row: { Id: 9007199254740993, Total: 1 }, anchor }), null)
  assert.deepEqual(stepsForMark('bar', o, columns, [], { row: { Id: 9007199254740991, Total: 1 }, anchor }), [
    { column: 'Id', value: '9007199254740991' },
  ])
  assert.equal(stepsForMark('bar', o, columns, [], { row: { Total: 1 }, anchor }), null)
  assert.deepEqual(stepsForMark('bar', o, columns, [], { row: { Id: null, Total: 1 }, anchor }), [{ column: 'Id', value: null }])
  const po = opts({ pivot: { rows: ['Id'], values: ['Total'], totals: true, shade: false } })
  assert.equal(stepsForMark('pivot', po, columns, [], { pivot: { rowValues: { Id: 2 ** 60 } }, anchor }), null)
  assert.equal(stepsForMark('pivot', po, columns, [], { pivot: { rowValues: { Id: undefined } }, anchor }), null)
})

test('menuItems', () => {
  const base = { aggregated: true, onDashboard: true, clickToFilter: true }
  assert.deepEqual(menuItems({ ...base, type: 'bar' }), ['rows', 'breakdown', 'filter'])
  assert.deepEqual(menuItems({ ...base, type: 'bar', onDashboard: false }), ['rows', 'breakdown'])
  assert.deepEqual(menuItems({ ...base, type: 'bar', clickToFilter: false }), ['rows', 'breakdown'])
  assert.deepEqual(menuItems({ ...base, type: 'bar', aggregated: false }), ['rows', 'filter'])
  for (const t of ['trend', 'pivot', 'scatter', 'counter', 'table'] as const) {
    assert.ok(!menuItems({ ...base, type: t }).includes('breakdown'), t)
    assert.ok(menuItems({ ...base, type: t }).includes('rows'), t)
  }
})

test('breakdownChoices drops columns already in the trail', () => {
  assert.deepEqual(breakdownChoices(['A', 'B', 'C'], [{ column: 'B', value: 'x' }]), ['A', 'C'])
  assert.deepEqual(breakdownChoices([], []), [])
})

test('withSavedAggregates reads totals from the saved definition by output name', () => {
  const col = (name: string): QueryResultColumnDto => ({ name, dataType: 'text', aggregate: null } as unknown as QueryResultColumnDto)
  const saved = [
    { tableAlias: 't', columnName: 'Budget', alias: 'Total', dataType: 'decimal', aggregate: 'sum', orderIndex: 1, isVisible: true },
    { tableAlias: 't', columnName: 'Program', alias: null, dataType: 'text', aggregate: 'none', orderIndex: 0, isVisible: true },
    { tableAlias: 't', columnName: 'Hidden', alias: null, dataType: 'decimal', aggregate: 'sum', orderIndex: 2, isVisible: false },
  ] as never
  const out = withSavedAggregates([col('Program'), col('Total'), col('Hidden'), col('Other')], saved)
  assert.deepEqual(out.map((c) => c.aggregate), ['none', 'sum', null, null])
})
