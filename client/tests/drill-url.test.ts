import assert from 'node:assert/strict'
import { test } from 'node:test'
import { decodeFilters, decodeTrail, encodeFilters, encodeTrail } from '../src/features/drill/drill-url.ts'

test('trail round-trips unicode, null, empty and fractional date-times byte for byte', () => {
  const steps = [
    { column: 'Région ☃ 日本', value: 'Zoë 😀' },
    { column: 'Closed', value: null },
    { column: 'Blank', value: '' },
    { column: 'At', value: '2026-01-02T03:04:05.1234567' },
  ]
  const text = encodeTrail(steps)
  assert.match(text, /^[A-Za-z0-9_-]*$/)
  assert.deepEqual(decodeTrail(text), steps)
})

test('an empty trail round-trips', () => {
  assert.deepEqual(decodeTrail(encodeTrail([])), [])
})

test('malformed trails decode to null', () => {
  assert.equal(decodeTrail('!!!'), null)
  assert.equal(decodeTrail('e30'), null)
  assert.equal(decodeTrail('a'), null)
  assert.equal(decodeTrail(encodeFilters([{ sourceTileId: 'a', column: 'c', value: 'v' }])), null)
  assert.equal(decodeTrail(encodeTrail([{ column: 1 as unknown as string, value: 'x' }])), null)
  assert.equal(decodeTrail(encodeTrail([{ column: 'a', value: 5 as unknown as string }])), null)
})

test('a trail of more than 8 steps is rejected', () => {
  const make = (n: number) => Array.from({ length: n }, (_, i) => ({ column: `c${i}`, value: `${i}` }))
  assert.equal(decodeTrail(encodeTrail(make(8)))?.length, 8)
  assert.equal(decodeTrail(encodeTrail(make(9))), null)
})

test('filters round-trip and are limited to 5', () => {
  const make = (n: number) =>
    Array.from({ length: n }, (_, i) => ({ sourceTileId: `t${i}`, column: 'Zoë', value: i === 0 ? null : '日本' }))
  assert.deepEqual(decodeFilters(encodeFilters(make(5))), make(5))
  assert.equal(decodeFilters(encodeFilters(make(6))), null)
  assert.equal(decodeFilters('%%'), null)
  assert.equal(decodeFilters(encodeTrail([{ column: 'a', value: 'b' }])), null)
})
