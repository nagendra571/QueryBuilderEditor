import test from 'node:test'
import assert from 'node:assert/strict'
import { displayOrder, displayString, matchesSearch } from '../src/features/builder/results/grid-columns.ts'
import { defaultView } from '../src/features/builder/results/grid-view.ts'

test('displayString', () => {
  assert.equal(displayString(null), '')
  assert.equal(displayString(undefined), '')
  assert.equal(displayString(1234.5), (1234.5).toLocaleString())
  assert.equal(displayString(true), 'true')
})

test('matchesSearch is case-insensitive, trimmed, and empty matches all', () => {
  assert.equal(matchesSearch('Hello', ' ELL '), true)
  assert.equal(matchesSearch(null, 'x'), false)
  assert.equal(matchesSearch('a', '   '), true)
})

test('displayOrder puts pins at the edges and drops hidden', () => {
  const names = ['a', 'b', 'c', 'd', 'e']
  const v = { ...defaultView(names), pinnedLeft: ['c'], pinnedRight: ['a'], hidden: ['d'] }
  assert.deepEqual(displayOrder(v, names), ['c', 'b', 'e', 'a'])
})
