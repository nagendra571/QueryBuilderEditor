import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  blankPasswordProblem,
  catalogControlsLocked,
  connectionTargetChanged,
  defaultPort,
  emptyForm,
  isConnectionValid,
  isFormDirty,
  needsPasswordReentry,
  parsePort,
  REENTER_PASSWORD_MESSAGE,
  REENTER_UNREADABLE_MESSAGE,
  portAfterProviderChange,
  shouldWarnInsecure,
  SSL_OPTIONS,
  toConnectionInput,
  validateConnection,
} from '../src/features/admin/connection-form.ts'
import type { ConnectionForm } from '../src/features/admin/connection-form.ts'

const good: ConnectionForm = { host: 'db.example.com', port: '5432', database: 'sales', username: 'app', password: 'x', sslMode: 'require' }
const saved = { host: 'db.example.com', port: 5432, database: 'sales', username: 'app', sslMode: 'require' as const, hasPassword: true }

test('default ports follow the provider', () => {
  assert.equal(defaultPort('postgreSql'), 5432)
  assert.equal(defaultPort('sqlServer'), 1433)
  assert.equal(emptyForm('sqlServer').port, '1433')
})

test('changing provider moves the port only while it still holds the old default', () => {
  assert.equal(portAfterProviderChange('5432', 'postgreSql', 'sqlServer'), '1433')
  assert.equal(portAfterProviderChange('', 'postgreSql', 'sqlServer'), '1433')
  assert.equal(portAfterProviderChange('6000', 'postgreSql', 'sqlServer'), '6000')
})

test('a blank port means the provider default; junk is rejected', () => {
  assert.equal(parsePort('', 'postgreSql'), 5432)
  assert.equal(parsePort(' 14330 ', 'sqlServer'), 14330)
  assert.equal(parsePort('12ab', 'sqlServer'), null)
  assert.equal(parsePort('-1', 'sqlServer'), null)
  assert.equal(parsePort('1.5', 'sqlServer'), null)
})

test('a complete form is valid', () => {
  assert.equal(isConnectionValid(good, 'postgreSql'), true)
})

test('port must be 1 to 65535', () => {
  assert.ok(validateConnection({ ...good, port: '0' }, 'postgreSql').port)
  assert.ok(validateConnection({ ...good, port: '65536' }, 'postgreSql').port)
  assert.equal(validateConnection({ ...good, port: '65535' }, 'postgreSql').port, undefined)
  assert.equal(validateConnection({ ...good, port: '1' }, 'postgreSql').port, undefined)
})

test('host, database and username are required and bounded', () => {
  assert.ok(validateConnection({ ...good, host: '  ' }, 'postgreSql').host)
  assert.ok(validateConnection({ ...good, host: 'h'.repeat(256) }, 'postgreSql').host)
  assert.ok(validateConnection({ ...good, database: '' }, 'postgreSql').database)
  assert.ok(validateConnection({ ...good, database: 'd'.repeat(129) }, 'postgreSql').database)
  assert.ok(validateConnection({ ...good, username: '' }, 'postgreSql').username)
  assert.ok(validateConnection({ ...good, username: 'u'.repeat(129) }, 'postgreSql').username)
  assert.ok(validateConnection({ ...good, password: 'p'.repeat(1025) }, 'postgreSql').password)
})

test('hosts with whitespace or connection-string characters are rejected', () => {
  for (const host of ['a b', 'a\tb', 'a,b', 'x;Password=y', 'a"b', "a'b"]) {
    assert.ok(validateConnection({ ...good, host }, 'postgreSql').host, host)
  }
})

test('a blank password needs re-entry only when host, port or username changed', () => {
  const blank = { ...good, password: '' }
  assert.equal(needsPasswordReentry(saved, blank, 'postgreSql'), false)
  assert.equal(needsPasswordReentry(saved, { ...blank, host: 'DB.example.com' }, 'postgreSql'), false)
  assert.equal(needsPasswordReentry(saved, { ...blank, host: 'other.example.com' }, 'postgreSql'), true)
  assert.equal(needsPasswordReentry(saved, { ...blank, port: '5433' }, 'postgreSql'), true)
  assert.equal(needsPasswordReentry(saved, { ...blank, username: 'someone' }, 'postgreSql'), true)
  assert.equal(needsPasswordReentry(saved, { ...blank, host: 'other.example.com', password: 'typed' }, 'postgreSql'), false)
  assert.equal(needsPasswordReentry(null, blank, 'postgreSql'), false)
})

test('the payload trims host and database but keeps username and password exactly', () => {
  const input = toConnectionInput({ ...good, host: ' h ', database: ' d ', username: ' u ', password: ' p ' }, 'postgreSql')
  assert.deepEqual(input, { host: 'h', port: 5432, database: 'd', username: ' u ', password: ' p ', sslMode: 'require' })
})

test('encryption choices use plain labels', () => {
  assert.deepEqual(SSL_OPTIONS.map((o) => o.label), ['Off', 'Required', 'Required and verify certificate'])
})

test('only a page known to be insecure gets the password warning', () => {
  assert.equal(shouldWarnInsecure(false), true)
  assert.equal(shouldWarnInsecure(true), false)
  assert.equal(shouldWarnInsecure(undefined), false)
})

test('lowering encryption counts as a changed connection, raising it does not', () => {
  const blank = { ...good, password: '' }
  assert.equal(needsPasswordReentry(saved, { ...blank, sslMode: 'disable' }, 'postgreSql'), true)
  assert.equal(needsPasswordReentry({ ...saved, sslMode: 'verifyFull' }, { ...blank, sslMode: 'require' }, 'postgreSql'), true)
  assert.equal(needsPasswordReentry({ ...saved, sslMode: 'disable' }, { ...blank, sslMode: 'require' }, 'postgreSql'), false)
  assert.equal(needsPasswordReentry(saved, { ...blank, sslMode: 'verifyFull' }, 'postgreSql'), false)
})

test('a blank password on an unreadable source needs re-entry, whatever else is unchanged', () => {
  const blank = { ...good, password: '' }
  assert.equal(blankPasswordProblem(null, true, blank, 'postgreSql'), REENTER_UNREADABLE_MESSAGE)
  assert.equal(blankPasswordProblem(null, true, { ...blank, password: 'typed' }, 'postgreSql'), null)
  assert.equal(blankPasswordProblem(saved, false, blank, 'postgreSql'), null)
  assert.equal(blankPasswordProblem(saved, false, { ...blank, sslMode: 'disable' }, 'postgreSql'), REENTER_PASSWORD_MESSAGE)
})

test('hosts are limited to names, addresses and, for SQL Server, host\\instance', () => {
  for (const host of ['db.example.com', '10.0.0.5', '[::1]', 'my_host-1']) {
    assert.equal(validateConnection({ ...good, host }, 'postgreSql').host, undefined, host)
  }
  for (const host of ['np:\\\\srv\\pipe', 'tcp:srv', '(localdb)\\MSSQLLocalDB', '/var/run/postgresql', 'host:5432', '\\\\srv\\share']) {
    assert.ok(validateConnection({ ...good, host }, 'sqlServer').host, host)
  }
  assert.equal(validateConnection({ ...good, host: 'sql01\\SQLEXPRESS', port: '1433' }, 'sqlServer').host, undefined)
  assert.equal(validateConnection({ ...good, host: 'sql01\\SQLEXPRESS', port: '1500' }, 'sqlServer').port, undefined)
  assert.ok(validateConnection({ ...good, host: 'sql01\\SQLEXPRESS' }, 'postgreSql').host)
})

test('control characters and SQL Server length limits are refused', () => {
  assert.ok(validateConnection({ ...good, database: 'a\u0000b' }, 'postgreSql').database)
  assert.ok(validateConnection({ ...good, username: 'a\nb' }, 'postgreSql').username)
  assert.ok(validateConnection({ ...good, password: 'a\u0007b' }, 'postgreSql').password)
  assert.ok(validateConnection({ ...good, password: 'p'.repeat(129) }, 'sqlServer').password)
  assert.equal(validateConnection({ ...good, password: 'p'.repeat(129) }, 'postgreSql').password, undefined)
})

test('changing host or database is flagged, changing port or username is not', () => {
  assert.equal(connectionTargetChanged(saved, good), false)
  assert.equal(connectionTargetChanged(saved, { ...good, host: 'other.example.com' }), true)
  assert.equal(connectionTargetChanged(saved, { ...good, database: 'other' }), true)
  assert.equal(connectionTargetChanged(saved, { ...good, username: 'x', port: '1' }), false)
  assert.equal(connectionTargetChanged(null, good), false)
})

test('catalog controls lock while the data source cannot be read', () => {
  assert.equal(catalogControlsLocked("Couldn't connect to this data source."), true)
  assert.equal(catalogControlsLocked(null), false)
  assert.equal(catalogControlsLocked(undefined), false)
  assert.equal(catalogControlsLocked(''), false)
})

test('the final SQL Server data source value is limited to 128 characters', () => {
  // "tcp:" + host + "," + port: with port 1433 a 119 character host fits and 120 does not.
  assert.equal(validateConnection({ ...good, host: 'h'.repeat(119), port: '1433' }, 'sqlServer').host, undefined)
  assert.ok(validateConnection({ ...good, host: 'h'.repeat(120), port: '1433' }, 'sqlServer').host)
  assert.equal(validateConnection({ ...good, host: 'h'.repeat(200) }, 'postgreSql').host, undefined)
})

test('a form counts as dirty once anything is typed or changed from its defaults', () => {
  assert.equal(isFormDirty(emptyForm('sqlServer'), 'sqlServer'), false)
  assert.equal(isFormDirty({ ...emptyForm('sqlServer'), host: 'h' }, 'sqlServer'), true)
  assert.equal(isFormDirty({ ...emptyForm('sqlServer'), password: 'p' }, 'sqlServer'), true)
  assert.equal(isFormDirty({ ...emptyForm('sqlServer'), sslMode: 'disable' }, 'sqlServer'), true)
  assert.equal(isFormDirty({ ...emptyForm('sqlServer'), port: '1500' }, 'sqlServer'), true)
})
