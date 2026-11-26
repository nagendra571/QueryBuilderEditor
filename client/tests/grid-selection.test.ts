import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  clampRange,
  moveFocus,
  normalize,
  rangeContains,
  rangeSize,
  selectAll,
} from '../src/features/builder/results/selection.ts'

const b = { rows: 10, cols: 4 }

test('normalize orders corners', () => {
  assert.deepEqual(normalize({ anchor: { row: 5, col: 3 }, focus: { row: 2, col: 1 } }), {
    top: 2,
    bottom: 5,
    left: 1,
    right: 3,
  })
})

test('moveFocus clamps at edges', () => {
  assert.deepEqual(moveFocus({ row: 0, col: 0 }, 'up', b, 5), { row: 0, col: 0 })
  assert.deepEqual(moveFocus({ row: 0, col: 0 }, 'left', b, 5), { row: 0, col: 0 })
  assert.deepEqual(moveFocus({ row: 9, col: 3 }, 'down', b, 5), { row: 9, col: 3 })
  assert.deepEqual(moveFocus({ row: 9, col: 3 }, 'right', b, 5), { row: 9, col: 3 })
  assert.deepEqual(moveFocus({ row: 4, col: 2 }, 'down', b, 5), { row: 5, col: 2 })
})

test('page keys move by pageRows, keep the column, and clamp', () => {
  assert.deepEqual(moveFocus({ row: 2, col: 1 }, 'pageDown', b, 5), { row: 7, col: 1 })
  assert.deepEqual(moveFocus({ row: 7, col: 1 }, 'pageDown', b, 5), { row: 9, col: 1 })
  assert.deepEqual(moveFocus({ row: 2, col: 1 }, 'pageUp', b, 5), { row: 0, col: 1 })
})

test('home, end and ctrl variants', () => {
  assert.deepEqual(moveFocus({ row: 4, col: 2 }, 'home', b, 5), { row: 4, col: 0 })
  assert.deepEqual(moveFocus({ row: 4, col: 2 }, 'end', b, 5), { row: 4, col: 3 })
  assert.deepEqual(moveFocus({ row: 4, col: 2 }, 'ctrlHome', b, 5), { row: 0, col: 0 })
  assert.deepEqual(moveFocus({ row: 4, col: 2 }, 'ctrlEnd', b, 5), { row: 9, col: 3 })
})

test('moveFocus on empty bounds returns the origin', () => {
  assert.deepEqual(moveFocus({ row: 3, col: 3 }, 'down', { rows: 0, cols: 3 }, 5), { row: 0, col: 0 })
  assert.deepEqual(moveFocus({ row: 3, col: 3 }, 'ctrlEnd', { rows: 3, cols: 0 }, 5), { row: 0, col: 0 })
})

test('selectAll', () => {
  assert.equal(selectAll({ rows: 0, cols: 3 }), null)
  assert.equal(selectAll({ rows: 3, cols: 0 }), null)
  assert.deepEqual(selectAll({ rows: 2, cols: 3 }), {
    anchor: { row: 0, col: 0 },
    focus: { row: 1, col: 2 },
  })
})

test('clampRange shrinks to new bounds and is null when empty', () => {
  const r = { anchor: { row: 1, col: 1 }, focus: { row: 20, col: 9 } }
  assert.deepEqual(clampRange(r, { rows: 5, cols: 3 }), {
    anchor: { row: 1, col: 1 },
    focus: { row: 4, col: 2 },
  })
  assert.equal(clampRange(r, { rows: 0, cols: 3 }), null)
  assert.equal(clampRange(r, { rows: 3, cols: 0 }), null)
})

test('rangeSize and rangeContains', () => {
  const r = { anchor: { row: 5, col: 3 }, focus: { row: 2, col: 2 } }
  assert.equal(rangeSize(r), 8)
  for (const p of [
    { row: 2, col: 2 },
    { row: 5, col: 3 },
    { row: 2, col: 3 },
    { row: 5, col: 2 },
  ]) {
    assert.equal(rangeContains(r, p), true)
  }
  for (const p of [
    { row: 1, col: 2 },
    { row: 6, col: 3 },
    { row: 3, col: 1 },
    { row: 3, col: 4 },
  ]) {
    assert.equal(rangeContains(r, p), false)
  }
})

test('rowSlice gives each row only its own selected span and focus column', async () => {
  const { rowSlice } = await import('../src/features/builder/results/selection.ts')
  const range = { anchor: { row: 4, col: 3 }, focus: { row: 2, col: 1 } }
  assert.deepEqual(rowSlice(null, 2), { selLeft: -1, selRight: -1, focusCol: -1 })
  assert.deepEqual(rowSlice(range, 2), { selLeft: 1, selRight: 3, focusCol: 1 })
  assert.deepEqual(rowSlice(range, 3), { selLeft: 1, selRight: 3, focusCol: -1 })
  assert.deepEqual(rowSlice(range, 5), { selLeft: -1, selRight: -1, focusCol: -1 })
})
