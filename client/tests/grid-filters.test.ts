import test from 'node:test'
import assert from 'node:assert/strict'
import {
  chipText,
  filterUi,
  hideValue,
  isEmptyFilter,
  matchesFilter,
  removeFilter,
  rowsPassing,
  setFilter,
  showOnly,
  valueKey,
  valueOptions,
  type GridFilter,
} from '../src/features/builder/results/grid-filters.ts'

const range = (column: string, min: number | null, max: number | null, includeEmpty = false): GridFilter => ({
  kind: 'range',
  column,
  min,
  max,
  includeEmpty,
})
const values = (
  column: string,
  v: (string | number | boolean | null)[],
  mode: 'include' | 'exclude' = 'include',
): GridFilter => ({ kind: 'values', column, mode, values: v })
const DAY = Date.UTC(2025, 0, 1)

test('filterUi picks range for number and dates', () => {
  assert.equal(filterUi('number'), 'range')
  assert.equal(filterUi('date'), 'range')
  assert.equal(filterUi('dateTime'), 'range')
  assert.equal(filterUi('text'), 'values')
  assert.equal(filterUi('boolean'), 'values')
})

test('valueKey normalises per column type', () => {
  assert.equal(valueKey('12.50', 'number'), 12.5)
  assert.equal(valueKey('$1,234', 'number'), '$1,234')
  assert.equal(valueKey(null, 'number'), null)
  assert.equal(valueKey('', 'text'), null)
  assert.equal(valueKey(null, 'text'), null)
  assert.equal(valueKey('Acme', 'text'), 'Acme')
  assert.equal(valueKey(true, 'boolean'), true)
})

test('matchesFilter: numbers', () => {
  assert.equal(matchesFilter('12.50', range('a', 12.5, 12.5), 'number'), true)
  assert.equal(matchesFilter('$1,234', range('a', null, null), 'number'), false)
  assert.equal(matchesFilter('$1,234', range('a', 0, 5000, true), 'number'), false)
  assert.equal(matchesFilter(13, range('a', 12.5, 12.5), 'number'), false)
  assert.equal(matchesFilter(5, range('a', null, 10), 'number'), true)
})

test('matchesFilter: empties', () => {
  assert.equal(matchesFilter(null, values('a', [null]), 'text'), true)
  assert.equal(matchesFilter('', values('a', [null]), 'text'), true)
  assert.equal(matchesFilter(null, range('a', 1, 2), 'number'), false)
  assert.equal(matchesFilter(null, range('a', 1, 2, true), 'number'), true)
})

test('matchesFilter: dates', () => {
  const f = range('d', DAY, DAY + 86_399_999)
  assert.equal(matchesFilter('2025-01-01T00:00:00', f, 'dateTime'), true)
  assert.equal(matchesFilter('2025-01-01T23:59:59', f, 'dateTime'), true)
  assert.equal(matchesFilter('2025-01-02T00:00:00', f, 'dateTime'), false)
  assert.equal(matchesFilter('nonsense', f, 'dateTime'), false)
  assert.equal(matchesFilter('2025-01-01', range('d', DAY, DAY), 'date'), true)
})

test('matchesFilter: values are exact, booleans and exclude work', () => {
  assert.equal(matchesFilter(true, values('b', [true]), 'boolean'), true)
  assert.equal(matchesFilter('acme', values('a', ['Acme']), 'text'), false)
  assert.equal(matchesFilter('Acme', values('a', ['Acme'], 'exclude'), 'text'), false)
  assert.equal(matchesFilter('Other', values('a', ['Acme'], 'exclude'), 'text'), true)
})

test('isEmptyFilter', () => {
  assert.equal(isEmptyFilter(values('a', [])), true)
  assert.equal(isEmptyFilter(values('a', [], 'exclude')), false)
  assert.equal(isEmptyFilter(range('a', null, null)), true)
  assert.equal(isEmptyFilter(range('a', null, null, true)), false)
  assert.equal(isEmptyFilter(range('a', 0, null)), false)
})

test('setFilter replaces in place, appends, and drops empties', () => {
  const base = [values('a', ['x']), values('b', ['y'])]
  assert.deepEqual(setFilter(base, values('a', ['z'])), [values('a', ['z']), values('b', ['y'])])
  assert.deepEqual(
    setFilter(base, values('c', ['q'])).map((f) => f.column),
    ['a', 'b', 'c'],
  )
  assert.deepEqual(setFilter(base, values('a', [])), [values('b', ['y'])])
  assert.deepEqual(removeFilter(base, 'b'), [values('a', ['x'])])
})

test('showOnly and hideValue', () => {
  assert.deepEqual(showOnly([range('a', 1, 2)], 'a', 'x'), [values('a', ['x'])])
  let f = hideValue([], 'a', 'x')
  f = hideValue(f, 'a', 'y')
  assert.deepEqual(f, [values('a', ['x', 'y'], 'exclude')])
  assert.deepEqual(hideValue(f, 'a', 'x'), f)
  assert.deepEqual(hideValue([values('a', ['x'])], 'a', 'y'), [values('a', ['y'], 'exclude')])
})

test('valueOptions counts, orders and caps', () => {
  const col = { name: 'c', dataType: 'text' } as never
  const rows = [{ c: 'b' }, { c: 'a' }, { c: 'b' }, { c: null }, { c: '' }, { c: 'c' }]
  const { options, truncated } = valueOptions(rows, col)
  assert.equal(truncated, false)
  assert.deepEqual(
    options.map((o) => [o.label, o.count]),
    [
      ['(empty)', 2],
      ['b', 2],
      ['a', 1],
      ['c', 1],
    ],
  )
  assert.equal(options[0].value, null)
  const many = Array.from({ length: 1005 }, (_, i) => ({ c: `v${i}` }))
  const r = valueOptions(many, col, 1000)
  assert.equal(r.options.length, 1000)
  assert.equal(r.truncated, true)
  assert.equal(valueOptions(many, col).options.length, 1000)
})

test('rowsPassing honours exceptColumn', () => {
  const columns = [
    { name: 'a', dataType: 'text' },
    { name: 'n', dataType: 'number' },
  ] as never[]
  const rows = [
    { a: 'x', n: 1 },
    { a: 'y', n: 2 },
    { a: 'x', n: 3 },
  ]
  const filters = [values('a', ['x']), range('n', 2, null)]
  assert.deepEqual(rowsPassing(rows, filters, columns), [{ a: 'x', n: 3 }])
  assert.deepEqual(rowsPassing(rows, filters, columns, 'a'), [
    { a: 'y', n: 2 },
    { a: 'x', n: 3 },
  ])
  assert.equal(rowsPassing(rows, [], columns), rows)
})

test('chipText', () => {
  assert.equal(chipText(values('a', ['A', 'B', 'C', 'D']), 'text').text, 'is A, B +2 more')
  assert.equal(chipText(values('a', ['A', 'B', 'C', 'D']), 'text').title, 'is A, B, C, D')
  assert.equal(chipText(values('a', ['A']), 'text').text, 'is A')
  assert.equal(chipText(values('a', ['A'], 'exclude'), 'text').text, 'is not A')
  assert.equal(chipText(values('a', [null]), 'text').text, 'is (empty)')
  assert.equal(chipText(range('n', 100, null), 'number').text, '≥ 100')
  assert.equal(chipText(range('n', null, 500), 'number').text, '≤ 500')
  assert.equal(chipText(range('n', 100, 500, true), 'number').text, '100 – 500 + empty')
  assert.equal(chipText(range('n', 100, 500), 'number').column, 'n')
  const d = range('d', Date.UTC(2025, 0, 1), Date.UTC(2025, 2, 31) + 86_399_999)
  assert.equal(chipText(d, 'dateTime').text, '2025-01-01 – 2025-03-31')
  assert.equal(chipText(range('d', DAY, null), 'date').text, '≥ 2025-01-01')
})
