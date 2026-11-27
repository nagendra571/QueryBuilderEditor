import assert from 'node:assert/strict'
import { test } from 'node:test'
import { chartForColumn, type OutputColumn } from '../src/features/visualizations/chart-data.ts'

const region: OutputColumn = { name: 'Region', dataType: 'text', isAggregate: false, aggregate: 'none' }
const day: OutputColumn = { name: 'Day', dataType: 'date', isAggregate: false, aggregate: 'none' }
const sales: OutputColumn = { name: 'Sales', dataType: 'number', isAggregate: true, aggregate: 'sum' }
const qty: OutputColumn = { name: 'Qty', dataType: 'number', isAggregate: false, aggregate: 'none' }
const all = [region, day, sales, qty]

test('a number column becomes the measure of a bar chart', () => {
  assert.deepEqual(chartForColumn('Qty', all), { type: 'bar', x: 'Region', measures: ['Qty'] })
})

test('a date column goes on X of a line chart with the default measure', () => {
  assert.deepEqual(chartForColumn('Day', all), { type: 'line', x: 'Day', measures: ['Sales'] })
})

test('a text column goes on X of a bar chart with the default measure', () => {
  assert.deepEqual(chartForColumn('Region', all), { type: 'bar', x: 'Region', measures: ['Sales'] })
})

test('a number column with no other column leaves X empty', () => {
  assert.deepEqual(chartForColumn('Sales', [sales]), { type: 'bar', x: '', measures: ['Sales'] })
})

test('a number column that would be the default X picks the first other non-number column', () => {
  assert.deepEqual(chartForColumn('Qty', [qty, day]), { type: 'bar', x: 'Day', measures: ['Qty'] })
})

test('a date column with no number column has no measure', () => {
  assert.deepEqual(chartForColumn('Day', [day]), { type: 'line', x: 'Day', measures: [] })
})

test('the measure is not repeated when the column is the default measure', () => {
  assert.deepEqual(chartForColumn('Sales', [region, sales]), { type: 'bar', x: 'Region', measures: ['Sales'] })
})

test('an unknown column gives an empty bar chart', () => {
  assert.deepEqual(chartForColumn('Nope', all), { type: 'bar', x: '', measures: [] })
})
