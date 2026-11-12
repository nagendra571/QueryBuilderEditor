import assert from 'node:assert/strict'
import { test } from 'node:test'
import { counterText, fitFontSize, targetText, trendChangeText, trendSummary } from '../src/features/visualizations/single-number.ts'
import { describeChart } from '../src/features/visualizations/chart-data.ts'
import type { TrendModel } from '../src/features/visualizations/story-models.ts'
import type { VisualizationOptions } from '../src/types/index.ts'

test('counterText without a format keeps the counter decimals', () => {
  assert.equal(counterText(1234.5, 0), '1,235')
  assert.equal(counterText(1234.5, 1), '1,234.5')
  assert.equal(counterText(null, 0), '—')
})

test('counterText applies counter decimals only to number and currency when format decimals are unset', () => {
  assert.equal(counterText(1230974, 0, { style: 'compact' }), '1.2M')
  assert.equal(counterText(42.5, 0, { style: 'percent' }), '42.5%')
  assert.equal(counterText(1234.5, 1, { style: 'number' }), '1,234.5')
  assert.equal(counterText(1234.5, 0, { style: 'currency', currency: 'USD' }), '$1,235')
})

test('counterText honours explicit format decimals', () => {
  assert.equal(counterText(1230974, 0, { style: 'compact', decimals: 2 }), '1.23M')
  assert.equal(counterText(42.5, 3, { style: 'percent', decimals: 0 }), '43%')
})

test('fitFontSize shrinks with the number of characters and is capped', () => {
  assert.equal(fitFontSize('1'), 'clamp(1rem, 148.39cqw, 4.5rem)')
  assert.equal(fitFontSize('1,230,974,000.00'), 'clamp(1rem, 9.27cqw, 4.5rem)')
})

test('targetText shows the real percent, even over 100', () => {
  assert.equal(targetText(82), '82% of target')
  assert.equal(targetText(130), '130% of target')
  assert.equal(targetText(null), null)
})

const trend = (patch: Partial<TrendModel>): TrendModel => ({
  kind: 'trend',
  latest: 112,
  latestLabel: '2024-02',
  previous: 100,
  previousLabel: '2024-01',
  change: 12,
  changePct: 12,
  points: [],
  skipped: 0,
  ...patch,
})

test('trend change text uses an arrow, the percent and the compared label', () => {
  assert.equal(trendChangeText(trend({})), '▲ 12% vs 2024-01')
  assert.equal(trendChangeText(trend({ change: -5, changePct: -5 })), '▼ 5% vs 2024-01')
})

test('trend change text for zero change, zero previous and no previous', () => {
  assert.equal(trendChangeText(trend({ change: 0, changePct: 0 })), '→ 0% vs 2024-01')
  assert.equal(trendChangeText(trend({ previous: 0, change: 8, changePct: null })), '▲ 8 vs 2024-01')
  assert.equal(trendChangeText(trend({ previous: 10, change: -8, changePct: null }), { style: 'compact' }), '▼ 8 vs 2024-01')
  assert.equal(trendChangeText(trend({ previous: null, previousLabel: null, change: null, changePct: null })), null)
})

test('trend summary states latest, change and compared label', () => {
  const s = trendSummary(trend({}), 'Sales')
  assert.match(s, /Trend: Sales, latest 112 on 2024-02/)
  assert.match(s, /up 12% compared with 2024-01/)
  assert.match(trendSummary(trend({ latest: null, latestLabel: '', previous: null, previousLabel: null, change: null, changePct: null }), 'Sales'), /no value/)
  assert.match(trendSummary(trend({ change: 0, changePct: 0 }), 'Sales'), /no change compared with 2024-01/)
})

test('counter description matches what is on screen', () => {
  const options: VisualizationOptions = {
    version: 1,
    measures: [],
    stacked: false,
    sortX: 'none',
    counter: { value: 'V', decimals: 0 },
    format: { style: 'compact' },
  }
  const model = { kind: 'counter' as const, value: 1230974, label: 'V', target: null, pctOfTarget: null }
  assert.equal(describeChart('counter', options, model), 'Counter: V, 1.2M')
  const withTarget = { ...model, value: 82, target: 100, pctOfTarget: 82 }
  assert.match(describeChart('counter', { ...options, format: null }, withTarget), /82% of target$/)
})
