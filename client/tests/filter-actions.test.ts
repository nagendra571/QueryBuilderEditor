import test from 'node:test'
import assert from 'node:assert/strict'
import { binFilter, hideText, showOnlyText, valueLabel } from '../src/features/builder/results/filter-actions.ts'

test('labels use the exact menu strings', () => {
  assert.equal(showOnlyText('Open'), 'Show only "Open"')
  assert.equal(hideText('Open'), 'Hide "Open"')
})

test('empty values read (empty)', () => {
  for (const v of [null, undefined, '']) assert.equal(valueLabel(v), '(empty)')
})

test('long labels are cut to 40 characters plus an ellipsis', () => {
  const long = 'x'.repeat(60)
  assert.equal(valueLabel(long), `${'x'.repeat(40)}…`)
  assert.equal(valueLabel('y'.repeat(40)), 'y'.repeat(40))
})

test('numbers are shown as the grid shows them', () => {
  assert.equal(valueLabel(5), '5')
})

test('a number bar keeps its bounds', () => {
  const f = binFilter('n', 'number', { from: 1.5, to: 3.25, count: 2 })
  assert.deepEqual(f, { kind: 'range', column: 'n', min: 1.5, max: 3.25, includeEmpty: false })
})

test('a date bar snaps to midnight of its first and last day', () => {
  const from = Date.UTC(2025, 0, 3, 7, 30)
  const to = Date.UTC(2025, 0, 9, 18, 0)
  const f = binFilter('d', 'date', { from, to, count: 1 })
  assert.deepEqual(f, {
    kind: 'range',
    column: 'd',
    min: Date.UTC(2025, 0, 3),
    max: Date.UTC(2025, 0, 9),
    includeEmpty: false,
  })
})

test('a date-time bar keeps its bounds', () => {
  const from = Date.UTC(2025, 0, 3, 7, 30)
  const to = Date.UTC(2025, 0, 9, 18, 0)
  const f = binFilter('d', 'dateTime', { from, to, count: 1 })
  assert.equal(f.kind === 'range' && f.min, from)
  assert.equal(f.kind === 'range' && f.max, to)
})
