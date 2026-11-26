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
