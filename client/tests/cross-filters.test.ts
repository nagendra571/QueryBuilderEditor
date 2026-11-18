import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  addFilter,
  chipLabel,
  clearFilters,
  filterStepFor,
  removeFilter,
  tileFilters,
} from '../src/features/dashboards/cross-filters.ts'
import type { VisualizationOptions } from '../src/types/index.ts'

const f = (column: string, value: string | null, sourceTileId = 't1') => ({ sourceTileId, column, value })
const opts = (o: Partial<VisualizationOptions>): VisualizationOptions => ({
  version: 1,
  measures: [],
  stacked: false,
  sortX: 'none',
  ...o,
})

test('addFilter appends and replaces an existing filter on the same column', () => {
  const one = addFilter([], f('Region', 'West'))
  assert.deepEqual(one, [f('Region', 'West')])
  const two = addFilter(one!, f('Status', 'Open'))
  assert.equal(two!.length, 2)
  assert.deepEqual(addFilter(two!, f('Region', 'East', 't2')), [f('Region', 'East', 't2'), f('Status', 'Open')])
})

test('addFilter refuses a sixth distinct column but still replaces when full', () => {
  const five = ['a', 'b', 'c', 'd', 'e'].map((c) => f(c, '1'))
  assert.equal(addFilter(five, f('z', '1')), null)
  assert.equal(addFilter(five, f('c', '2'))!.length, 5)
})

test('removeFilter and clearFilters', () => {
  const list = [f('a', '1'), f('b', '2')]
  assert.deepEqual(removeFilter(list, 'a'), [f('b', '2')])
  assert.deepEqual(removeFilter(list, 'zzz'), list)
  assert.deepEqual(clearFilters(), [])
})

test('tiles send filters only when the switch is on and the list is not empty', () => {
  const list = [f('a', '1')]
  assert.equal(tileFilters(false, list), undefined)
  assert.equal(tileFilters(true, []), undefined)
  assert.deepEqual(tileFilters(true, list), list)
  assert.equal(tileFilters(true, Array.from({ length: 9 }, (_, i) => f(`c${i}`, '1')))!.length, 5)
})

test('chipLabel shows column = value, with blank and null spelled out', () => {
  assert.equal(chipLabel(f('Region', 'West')), 'Region = West')
  assert.equal(chipLabel(f('Region', null)), 'Region = (none)')
  assert.equal(chipLabel(f('Region', '')), 'Region = (blank)')
})

test('filterStepFor picks the step on the chart X', () => {
  const steps = [{ column: 'Status', value: 'Open' }, { column: 'Region', value: 'West' }]
  assert.deepEqual(filterStepFor('bar', opts({ x: 'Region', series: 'Status' }), steps), steps[1])
  assert.equal(filterStepFor('bar', opts({ x: 'Other' }), steps), null)
  assert.equal(filterStepFor('counter', opts({}), []), null)
})

test('filterStepFor on a pivot prefers the first row field, then the column field', () => {
  const steps = [{ column: 'Region', value: 'West' }, { column: 'Year', value: '2026' }]
  assert.deepEqual(filterStepFor('pivot', opts({ pivot: { rows: ['Region'], column: 'Year', values: [] } as never }), steps), steps[0])
  assert.deepEqual(
    filterStepFor('pivot', opts({ pivot: { rows: [], column: 'Year', values: [] } as never }), steps),
    steps[1],
  )
})
