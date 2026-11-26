import test from 'node:test'
import assert from 'node:assert/strict'
import { footerRange } from '../src/features/builder/results/footer-text.ts'

test('footerRange', () => {
  assert.equal(footerRange(1, 40, 1200, 1200), '1–40 of 1,200 visible')
  assert.equal(footerRange(1, 12, 12, 1200), '1–12 of 12 (filtered from 1,200)')
  assert.equal(footerRange(0, 0, 0, 5), '')
  assert.equal(footerRange(0, 0, 5, 5), '')
})

test('selectedCellsText', async () => {
  const { selectedCellsText } = await import('../src/features/builder/results/footer-text.ts')
  assert.equal(selectedCellsText(0), '')
  assert.equal(selectedCellsText(1), '1 cell selected')
  assert.equal(selectedCellsText(12), '12 cells selected')
  assert.equal(selectedCellsText(1200), '1,200 cells selected')
})
