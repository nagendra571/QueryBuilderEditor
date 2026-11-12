import assert from 'node:assert/strict'
import { test } from 'node:test'
import {
  MAX_RETURN_URLS,
  moveCountsSummary,
  parseCeiling,
  validateAppKey,
  validateAppName,
  validateKeyInput,
  validateKid,
  validateReturnUrl,
  validateReturnUrls,
  validateScopeKeys,
} from '../src/features/hub/app-form.ts'
import { actionLabel, auditQueryParams, HUB_AUDIT_ACTIONS } from '../src/features/hub/audit-filter.ts'

// The accept and refuse values below are the ones HubRegistryTests uses against the server.

test('app key: 2 to 63 lowercase letters, digits and hyphens, starting with a letter or digit', () => {
  for (const bad of ['A', '-ab', 'Ab-cd', 'ab_c', 'ab c', 'a'.repeat(64), '']) assert.notEqual(validateAppKey(bad), null, bad)
  for (const ok of ['a'.repeat(63), 'new-app', 'a1', '1a']) assert.equal(validateAppKey(ok), null, ok)
})

test('app name: required, trimmed, at most 200', () => {
  assert.notEqual(validateAppName(''), null)
  assert.notEqual(validateAppName(' '), null)
  assert.notEqual(validateAppName('n'.repeat(201)), null)
  assert.equal(validateAppName('n'.repeat(200)), null)
  assert.equal(validateAppName('  Billing  '), null)
})

test('key id: letters, digits, dot, underscore, hyphen; starts with a letter or digit; at most 64', () => {
  for (const bad of ['.', '..', '.x', '-x', '_x', '', 'k'.repeat(65), 'a/b', 'a b', 'a%b', 'a?b']) assert.notEqual(validateKid(bad), null, bad)
  for (const ok of ['k1', 'a.b_c-d', 'k'.repeat(64), '1']) assert.equal(validateKid(ok), null, ok)
})

test('scope keys: names, no duplicates ignoring case, at most 20', () => {
  assert.equal(validateScopeKeys(['ProgramId', 'Region']), null)
  assert.equal(validateScopeKeys([]), null)
  assert.notEqual(validateScopeKeys(['bad key']), null)
  assert.notEqual(validateScopeKeys(['A', 'a']), null)
  assert.notEqual(validateScopeKeys(Array.from({ length: 21 }, (_, i) => `k${i}`)), null)
  assert.equal(validateScopeKeys(Array.from({ length: 20 }, (_, i) => `k${i}`)), null)
})

test('record ceiling: empty means none, otherwise a whole number from 1 to 100,000', () => {
  assert.deepEqual(parseCeiling(''), { value: null, problem: null })
  assert.deepEqual(parseCeiling('  '), { value: null, problem: null })
  assert.deepEqual(parseCeiling('1'), { value: 1, problem: null })
  assert.deepEqual(parseCeiling('100000'), { value: 100000, problem: null })
  for (const bad of ['0', '-5', '100001', '1.5', 'abc', '1e3']) assert.notEqual(parseCeiling(bad).problem, null, bad)
})

test('return URL accepts the canonical https form and http only for localhost', () => {
  for (const ok of ['https://a.example/home', 'http://localhost:5000/x', 'https://a.example/', 'https://a.example/' + 'x'.repeat(782)]) {
    assert.equal(validateReturnUrl(ok), null, ok)
  }
})

test('return URL refuses what the server refuses', () => {
  const bad = [
    'http://a.example/x',
    'http://localhost.evil.test/x',
    'https://a.example/x#frag',
    'https://user:pw@a.example/x',
    '/relative',
    'ftp://a.example/x',
    'https:evil.com',
    'https:/evil.com/x',
    'https://A.example/x',
    'https://a.example/a b',
    'https://a.example:443/x',
    'javascript:alert(1)',
    'https://a.example/' + 'x'.repeat(800),
    '',
    ' https://a.example/x',
  ]
  for (const url of bad) assert.notEqual(validateReturnUrl(url), null, url)
})

test('return URL without a path says what to write instead', () => {
  assert.match(validateReturnUrl('https://a.example') ?? '', /`https:\/\/a\.example\/`/)
})

test('return URL list: at most 20, exact duplicates collapse, case-only duplicates are refused', () => {
  assert.equal(MAX_RETURN_URLS, 20)
  const many = (n: number) => Array.from({ length: n }, (_, i) => `https://a.example/${i}`)
  assert.equal(validateReturnUrls(many(20)), null)
  assert.notEqual(validateReturnUrls(many(21)), null)
  assert.equal(validateReturnUrls(['https://a.example/home', 'https://a.example/home']), null)
  assert.notEqual(validateReturnUrls(['https://a.example/X', 'https://a.example/x']), null)
  assert.notEqual(validateReturnUrls(['http://a.example/x']), null)
})

test('signing key input: algorithm, kid and a PEM public key', () => {
  const pem = '-----BEGIN PUBLIC KEY-----\nAAAA\n-----END PUBLIC KEY-----'
  assert.equal(validateKeyInput('k1', 'RS256', pem), null)
  assert.equal(validateKeyInput('k2', 'ES256', pem), null)
  assert.notEqual(validateKeyInput('k1', 'HS256', pem), null)
  assert.notEqual(validateKeyInput('', 'RS256', pem), null)
  assert.notEqual(validateKeyInput('k1', 'RS256', ''), null)
  assert.notEqual(validateKeyInput('k1', 'RS256', 'not a key'), null)
  assert.notEqual(validateKeyInput('k1', 'RS256', pem + ' '.repeat(4000)), null)
})

test('move conflict counts read as a list of what still uses the data source', () => {
  assert.deepEqual(moveCountsSummary({ savedQueries: 2, dashboardTiles: 1, parameterDropdowns: 0, dataScopeRules: 3 }), [
    '2 saved queries',
    '1 dashboard tile',
    '3 scope rules',
  ])
  assert.deepEqual(moveCountsSummary({ savedQueries: 0 }), [])
  assert.deepEqual(moveCountsSummary(undefined), [])
})

test('audit action names read as words, and the filter offers the hub actions', () => {
  assert.equal(actionLabel('dataSourceLockChanged'), 'Data source lock changed')
  assert.equal(actionLabel('launchRefused'), 'Launch refused')
  for (const a of ['dataSourceMoved', 'dataSourceLockChanged', 'appRegistryChanged']) assert.ok((HUB_AUDIT_ACTIONS as readonly string[]).includes(a), a)
})

test('the audit request sends the PascalCase action name the server binds', () => {
  assert.equal(auditQueryParams({ action: 'dataSourceMoved' }).action, 'DataSourceMoved')
  assert.equal(auditQueryParams({ action: 'launchRefused', take: 50 }).take, 50)
  assert.equal(auditQueryParams({}).action, undefined)
  for (const a of HUB_AUDIT_ACTIONS) assert.match(auditQueryParams({ action: a }).action!, /^[A-Z][A-Za-z]+$/)
})
