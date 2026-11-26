import { test } from 'node:test'
import assert from 'node:assert/strict'
import { clampPanelWidth, initialPanel, panelMode } from '../src/features/builder/results/panel-layout.ts'

test('clampPanelWidth keeps the width between the minimum, maximum and half the area', () => {
  assert.equal(clampPanelWidth(340, 1200), 340)
  assert.equal(clampPanelWidth(900, 1200), 560)
  assert.equal(clampPanelWidth(600, 800), 400)
  assert.equal(clampPanelWidth(100, 1200), 280)
  assert.equal(clampPanelWidth(340, 400), 280)
})

test('panelMode overlays below 900px', () => {
  assert.equal(panelMode(899), 'overlay')
  assert.equal(panelMode(900), 'dock')
})

test('the panel starts closed on the Row tab', () => {
  assert.deepEqual(initialPanel, { open: false, tab: 'row', width: 340 })
})
