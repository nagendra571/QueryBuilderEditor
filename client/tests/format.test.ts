import assert from 'node:assert/strict'
import { test } from 'node:test'
import { formatAxis, formatValue } from '../src/features/visualizations/format.ts'

const styles = [
  { style: 'number' },
  { style: 'compact' },
  { style: 'currency', currency: 'USD' },
  { style: 'percent' },
] as const

test('null is an em dash for every style and for no format', () => {
  assert.equal(formatValue(null), '—')
  assert.equal(formatValue(null, null), '—')
  for (const f of styles) assert.equal(formatValue(null, f), '—')
})

test('currency with a valid code matches Intl currency output', () => {
  const expected = new Intl.NumberFormat(undefined, {
    style: 'currency',
    currency: 'USD',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(1000.5)
  assert.equal(formatValue(1000.5, { style: 'currency', currency: 'USD', decimals: 2 }), expected)
})

test('currency with a missing or malformed code falls back to a number without throwing', () => {
  const fallback = formatValue(1234.5, { style: 'number' })
  for (const currency of ['US', '', null, undefined, '12$', 'USDX']) {
    assert.doesNotThrow(() => formatValue(1234.5, { style: 'currency', currency }))
    assert.equal(formatValue(1234.5, { style: 'currency', currency }), fallback)
  }
})

test('percent treats the value as already a percentage', () => {
  const out = formatValue(42, { style: 'percent' })
  assert.equal(out, new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 2 }).format(0.42))
  assert.ok(!out.includes('4,200') && !out.includes('4200'))
  assert.ok(out.includes('42'))
})

test('compact matches Intl compact output', () => {
  assert.equal(
    formatValue(1230974, { style: 'compact' }),
    new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(1230974),
  )
})

test('number with decimals pins the fraction digits', () => {
  assert.equal(
    formatValue(3, { style: 'number', decimals: 2 }),
    new Intl.NumberFormat(undefined, { minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(3),
  )
})

test('no format: whole numbers have no decimals, fractions up to 2', () => {
  const plain = (n: number, d: number) =>
    new Intl.NumberFormat(undefined, { minimumFractionDigits: d, maximumFractionDigits: d }).format(n)
  assert.equal(formatValue(1500), plain(1500, 0))
  assert.equal(formatValue(1.5), plain(1.5, 2))
  assert.equal(formatValue(1.5), formatValue(1.5, null))
})

test('axis labels default to compact and follow the format', () => {
  const compact = (n: number) => new Intl.NumberFormat(undefined, { notation: 'compact', maximumFractionDigits: 1 }).format(n)
  assert.equal(formatAxis(1230974), compact(1230974))
  assert.equal(formatAxis(1230974, { style: 'compact' }), compact(1230974))
  assert.equal(
    formatAxis(42, { style: 'percent' }),
    new Intl.NumberFormat(undefined, { style: 'percent', maximumFractionDigits: 2 }).format(0.42),
  )
  assert.doesNotThrow(() => formatAxis(5, { style: 'currency', currency: 'nope' }))
})
