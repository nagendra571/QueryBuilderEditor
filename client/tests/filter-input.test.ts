import test from 'node:test'
import assert from 'node:assert/strict'
import { boundInputText, buildRangeFilter, filterFromTicks, initialTicks, parseBound, rangeProblem } from '../src/features/builder/results/filter-input.ts'
import type { ValueOption } from '../src/features/builder/results/grid-filters.ts'

const opts = (...v: (string | null)[]): ValueOption[] => v.map((value) => ({ value, label: String(value), count: 1 }))

test('numbers parse, blanks are open ends, junk is invalid', () => {
  assert.deepEqual(parseBound('', 'number', false), { ok: true, value: null })
  assert.deepEqual(parseBound(' 2.5 ', 'number', false), { ok: true, value: 2.5 })
  assert.equal(parseBound('abc', 'number', false).ok, false)
})

test('dateTime To includes the whole day; date To is midnight', () => {
  assert.deepEqual(parseBound('2026-03-05', 'dateTime', true), { ok: true, value: Date.UTC(2026, 2, 5) + 86_399_999 })
  assert.deepEqual(parseBound('2026-03-05', 'dateTime', false), { ok: true, value: Date.UTC(2026, 2, 5) })
  assert.deepEqual(parseBound('2026-03-05', 'date', true), { ok: true, value: Date.UTC(2026, 2, 5) })
})

test('range problems', () => {
  assert.equal(rangeProblem('5', '1', 'number'), 'order')
  assert.equal(rangeProblem('x', '', 'number'), 'invalid')
  assert.equal(rangeProblem('1', '', 'number'), null)
  assert.equal(buildRangeFilter('c', 'x', '', false, 'number'), null)
  assert.deepEqual(buildRangeFilter('c', '1', '', true, 'number'), { kind: 'range', column: 'c', min: 1, max: null, includeEmpty: true })
})

test('bound text round-trips', () => {
  assert.equal(boundInputText(Date.UTC(2026, 2, 5), 'date'), '2026-03-05')
  assert.equal(boundInputText(7, 'number'), '7')
  assert.equal(boundInputText(null, 'number'), '')
})

test('ticks start from the existing filter', () => {
  const o = opts('a', 'b', 'c', 'd')
  assert.equal(initialTicks(o, undefined).size, 4)
  assert.deepEqual([...initialTicks(o, { kind: 'values', column: 'x', mode: 'include', values: ['a'] })], ['a'])
  assert.deepEqual([...initialTicks(o, { kind: 'values', column: 'x', mode: 'exclude', values: ['a'] })], ['b', 'c', 'd'])
})

test('apply picks include below half, exclude otherwise, none when all ticked', () => {
  const o = opts('a', 'b', 'c', 'd')
  assert.equal(filterFromTicks('x', o, new Set(['a', 'b', 'c', 'd'])), null)
  assert.deepEqual(filterFromTicks('x', o, new Set(['a'])), { kind: 'values', column: 'x', mode: 'include', values: ['a'] })
  assert.deepEqual(filterFromTicks('x', o, new Set(['a', 'b'])), { kind: 'values', column: 'x', mode: 'exclude', values: ['c', 'd'] })
})
