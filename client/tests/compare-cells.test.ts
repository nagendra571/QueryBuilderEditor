import assert from 'node:assert/strict'
import { test } from 'node:test'
import { compareCells } from '../src/features/builder/results/compare-cells.ts'

test('numeric text sorts by value in a number column', () => {
  assert.ok(compareCells('1.25', '1.5', true) < 0)
  assert.ok(compareCells('10.5', '9.75', true) > 0)
})

test('text that is not a number falls back to collation, and nulls sort last', () => {
  assert.ok(compareCells('abc', '1.5', true) > 0)
  assert.ok(compareCells(null, '1', true) > 0)
  assert.equal(compareCells(1, 2), -1)
})
