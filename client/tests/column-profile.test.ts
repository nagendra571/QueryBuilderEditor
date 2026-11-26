import assert from 'node:assert/strict'
import { test } from 'node:test'
import { histogram, profileColumn, profileKind } from '../src/features/builder/results/column-profile.ts'
import type { QueryResultColumnDto } from '../src/types/index.ts'

const col = (dataType: QueryResultColumnDto['dataType'], aggregate?: QueryResultColumnDto['aggregate']): QueryResultColumnDto => ({
  name: 'c',
  dataType,
  aggregate,
})
const rowsOf = (values: unknown[]) => values.map((c) => ({ c }))

test('profileKind maps data types', () => {
  assert.equal(profileKind('number'), 'number')
  assert.equal(profileKind('dateTime'), 'date')
  assert.equal(profileKind('date'), 'date')
  assert.equal(profileKind('boolean'), 'boolean')
  assert.equal(profileKind('guid'), 'text')
  assert.equal(profileKind('unknown'), 'text')
  assert.equal(profileKind('text'), 'text')
})

test('histogram edge cases', () => {
  assert.deepEqual(histogram([], 20), [])
  assert.deepEqual(histogram([5, 5, 5]), [{ from: 5, to: 5, count: 3 }])
  assert.deepEqual(histogram([0, 10], 2), [
    { from: 0, to: 5, count: 1 },
    { from: 5, to: 10, count: 1 },
  ])
})

test('number profile', () => {
  const p = profileColumn(rowsOf([1, 2, null, '3.5', '$4', 10]), col('number'))
  assert.equal(p.kind, 'number')
  if (p.kind !== 'number') return
  assert.equal(p.rows, 6)
  assert.equal(p.empty, 1)
  assert.equal(p.unreadable, 1)
  assert.equal(p.distinct, 4)
  assert.equal(p.min, 1)
  assert.equal(p.max, 10)
  assert.equal(p.sum, 16.5)
  assert.equal(p.mean, 4.125)
  assert.equal(p.median, 2.75)
  assert.equal(p.showSum, true)
  assert.equal(p.bins.length, 20)
})

test('sum hidden for averages', () => {
  const p = profileColumn(rowsOf([1, 2]), col('number', 'avg'))
  if (p.kind !== 'number') throw new Error('kind')
  assert.equal(p.showSum, false)
  assert.equal(p.sum, null)
})

test('all-null number column', () => {
  const p = profileColumn(rowsOf([null, undefined]), col('number'))
  if (p.kind !== 'number') throw new Error('kind')
  assert.equal(p.min, null)
  assert.equal(p.max, null)
  assert.equal(p.mean, null)
  assert.equal(p.median, null)
  assert.equal(p.sum, null)
  assert.deepEqual(p.bins, [])
})

test('date profile', () => {
  const p = profileColumn(rowsOf(['2025-01-01', '2025-03-01', 'not a date', null]), col('date'))
  if (p.kind !== 'date') throw new Error('kind')
  assert.equal(p.earliest, Date.parse('2025-01-01'))
  assert.equal(p.latest, Date.parse('2025-03-01'))
  assert.equal(p.unreadable, 1)
  assert.equal(p.empty, 1)
  assert.equal(p.distinct, 2)
})

test('text profile', () => {
  const p = profileColumn(rowsOf(['b', 'a', 'a', '', null, 'ccc', 'b']), col('text'))
  if (p.kind !== 'text') throw new Error('kind')
  assert.equal(p.empty, 2)
  assert.equal(p.distinct, 3)
  assert.deepEqual(p.top, [
    { value: 'a', count: 2 },
    { value: 'b', count: 2 },
    { value: 'ccc', count: 1 },
  ])
  assert.equal(p.other, 0)
  assert.equal(p.shortest, 1)
  assert.equal(p.longest, 3)
})

test('text profile cuts top values to 10', () => {
  const values = Array.from({ length: 12 }, (_, i) => `v${String(i + 1).padStart(2, '0')}`)
  const p = profileColumn(rowsOf(values), col('text'))
  if (p.kind !== 'text') throw new Error('kind')
  assert.equal(p.top.length, 10)
  assert.equal(p.other, 2)
})

test('boolean profile', () => {
  const p = profileColumn(rowsOf([true, false, true, null, 'x']), col('boolean'))
  if (p.kind !== 'boolean') throw new Error('kind')
  assert.equal(p.yes, 2)
  assert.equal(p.no, 1)
  assert.equal(p.empty, 1)
  assert.equal(p.unreadable, 1)
  assert.equal(p.distinct, 2)
})
