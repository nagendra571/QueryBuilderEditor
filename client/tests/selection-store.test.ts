import { test } from 'node:test'
import assert from 'node:assert/strict'
import { createSelectionStore } from '../src/features/builder/results/selection-store.ts'

const range = (r: number, c: number, r2 = r, c2 = c) => ({ anchor: { row: r, col: c }, focus: { row: r2, col: c2 } })

test('get() is stable across calls', () => {
  const s = createSelectionStore()
  assert.equal(s.get(), s.get())
})

test('an equal range in a new object does not notify or replace the snapshot', () => {
  const s = createSelectionStore()
  s.publish({ range: range(1, 2) })
  const before = s.get()
  let calls = 0
  s.subscribe(() => calls++)
  s.publish({ range: range(1, 2) })
  assert.equal(calls, 0)
  assert.equal(s.get(), before)
})

test('a different range notifies once and replaces the snapshot', () => {
  const s = createSelectionStore()
  s.publish({ range: range(1, 2) })
  const before = s.get()
  let calls = 0
  s.subscribe(() => calls++)
  s.publish({ range: range(1, 3) })
  assert.equal(calls, 1)
  assert.notEqual(s.get(), before)
  assert.deepEqual(s.get().range, range(1, 3))
})

test('publishing the same rows array does not notify; a new one does', () => {
  const s = createSelectionStore()
  const rows = [{ a: 1 }]
  s.publish({ rows })
  let calls = 0
  s.subscribe(() => calls++)
  s.publish({ rows })
  assert.equal(calls, 0)
  s.publish({ rows: [{ a: 1 }] })
  assert.equal(calls, 1)
})

test('null to null range does not notify', () => {
  const s = createSelectionStore()
  let calls = 0
  s.subscribe(() => calls++)
  s.publish({ range: null })
  assert.equal(calls, 0)
})

test('unsubscribe stops notifications', () => {
  const s = createSelectionStore()
  let calls = 0
  const off = s.subscribe(() => calls++)
  s.publish({ range: range(0, 0) })
  off()
  s.publish({ range: range(1, 1) })
  assert.equal(calls, 1)
})
