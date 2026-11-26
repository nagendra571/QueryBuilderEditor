import assert from 'node:assert/strict'
import { test } from 'node:test'
import { layoutColumns, moveColumn, pinColumn } from '../src/features/builder/results/grid-layout.ts'
import { defaultView } from '../src/features/builder/results/grid-view.ts'

test('sticky offsets accumulate and the edges are marked', () => {
  const l = layoutColumns(['a', 'b', 'c', 'd', 'e'], [100, 110, 120, 130, 140], ['a', 'b'], ['d', 'e'])
  assert.deepEqual(l.map((c) => c.stickyLeft), [44, 144, undefined, undefined, undefined])
  assert.deepEqual(l.map((c) => c.stickyRight), [undefined, undefined, undefined, 140, 0])
  assert.deepEqual(l.map((c) => c.edge), [false, true, false, true, false])
  assert.equal(l[2].x, 254)
})

test('pinning moves a column between lists: left appends, right prepends', () => {
  let v = defaultView(['a', 'b', 'c'])
  v = pinColumn(v, 'a', 'left')
  v = pinColumn(v, 'b', 'left')
  v = pinColumn(v, 'c', 'right')
  v = pinColumn(v, 'a', 'right')
  assert.deepEqual([v.pinnedLeft, v.pinnedRight], [['b'], ['a', 'c']])
  assert.deepEqual(pinColumn(v, 'a', null).pinnedRight, ['c'])
})

test('moveColumn reorders within a group only', () => {
  const v = pinColumn(defaultView(['a', 'b', 'c', 'd']), 'a', 'left')
  assert.deepEqual(moveColumn(v, 'd', 'b').order, ['a', 'd', 'b', 'c'])
  assert.equal(moveColumn(v, 'a', 'b'), v)
})

test('columnWindow renders pinned columns always and spaces skipped centre columns', async () => {
  const { columnWindow } = await import('../src/features/builder/results/grid-layout.ts')
  const l = layoutColumns(['a', 'b', 'c', 'd', 'e', 'f'], [100, 110, 120, 130, 140, 150], ['a'], ['f'])
  const all = columnWindow(l, null, null)
  assert.deepEqual(all, { left: [0], centre: [1, 2, 3, 4], right: [5], before: 0, after: 0 })
  const w = columnWindow(l, 1, 2)
  assert.deepEqual(w, { left: [0], centre: [2, 3], right: [5], before: 110, after: 140 })
  const clamped = columnWindow(l, 3, 99)
  assert.deepEqual(clamped.centre, [4])
  assert.equal(clamped.before, 110 + 120 + 130)
  assert.equal(clamped.after, 0)
})
