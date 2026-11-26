import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  MAX_TOTAL_CELLS,
  decimalsOf,
  formatTotal,
  selectionTotals,
} from '../src/features/builder/results/selection-totals.ts'

const cols = [
  { name: 'a', dataType: 'number' },
  { name: 'b', dataType: 'text' },
] as const

const rows = [
  { a: 1, b: 'x' },
  { a: '2.5', b: null },
  { a: null, b: 'y' },
]

test('numbers and text over a rectangle', () => {
  const t = selectionTotals(rows, [...cols], { top: 0, bottom: 2, left: 0, right: 1 })
  assert.deepEqual(t, {
    cells: 6,
    count: 4,
    numeric: { sum: 3.5, avg: 1.75, min: 1, max: 2.5, decimals: 1 },
    skipped: false,
  })
})

test('a text-only rectangle has no numeric totals', () => {
  const t = selectionTotals(rows, [...cols], { top: 0, bottom: 2, left: 1, right: 1 })
  assert.equal(t.numeric, null)
  assert.equal(t.count, 2)
})

test('all-empty cells count zero', () => {
  const t = selectionTotals([{ a: null }, { a: '' }, { a: undefined }], [cols[0]], {
    top: 0,
    bottom: 2,
    left: 0,
    right: 0,
  })
  assert.equal(t.count, 0)
  assert.equal(t.numeric, null)
})

test('a huge range is skipped but still counted', () => {
  const big = Array.from({ length: MAX_TOTAL_CELLS + 1 }, () => ({ a: 1 }))
  const t = selectionTotals(big, [cols[0]], { top: 0, bottom: big.length - 1, left: 0, right: 0 })
  assert.equal(t.skipped, true)
  assert.equal(t.numeric, null)
  assert.equal(t.count, MAX_TOTAL_CELLS + 1)
})

test('formatTotal and decimalsOf', () => {
  assert.equal(formatTotal(1234.5, 1), (1234.5).toLocaleString(undefined, { maximumFractionDigits: 1 }))
  assert.equal(decimalsOf(2.25), 2)
  assert.equal(decimalsOf(3), 0)
  assert.equal(decimalsOf(1e-7), 0)
})
