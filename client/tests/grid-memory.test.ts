import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  createGridMemory,
  restoredScroll,
  restoredSelection,
  sameView,
} from '../src/features/builder/results/grid-memory.ts'

const rows = [{ a: 1 }]
const range = { anchor: { row: 1, col: 0 }, focus: { row: 3, col: 2 } }

test('sameView needs the same key and the same rows object', () => {
  assert.equal(sameView({ resetKey: 'k', data: rows }, 'k', rows), true)
  assert.equal(sameView({ resetKey: 'k', data: rows }, 'other', rows), false)
  assert.equal(sameView({ resetKey: 'k', data: rows }, 'k', [{ a: 1 }]), false)
  assert.equal(sameView(null, 'k', rows), false)
  assert.equal(sameView(undefined, 'k', rows), false)
})

test('a fresh memory restores nothing', () => {
  const m = createGridMemory()
  assert.deepEqual(restoredSelection(m, 'k', rows), { resetKey: 'k', data: rows, range: null })
  assert.deepEqual(restoredScroll(m, 'k', rows), { top: 0, left: 0 })
  assert.deepEqual(restoredSelection(undefined, 'k', rows), { resetKey: 'k', data: rows, range: null })
  assert.deepEqual(restoredScroll(undefined, 'k', rows), { top: 0, left: 0 })
})

test('the same view gets its selection and scroll back after a remount', () => {
  const m = createGridMemory()
  m.saveSelection({ resetKey: 'k', data: rows, range })
  m.saveScroll({ resetKey: 'k', data: rows, top: 880, left: 312 })
  assert.equal(restoredSelection(m, 'k', rows).range, range)
  assert.deepEqual(restoredScroll(m, 'k', rows), { top: 880, left: 312 })
})

test('a changed search, sort, layout or run drops what was saved', () => {
  const m = createGridMemory()
  m.saveSelection({ resetKey: 'k', data: rows, range })
  m.saveScroll({ resetKey: 'k', data: rows, top: 880, left: 312 })
  assert.deepEqual(restoredSelection(m, 'k2', rows), { resetKey: 'k2', data: rows, range: null })
  assert.deepEqual(restoredScroll(m, 'k2', rows), { top: 0, left: 0 })
  const rerun = [{ a: 1 }]
  assert.equal(restoredSelection(m, 'k', rerun).range, null)
  assert.deepEqual(restoredScroll(m, 'k', rerun), { top: 0, left: 0 })
})

test('a later save replaces the earlier one', () => {
  const m = createGridMemory()
  m.saveScroll({ resetKey: 'k', data: rows, top: 10, left: 0 })
  m.saveScroll({ resetKey: 'k', data: rows, top: 40, left: 5 })
  assert.deepEqual(restoredScroll(m, 'k', rows), { top: 40, left: 5 })
  m.saveSelection({ resetKey: 'k', data: rows, range: null })
  assert.equal(restoredSelection(m, 'k', rows).range, null)
})
