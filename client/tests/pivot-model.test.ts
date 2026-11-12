import assert from 'node:assert/strict'
import { test } from 'node:test'
import { PIVOT_TOO_WIDE, buildPivot, shadeFraction } from '../src/features/visualizations/pivot-model.ts'
import type { AggregateFunction, PivotOptions } from '../src/types/index.ts'

const pivot = (patch: Partial<PivotOptions>): PivotOptions => ({
  rows: ['Region'],
  column: 'Year',
  values: ['Sales'],
  totals: true,
  shade: false,
  ...patch,
})
const opts = (p: PivotOptions) => ({ version: 1, measures: [], stacked: false, sortX: 'none' as const, pivot: p })
const agg =
  (a: AggregateFunction | null | undefined) =>
  () =>
    a

const rows = [
  { Region: 'East', Year: '2024', Sales: 10 },
  { Region: 'East', Year: '2024', Sales: 5 },
  { Region: 'East', Year: '2025', Sales: 20 },
  { Region: 'West', Year: '2024', Sales: 7 },
]

test('sum: two rows in one cell add up, and every total adds up', () => {
  const m = buildPivot(opts(pivot({})), rows, agg('sum'))
  assert.deepEqual(m.rowHeaders, [['East'], ['West']])
  assert.deepEqual(m.columnHeaders, ['2024', '2025'])
  assert.equal(m.cells[0][0][0].value, 15)
  assert.equal(m.cells[0][1][0].value, 20)
  assert.deepEqual(m.cells[1][1][0], { value: null, notCombinable: false })
  assert.equal(m.columnTotals![0][0].value, 35)
  assert.equal(m.columnTotals![1][0].value, 7)
  assert.equal(m.rowTotals![0][0].value, 22)
  assert.equal(m.rowTotals![1][0].value, 20)
  assert.equal(m.grandTotals![0].value, 42)
  assert.equal(m.hasNotCombinable, false)
})

test('count and none add like sum', () => {
  for (const a of ['count', 'none', null, undefined] as const) {
    const m = buildPivot(opts(pivot({})), rows, agg(a))
    assert.equal(m.cells[0][0][0].value, 15)
    assert.equal(m.grandTotals![0].value, 42)
  }
})

test('min and max combine by min and max in cells and totals', () => {
  const min = buildPivot(opts(pivot({})), rows, agg('min'))
  assert.equal(min.cells[0][0][0].value, 5)
  assert.equal(min.columnTotals![0][0].value, 5)
  assert.equal(min.rowTotals![0][0].value, 5)
  assert.equal(min.grandTotals![0].value, 5)
  const max = buildPivot(opts(pivot({})), rows, agg('max'))
  assert.equal(max.cells[0][0][0].value, 10)
  assert.equal(max.columnTotals![0][0].value, 20)
  assert.equal(max.grandTotals![0].value, 20)
})

test('avg and countDistinct: one row shows, several are not combinable, all totals are not combinable', () => {
  for (const a of ['avg', 'countDistinct'] as const) {
    const m = buildPivot(opts(pivot({})), rows, agg(a))
    assert.deepEqual(m.cells[0][0][0], { value: null, notCombinable: true })
    assert.deepEqual(m.cells[0][1][0], { value: 20, notCombinable: false })
    assert.deepEqual(m.cells[1][1][0], { value: null, notCombinable: false })
    for (const t of [...m.columnTotals!, ...m.rowTotals!]) assert.equal(t[0].notCombinable, true)
    assert.equal(m.grandTotals![0].notCombinable, true)
    assert.equal(m.hasNotCombinable, true)
  }
})

test('hasNotCombinable is false when totals are off and every cell holds one row', () => {
  const single = [
    { Region: 'East', Year: '2024', Sales: 1 },
    { Region: 'West', Year: '2024', Sales: 2 },
  ]
  const m = buildPivot(opts(pivot({ totals: false })), single, agg('avg'))
  assert.equal(m.hasNotCombinable, false)
})

test('a missing combination is blank, never zero', () => {
  const m = buildPivot(opts(pivot({ totals: false })), rows, agg('sum'))
  assert.deepEqual(m.cells[1][1][0], { value: null, notCombinable: false })
})

test('a null measure is ignored and null headers go under (blank), last', () => {
  const data = [
    { Region: null, Year: '2024', Sales: 3 },
    { Region: 'East', Year: null, Sales: 4 },
    { Region: 'East', Year: '2024', Sales: null },
    { Region: 'East', Year: '2024', Sales: 6 },
  ]
  const m = buildPivot(opts(pivot({})), data, agg('sum'))
  assert.deepEqual(m.rowHeaders, [['East'], ['(blank)']])
  assert.deepEqual(m.columnHeaders, ['2024', '(blank)'])
  assert.equal(m.cells[0][0][0].value, 6)
  assert.equal(m.cells[0][1][0].value, 4)
  assert.equal(m.cells[1][0][0].value, 3)
})

test('a cell with only null measures stays missing', () => {
  const m = buildPivot(opts(pivot({})), [{ Region: 'East', Year: '2024', Sales: null }], agg('sum'))
  assert.deepEqual(m.cells[0][0][0], { value: null, notCombinable: false })
  assert.equal(m.grandTotals![0].value, null)
})

test('columns named like Object.prototype members work in any role', () => {
  for (const name of ['constructor', '__proto__', 'a.b', 'toString']) {
    const data = [
      { [name]: 'x', Year: '2024', Sales: 1 },
      { [name]: 'x', Year: '2024', Sales: 2 },
    ]
    const asRow = buildPivot(opts(pivot({ rows: [name] })), data, agg('sum'))
    assert.deepEqual(asRow.rowHeaders, [['x']])
    assert.equal(asRow.cells[0][0][0].value, 3)

    const asCol = buildPivot(opts(pivot({ rows: ['Year'], column: name })), data, agg('sum'))
    assert.deepEqual(asCol.columnHeaders, ['x'])
    assert.equal(asCol.cells[0][0][0].value, 3)

    const valueRows = [{ Region: 'East', Year: '2024', [name]: 4 }]
    const asValue = buildPivot(opts(pivot({ values: [name] })), valueRows, agg('sum'))
    assert.equal(asValue.cells[0][0][0].value, 4)
  }
})

test('a column absent from the rows reads as missing, not as an inherited property', () => {
  const m = buildPivot(opts(pivot({ values: ['constructor'] })), [{ Region: 'East', Year: '2024' }], agg('sum'))
  assert.deepEqual(m.cells[0][0][0], { value: null, notCombinable: false })
  assert.deepEqual(m.rowHeaders, [['East']])
})

test('no column: one implicit column and no totals column', () => {
  const m = buildPivot(opts(pivot({ column: null })), rows, agg('sum'))
  assert.equal(m.columnHeaders.length, 1)
  assert.equal(m.cells[0].length, 1)
  assert.equal(m.cells[0][0][0].value, 35)
  assert.equal(m.cells[1][0][0].value, 7)
  assert.equal(m.columnTotals, null)
  assert.equal(m.grandTotals, null)
  assert.equal(m.rowTotals![0][0].value, 42)
})

test('totals off: every total is null', () => {
  const m = buildPivot(opts(pivot({ totals: false })), rows, agg('sum'))
  assert.equal(m.rowTotals, null)
  assert.equal(m.columnTotals, null)
  assert.equal(m.grandTotals, null)
})

test('headers sort with the category order: numbers numerically, text naturally, blank last', () => {
  const data = [
    { Region: '10', Year: 'b', Sales: 1 },
    { Region: '9', Year: 'a', Sales: 1 },
    { Region: '', Year: 'a', Sales: 1 },
    { Region: '100', Year: 'a', Sales: 1 },
  ]
  const m = buildPivot(opts(pivot({})), data, agg('sum'))
  assert.deepEqual(m.rowHeaders, [['9'], ['10'], ['100'], ['(blank)']])
})

test('multi-level rows sort by each level and a separator in a value cannot collide', () => {
  const data = [
    { A: 'x|y', B: 'z', Year: '1', Sales: 1 },
    { A: 'x', B: 'y|z', Year: '1', Sales: 2 },
    { A: 'x', B: 'y', Year: '1', Sales: 4 },
  ]
  const m = buildPivot(opts(pivot({ rows: ['A', 'B'] })), data, agg('sum'))
  assert.equal(m.rowHeaders.length, 3)
  assert.deepEqual(m.rowHeaders[0], ['x', 'y'])
  assert.equal(m.cells[0][0][0].value, 4)
})

test('several values: ranges are per value and ignore not-combinable and missing cells', () => {
  const data = [
    { Region: 'East', Year: '2024', Sales: 10, Units: 100 },
    { Region: 'West', Year: '2024', Sales: 30, Units: 300 },
    { Region: 'West', Year: '2025', Sales: 20, Units: 200 },
  ]
  const m = buildPivot(opts(pivot({ values: ['Sales', 'Units'] })), data, agg('sum'))
  assert.deepEqual(m.values, ['Sales', 'Units'])
  assert.deepEqual(m.valueRanges, [
    { min: 10, max: 30 },
    { min: 100, max: 300 },
  ])
})

test('shadeFraction is null for equal range, null, and not-combinable; otherwise 0 to 1', () => {
  assert.equal(shadeFraction({ value: 5, notCombinable: false }, { min: 5, max: 5 }), null)
  assert.equal(shadeFraction({ value: null, notCombinable: false }, { min: 0, max: 5 }), null)
  assert.equal(shadeFraction({ value: 3, notCombinable: true }, { min: 0, max: 5 }), null)
  assert.equal(shadeFraction({ value: 0, notCombinable: false }, { min: 0, max: 10 }), 0)
  assert.equal(shadeFraction({ value: 5, notCombinable: false }, { min: 0, max: 10 }), 0.5)
  assert.equal(shadeFraction({ value: 10, notCombinable: false }, { min: 0, max: 10 }), 1)
})

test('a range with no numeric cells does not break shading', () => {
  const m = buildPivot(opts(pivot({})), [{ Region: 'East', Year: '2024', Sales: null }], agg('sum'))
  assert.equal(shadeFraction(m.cells[0][0][0], m.valueRanges[0]), null)
})

test('1,000 rows build in under 200 ms', () => {
  const data = Array.from({ length: 1000 }, (_, i) => ({ Region: `R${i % 40}`, Year: `${2000 + (i % 25)}`, Sales: i }))
  const start = performance.now()
  buildPivot(opts(pivot({})), data, agg('sum'))
  assert.ok(performance.now() - start < 200)
})

test('the too-wide message matches the server wording', () => {
  assert.equal(PIVOT_TOO_WIDE, 'Too many columns for a pivot table (max 50); add filters or choose a different column.')
})

const aggBy = (map: Record<string, AggregateFunction>) => (name: string) => map[name]

test('totals default to on when the option is absent', () => {
  const p = { rows: ['Region'], column: 'Year', values: ['Sales'], shade: false } as unknown as PivotOptions
  const m = buildPivot(opts(p), rows, agg('sum'))
  assert.notEqual(m.grandTotals, null)
})

test('bad measure values are counted as skipped, numeric strings are used', () => {
  const data = [
    { Region: 'East', Year: '2024', Sales: 'N/A' },
    { Region: 'East', Year: '2024', Sales: '4' },
    { Region: 'East', Year: '2024', Sales: 1 },
  ]
  const m = buildPivot(opts(pivot({})), data, agg('sum'))
  assert.equal(m.skipped, 1)
  assert.equal(m.cells[0][0][0].value, 5)
  assert.equal(buildPivot(opts(pivot({})), rows, agg('sum')).skipped, 0)
})

test('mixed aggregates: sum totals add while avg totals are not combinable', () => {
  const data = [
    { Region: 'East', Year: '2024', Sales: 10, Avg: 1 },
    { Region: 'East', Year: '2025', Sales: 20, Avg: 2 },
  ]
  const m = buildPivot(opts(pivot({ values: ['Sales', 'Avg'] })), data, aggBy({ Sales: 'sum', Avg: 'avg' }))
  assert.equal(m.grandTotals![0].value, 30)
  assert.equal(m.grandTotals![1].notCombinable, true)
  assert.equal(m.columnTotals![0][0].value, 30)
  assert.equal(m.columnTotals![0][1].notCombinable, true)
  assert.equal(m.cells[0][0][1].value, 1)
  assert.equal(m.hasNotCombinable, true)
})

test('an avg cell with one null and one value shows the value', () => {
  const data = [
    { Region: 'East', Year: '2024', Sales: null },
    { Region: 'East', Year: '2024', Sales: 8 },
  ]
  const m = buildPivot(opts(pivot({ totals: false })), data, agg('avg'))
  assert.deepEqual(m.cells[0][0][0], { value: 8, notCombinable: false })
  assert.equal(m.hasNotCombinable, false)
})

test('ranges ignore not-combinable cells', () => {
  const data = [
    { Region: 'East', Year: '2024', Sales: 1 },
    { Region: 'East', Year: '2024', Sales: 100 },
    { Region: 'West', Year: '2024', Sales: 10 },
    { Region: 'West', Year: '2025', Sales: 20 },
  ]
  const m = buildPivot(opts(pivot({ totals: false })), data, agg('avg'))
  assert.equal(m.cells[0][0][0].notCombinable, true)
  assert.deepEqual(m.valueRanges, [{ min: 10, max: 20 }])
})

test('numerically equal but distinct headers keep their parent grouped', () => {
  const data = [
    { A: '1', B: 'x', Year: '1', Sales: 1 },
    { A: '1.0', B: 'x', Year: '1', Sales: 1 },
    { A: '1', B: 'y', Year: '1', Sales: 1 },
    { A: '1.0', B: 'y', Year: '1', Sales: 1 },
  ]
  const m = buildPivot(opts(pivot({ rows: ['A', 'B'] })), data, agg('sum'))
  const parents = m.rowHeaders.map((h) => h[0])
  assert.deepEqual(parents, [...parents].sort().sort((a, b) => parents.indexOf(a) - parents.indexOf(b)))
  assert.equal(new Set(parents.slice(0, 2)).size, 1)
})
