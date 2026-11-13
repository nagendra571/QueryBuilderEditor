import assert from 'node:assert/strict'
import { test } from 'node:test'
import { downloadFileName } from '../src/features/visualizations/download-name.ts'

test('characters file systems refuse become spaces, collapsed', () => {
  assert.equal(downloadFileName('Sales / Region: 2026?', 'csv'), 'Sales Region 2026.csv')
  assert.equal(downloadFileName('a\tb', 'xlsx'), 'a b.xlsx')
})

test('brackets and unicode stay', () => {
  assert.equal(downloadFileName('Sales by [region] – €', 'csv'), 'Sales by [region] – €.csv')
})

test('an empty or dots-only name falls back', () => {
  assert.equal(downloadFileName('  ...  ', 'csv'), 'chart.csv')
  assert.equal(downloadFileName(null, 'xlsx', 'tile'), 'tile.xlsx')
})

test('Windows device names get a prefix', () => {
  assert.equal(downloadFileName('CON<>|"*', 'csv'), '_CON.csv')
  assert.equal(downloadFileName('nul.report', 'csv'), '_nul.report.csv')
  assert.equal(downloadFileName('Console', 'csv'), 'Console.csv')
})

test('long names are cut to 100 characters before the extension', () => {
  assert.equal(downloadFileName('x'.repeat(300), 'csv'), `${'x'.repeat(100)}.csv`)
})

test('cutting never splits an emoji', () => {
  assert.equal(downloadFileName('a'.repeat(99) + '\u{1F4CA}', 'csv'), `${'a'.repeat(99)}.csv`)
})
