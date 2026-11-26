import { test } from 'node:test'
import assert from 'node:assert/strict'
import {
  compileFormat,
  defaultDateFormat,
  defaultNumberFormat,
  formatDate,
  formatNumber,
  isComplete,
  isFormatted,
  ruleLabel,
  rulesFor,
  type ColumnFormat,
  type HighlightRule,
  type NumberFormat,
} from '../src/features/builder/results/grid-format.ts'
import { formatValue } from '../src/features/visualizations/format.ts'

const GB = 'en-GB'
const nf = (o: Partial<NumberFormat>): NumberFormat => ({ ...defaultNumberFormat, ...o })

test('formatNumber styles', () => {
  assert.equal(formatNumber(1234.5, defaultNumberFormat, GB), (1234.5).toLocaleString(GB))
  assert.equal(formatNumber(1234.5, nf({ style: 'number', decimals: 2 }), GB), '1,234.50')
  assert.equal(formatNumber(1234.5, nf({ style: 'number', decimals: 2, thousands: false }), GB), '1234.50')
  assert.equal(formatNumber(12400, nf({ style: 'compact' }), GB), new Intl.NumberFormat(GB, { notation: 'compact', maximumFractionDigits: 1 }).format(12400))
  assert.equal(
    formatNumber(12400, nf({ style: 'currency', decimals: 2 }), GB),
    new Intl.NumberFormat(GB, { style: 'currency', currency: 'USD', minimumFractionDigits: 2, maximumFractionDigits: 2 }).format(12400),
  )
  assert.equal(formatNumber(1234.5, nf({ style: 'currency', currency: 'US', decimals: 2 }), GB), formatNumber(1234.5, nf({ style: 'number', decimals: 2 }), GB))
  assert.equal(formatNumber(42, nf({ style: 'percent' }), GB), '42%')
  assert.equal(formatNumber(0.42, nf({ style: 'percent', fraction: true }), GB), '42%')
  assert.match(formatNumber(-1234.5, nf({ style: 'number', decimals: 1 }), GB), /^[-−]1,234\.5$/)
})

test('formatNumber matches the chart formatter', () => {
  const cases: [NumberFormat, number][] = [
    [nf({ style: 'number', decimals: 2 }), 1234.567],
    [nf({ style: 'number' }), 1234.567],
    [nf({ style: 'number' }), 1234],
    [nf({ style: 'compact' }), 12400],
    [nf({ style: 'compact', decimals: 2 }), 12345],
    [nf({ style: 'currency', decimals: 2 }), 12400],
    [nf({ style: 'currency' }), 12400.5],
    [nf({ style: 'percent' }), 42.567],
    [nf({ style: 'percent', decimals: 1 }), 42.567],
  ]
  for (const [f, n] of cases) {
    assert.equal(
      formatNumber(n, f),
      formatValue(n, { style: f.style as 'number', decimals: f.decimals, currency: f.currency }),
      `${f.style} ${f.decimals} ${n}`,
    )
  }
})

test('formatDate zoneless values keep their calendar day', () => {
  const d = (pattern: 'medium', locale = GB) => formatDate('2026-11-03T00:00:00', 'dateTime', { ...defaultDateFormat, pattern }, locale)
  assert.equal(d('medium'), '3 Nov 2026')
  assert.equal(d('medium', 'en-US'), 'Nov 3, 2026')
  const f = (pattern: 'weekday' | 'short' | 'iso' | 'monthYear' | 'year') =>
    formatDate('2026-11-03T00:00:00', 'dateTime', { ...defaultDateFormat, pattern }, GB)
  assert.equal(f('weekday'), new Intl.DateTimeFormat(GB, { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', timeZone: 'UTC' }).format(Date.UTC(2026, 10, 3)))
  assert.match(f('weekday')!, /^Tue/)
  assert.equal(f('short'), '03/11/2026')
  assert.equal(f('iso'), '2026-11-03')
  assert.equal(f('monthYear'), 'Nov 2026')
  assert.equal(f('year'), '2026')
  assert.equal(formatDate('2026-11-03', 'date', { pattern: 'iso', showTime: false }, GB), '2026-11-03')
})

test('formatDate time, zones, auto and unreadable', () => {
  const med = { pattern: 'medium' as const, showTime: true }
  assert.match(formatDate('2026-11-03T14:05:00', 'dateTime', med, GB)!, /14:05/)
  assert.doesNotMatch(formatDate('2026-11-03T14:05:00', 'date', med, GB)!, /14:05/)
  const zoned = '2026-11-03T23:30:00Z'
  assert.equal(
    formatDate(zoned, 'dateTime', { pattern: 'medium', showTime: false }, GB),
    new Intl.DateTimeFormat(GB, { day: 'numeric', month: 'short', year: 'numeric' }).format(Date.parse(zoned)),
  )
  assert.equal(formatDate('2026-11-03T14:05:00', 'dateTime', defaultDateFormat, GB), '2026-11-03T14:05:00')
  assert.equal(formatDate('not a date', 'date', { pattern: 'medium', showTime: false }, GB), null)
})

test('rulesFor', () => {
  assert.deepEqual(rulesFor('number'), ['gt', 'lt', 'between', 'eq'])
  for (const t of ['text', 'guid', 'unknown'] as const) assert.deepEqual(rulesFor(t), ['is', 'contains', 'empty'])
  for (const t of ['date', 'dateTime'] as const) assert.deepEqual(rulesFor(t), ['before', 'after', 'between'])
  assert.deepEqual(rulesFor('boolean'), [])
})

const rule = (op: HighlightRule['op'], value: string, value2 = '', color: HighlightRule['color'] = 'red'): HighlightRule => ({
  id: op + value,
  op,
  value,
  value2,
  color,
})
const hl = (dataType: 'number' | 'text' | 'date' | 'dateTime', rules: HighlightRule[], value: unknown) =>
  compileFormat([{ c: value }], { name: 'c', dataType }, { rules }, GB).look(value).highlight

test('rules match', () => {
  assert.equal(hl('number', [rule('gt', '15000')], 15001), 'red')
  assert.equal(hl('number', [rule('gt', '15000')], 15000), null)
  assert.equal(hl('number', [rule('lt', '5')], 4), 'red')
  assert.equal(hl('number', [rule('eq', '5')], 5), 'red')
  for (const v of [10, 15, 20]) assert.equal(hl('number', [rule('between', '10', '20')], v), 'red')
  assert.equal(hl('number', [rule('between', '10', '20')], 21), null)
  assert.equal(hl('number', [rule('between', '20', '10')], 15), 'red')
  assert.equal(hl('text', [rule('is', 'Open')], 'Open'), 'red')
  assert.equal(hl('text', [rule('is', 'Open')], 'open'), null)
  assert.equal(hl('text', [rule('contains', 'PEN')], 'Open'), 'red')
  for (const v of [null, undefined, '', '  ']) assert.equal(hl('text', [rule('empty', '')], v), 'red')
  assert.equal(hl('text', [rule('empty', '')], 'x'), null)
})

test('date rules compare the displayed calendar day', () => {
  const v = '2026-11-03T23:30:00'
  assert.equal(hl('dateTime', [rule('before', '2026-11-04')], v), 'red')
  assert.equal(hl('dateTime', [rule('after', '2026-11-03')], v), null)
  assert.equal(hl('dateTime', [rule('after', '2026-11-02')], v), 'red')
  assert.equal(hl('dateTime', [rule('between', '2026-11-03', '2026-11-03')], v), 'red')
  assert.equal(hl('dateTime', [rule('between', '2026-11-05', '2026-11-01')], v), 'red')
  assert.equal(hl('date', [rule('before', 'garbage')], '2026-11-03'), null)
})

test('incomplete rules never match and the first match wins', () => {
  assert.equal(isComplete(rule('gt', '')), false)
  assert.equal(isComplete(rule('between', '1', '')), false)
  assert.equal(isComplete(rule('empty', '')), true)
  assert.equal(hl('number', [rule('gt', '')], 5), null)
  assert.equal(hl('number', [rule('gt', '1', '', 'blue'), rule('gt', '2', '', 'green')], 5), 'blue')
})

test('ruleLabel', () => {
  const fmt = (v: string) => (v === '15000' ? '15,000' : v)
  assert.equal(ruleLabel('Amount', rule('gt', '15000'), fmt), 'Amount > 15,000')
  assert.equal(ruleLabel('Amount', rule('lt', '1'), fmt), 'Amount < 1')
  assert.equal(ruleLabel('Amount', rule('between', '1', '2'), fmt), 'Amount between 1 and 2')
  assert.equal(ruleLabel('Amount', rule('empty', ''), fmt), 'Amount is empty')
})

const numLook = (values: number[], f: ColumnFormat) => {
  const rows = values.map((c) => ({ c }))
  const c = compileFormat(rows, { name: 'c', dataType: 'number' }, f, GB)
  return values.map((v) => c.look(v))
}

test('bars', () => {
  const l = numLook([0, 50, 100], { bars: true, rules: [] })
  assert.deepEqual(l[2].bar, { from: 0, to: 1, negative: false })
  assert.equal(l[1].bar!.to, 0.5)
  const n = numLook([-50, 0, 150], { bars: true, rules: [] })
  assert.deepEqual(n[0].bar, { from: 0, to: 0.25, negative: true })
  assert.deepEqual(n[2].bar, { from: 0.25, to: 1, negative: false })
})

test('scales', () => {
  const b = numLook([1, 5, 9], { scale: 'blue', rules: [] })
  assert.equal(b[0].scale!.t, 0)
  assert.equal(b[2].scale!.t, 1)
  const r = numLook([1, 2, 9], { scale: 'redGreen', rules: [] })
  assert.deepEqual(r.map((x) => x.scale!.t), [0, 0.5, 1])
})

test('flat column', () => {
  const l = numLook([7, 7, 7], { bars: true, scale: 'redGreen', rules: [] })
  assert.equal(l[0].bar!.to, 1)
  assert.equal(l[0].scale!.t, 0.5)
  const z = numLook([0, 0], { bars: true, scale: 'blue', rules: [] })
  assert.equal(z[0].bar, null)
  assert.equal(z[0].scale!.t, 0.5)
})

test('text in a number column is left alone and ignored by statistics', () => {
  const big = '123456789012345678901234567890.5'
  const rows = [{ c: 0 }, { c: 10 }, { c: big }]
  const c = compileFormat(rows, { name: 'c', dataType: 'number' }, { bars: true, scale: 'blue', rules: [rule('gt', '0')] }, GB)
  const look = c.look(big)
  assert.equal(look.text, big)
  assert.equal(look.bar, null)
  assert.equal(look.scale, null)
  assert.equal(look.highlight, null)
  assert.equal(c.look(10).bar!.to, 1)
  assert.equal(c.look(10).scale!.t, 1)
})

test('title', () => {
  const f: ColumnFormat = { number: nf({ style: 'compact' }), rules: [rule('gt', '15000')] }
  const rows = [{ c: 20000 }]
  const c = compileFormat(rows, { name: 'c', dataType: 'number' }, f, GB)
  const l = c.look(20000)
  assert.equal(l.text, new Intl.NumberFormat(GB, { notation: 'compact', maximumFractionDigits: 1 }).format(20000))
  assert.equal(l.title, `${(20000).toLocaleString()} · c > ${new Intl.NumberFormat(GB, { notation: 'compact', maximumFractionDigits: 1 }).format(15000)}`)
  const plain = compileFormat([{ c: 'x' }], { name: 'c', dataType: 'text' }, { rules: [] }, GB).look('x')
  assert.equal(plain.title, null)
  const ruleOnly = compileFormat([{ c: 'x' }], { name: 'c', dataType: 'text' }, { rules: [rule('is', 'x')] }, GB).look('x')
  assert.equal(ruleOnly.title, 'c is x')
})

test('compileFormat caches per rows, column and format', () => {
  const rows = [{ c: 1 }]
  const f: ColumnFormat = { bars: true, rules: [] }
  const col = { name: 'c', dataType: 'number' as const }
  const a = compileFormat(rows, col, f, GB)
  assert.equal(compileFormat(rows, col, f, GB), a)
  assert.notEqual(compileFormat(rows, col, { ...f }, GB), a)
  assert.notEqual(compileFormat([{ c: 1 }], col, f, GB), a)
})

test('isFormatted', () => {
  assert.equal(isFormatted(undefined), false)
  assert.equal(isFormatted({ rules: [] }), false)
  assert.equal(isFormatted({ number: defaultNumberFormat, date: defaultDateFormat, rules: [] }), false)
  assert.equal(isFormatted({ number: nf({ style: 'compact' }), rules: [] }), true)
  assert.equal(isFormatted({ date: { pattern: 'iso', showTime: false }, rules: [] }), true)
  assert.equal(isFormatted({ bars: true, rules: [] }), true)
  assert.equal(isFormatted({ scale: 'blue', rules: [] }), true)
  assert.equal(isFormatted({ rules: [rule('gt', '1')] }), true)
})

test('grouping follows the locale default, like the charts', () => {
  assert.equal(formatNumber(1234, nf({ style: 'number' }), 'es'), new Intl.NumberFormat('es', { minimumFractionDigits: 0, maximumFractionDigits: 0 }).format(1234))
  assert.equal(formatNumber(1234, nf({ style: 'number', thousands: false }), 'es'), '1234')
})
