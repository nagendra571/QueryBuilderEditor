import { test } from 'node:test'
import assert from 'node:assert/strict'
import { readdirSync, readFileSync } from 'node:fs'
import { join } from 'node:path'
import { fileURLToPath } from 'node:url'
import { newTileKey } from '../src/features/dashboards/layout.ts'

const SOURCE_ROOT = fileURLToPath(new URL('../src', import.meta.url))

// Browsers leave these undefined on a plain-http page. The package is embedded in host apps it doesn't
// control, and the hosted test site is http, where a call threw and Add chart silently did nothing.
const SECURE_ONLY = ['crypto.randomUUID', 'crypto.subtle', 'navigator.clipboard', 'navigator.serviceWorker']

function sourceFiles(directory: string): string[] {
  return readdirSync(directory, { withFileTypes: true }).flatMap((entry) => {
    const path = join(directory, entry.name)
    if (entry.isDirectory()) return sourceFiles(path)
    return /\.tsx?$/.test(entry.name) ? [path] : []
  })
}

test('client code uses no API that is missing on a plain-http page', () => {
  const offenders = sourceFiles(SOURCE_ROOT).flatMap((file) => {
    const source = readFileSync(file, 'utf8')
    return SECURE_ONLY.filter((api) => source.includes(api)).map((api) => `${file}: ${api}`)
  })
  assert.deepEqual(offenders, [])
})

test('new tile keys are unique', () => {
  const keys = Array.from({ length: 500 }, () => newTileKey())
  assert.equal(new Set(keys).size, keys.length)
})

test('a new tile key can never equal a saved tile id, which is a GUID', () => {
  assert.doesNotMatch(newTileKey(), /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i)
})
