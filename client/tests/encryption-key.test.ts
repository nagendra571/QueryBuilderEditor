import assert from 'node:assert/strict'
import { test } from 'node:test'
import { generateEncryptionKey, KEY_BYTES, reencryptSummary } from '../src/features/admin/encryption-key.ts'

test('a generated key is base64 of exactly 32 bytes', () => {
  const key = generateEncryptionKey()
  assert.match(key, /^[A-Za-z0-9+/]+={0,2}$/)
  assert.equal(Buffer.from(key, 'base64').length, KEY_BYTES)
  assert.equal(Buffer.from(key, 'base64').toString('base64'), key)
})

test('two generated keys differ', () => {
  assert.notEqual(generateEncryptionKey(), generateEncryptionKey())
})

test('the summary lists only what happened and says to run again after a skip', () => {
  const lines = reencryptSummary({ reencrypted: 2, alreadyCurrent: 0, unreadable: 0, skippedBecauseChanged: 1 })
  assert.equal(lines.length, 2)
  assert.match(lines[0], /^2 data sources re-encrypted/)
  assert.match(lines[1], /^1 data source skipped because it changed while re-encrypting\. Run it again\.$/)
})

test('the summary of an empty run is a sentence, not nothing', () => {
  assert.deepEqual(reencryptSummary({ reencrypted: 0, alreadyCurrent: 0, unreadable: 0, skippedBecauseChanged: 0 }), [
    'There are no managed data sources to re-encrypt.',
  ])
})

test('the summary mentions unreadable sources and where to fix them', () => {
  const [line] = reencryptSummary({ reencrypted: 0, alreadyCurrent: 0, unreadable: 1, skippedBecauseChanged: 0 })
  assert.match(line, /1 data source couldn't be read/)
  assert.match(line, /re-enter its password/)
})
