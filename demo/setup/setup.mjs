// Sets up the "Northwind Programs" app in a QueryBuilder Hub: registration, data source policy, and the starting set
// of queries, charts and three published dashboards (Program overview, Program performance, Funnel and outcomes).
// Run demo/sql/programs.sql and demo/sql/programs-showcase.sql on the programs database first. Idempotent. Node 20+, no dependencies. See README.md for the inputs.
//
//   node demo/setup/setup.mjs              everything
//   node demo/setup/setup.mjs --app-only   register the app and its key, then stop (prints the app id)
//   node demo/setup/setup.mjs --verify     after setup, launch as Mary, Raj and Lee and check what each one sees
//
// Nothing secret is printed: the sign-in code, keys and passwords are read, used and never logged.

import { createPrivateKey, createPublicKey, createSign, randomUUID } from 'node:crypto'
import { readFileSync } from 'node:fs'

const args = new Set(process.argv.slice(2))
const env = (name, required = true) => {
  const v = process.env[name]
  if (required && !v) fail(`Set the environment variable ${name} (see demo/setup/README.md).`)
  return v ?? ''
}
function fail(message) {
  console.error('ERROR: ' + message)
  process.exit(1)
}

const HUB = env('QB_HUB').replace(/\/+$/, '')
const APP_KEY = 'northwind'
const APP_NAME = 'Northwind Programs'
const DS_NAME = 'Northwind Programs'
const KID = env('QB_KEY_ID')
const RETURN_URL = env('QB_RETURN_URL', false)
const DS_MODE = env('QB_DS_MODE', false) || 'existing'
const publicPem = () => readFileSync(env('QB_APP_PUBLIC_PEM_FILE'), 'utf8')
const privateKey = () => createPrivateKey(readFileSync(env('QB_APP_PRIVATE_PEM_FILE'), 'utf8'))
const BASE = '/querybuilder'
const ALLOWED = [
  'sales.vw_ProgramEnrollments', 'sales.vw_ProgramSummary', 'sales.vw_ProgramBudgets', 'sales.vw_Programs',
  'sales.vw_ProgramMonthly', 'sales.vw_ProgramFunnel', 'sales.vw_ProgramSessions', // demo/sql/programs-showcase.sql
]

// ---------- HTTP with a hand-made cookie jar ----------

class Session {
  constructor(label) {
    this.label = label
    this.jar = new Map()
  }
  absorb(res) {
    for (const line of res.headers.getSetCookie()) {
      const [pair, ...attrs] = line.split(';')
      const eq = pair.indexOf('=')
      const name = pair.slice(0, eq).trim()
      const value = pair.slice(eq + 1).trim()
      const expired = attrs.some((a) => /^\s*max-age=0\s*$/i.test(a)) || attrs.some((a) => /^\s*expires=/i.test(a) && Date.parse(a.split('=').slice(1).join('=')) < Date.now())
      if (expired || value === '') this.jar.delete(name)
      else this.jar.set(name, value)
    }
  }
  cookieHeader() {
    return [...this.jar].map(([k, v]) => `${k}=${v}`).join('; ')
  }
  async raw(method, path, { headers = {}, body } = {}) {
    const res = await fetch(HUB + path, {
      method,
      redirect: 'manual',
      headers: { cookie: this.cookieHeader(), ...headers },
      body,
    })
    this.absorb(res)
    const text = await res.text()
    let json
    try {
      json = text ? JSON.parse(text) : undefined
    } catch {
      json = undefined
    }
    return { status: res.status, text, json, headers: res.headers }
  }
  // State-changing calls need the CSRF header; it is harmless on reads.
  async api(method, path, body, okStatuses = [200, 201, 204]) {
    const headers = { 'x-querybuilder-request': '1' }
    let payload
    if (body !== undefined) {
      headers['content-type'] = 'application/json'
      payload = JSON.stringify(body)
    }
    const r = await this.raw(method, BASE + '/api' + path, { headers, body: payload })
    if (!okStatuses.includes(r.status)) {
      const detail = r.json?.detail || r.json?.title || r.text.slice(0, 300)
      throw new Error(`${this.label}: ${method} ${path} -> ${r.status} ${detail}`)
    }
    return r
  }
}

function b64url(buf) {
  return Buffer.from(buf).toString('base64url')
}

function launchToken({ sub, name, email, role, programId, lifetime = 60 }) {
  const iat = Math.floor(Date.now() / 1000)
  const header = { alg: 'RS256', typ: 'JWT', kid: KID }
  const payload = { iss: APP_KEY, aud: HUB, sub, name, email, role }
  if (programId == null) payload.unrestricted = true
  else payload.scope = { ProgramId: [String(programId)] }
  payload.iat = iat
  payload.exp = iat + lifetime
  payload.jti = randomUUID()
  const input = b64url(JSON.stringify(header)) + '.' + b64url(JSON.stringify(payload))
  const sig = createSign('RSA-SHA256').update(input).sign(privateKey())
  return input + '.' + b64url(sig)
}

async function launch(person) {
  const s = new Session(person.label)
  const token = launchToken(person)
  const r = await s.raw('POST', BASE + '/launch', {
    headers: { 'content-type': 'application/x-www-form-urlencoded' },
    body: new URLSearchParams({ token }).toString(),
  })
  if (r.status !== 303) fail(`Launch as ${person.label} was refused (HTTP ${r.status}). Look at LaunchRefused entries in ${BASE}/hub/audit.`)
  return s
}

const PEOPLE = {
  alex: { label: 'Alex', sub: 'alex.morgan@northwind.example', name: 'Alex Morgan', email: 'alex.morgan@northwind.example', role: 'Admin', programId: null },
  mary: { label: 'Mary', sub: 'mary.chen@northwind.example', name: 'Mary Chen', email: 'mary.chen@northwind.example', role: 'Admin', programId: 10 },
  raj: { label: 'Raj', sub: 'raj.patel@northwind.example', name: 'Raj Patel', email: 'raj.patel@northwind.example', role: 'Admin', programId: 20 },
  lee: { label: 'Lee', sub: 'lee.brooks@northwind.example', name: 'Lee Brooks', email: 'lee.brooks@northwind.example', role: 'Viewer', programId: 10 },
}

const same = (a, b) => JSON.stringify(a) === JSON.stringify(b)
const step = (n, text) => console.log(`\n[${n}] ${text}`)

// ---------- 1-2. Super admin, the app, its key and return URLs ----------

async function superAdminSetup() {
  step(1, 'Super admin signs in with the one-time code')
  const codeFile = readFileSync(env('QB_CODE_FILE'), 'utf8')
  const code = /^\s*Code:\s*(\S+)/m.exec(codeFile)?.[1]
  if (!code) fail('QB_CODE_FILE has no "Code: <code>" line.')
  const sa = new Session('super admin')
  const r = await sa.raw('POST', BASE + '/signin/code', {
    headers: { 'content-type': 'application/x-www-form-urlencoded', origin: new URL(HUB).origin },
    body: new URLSearchParams({ code }).toString(),
  })
  if (r.status !== 303) fail(`The sign-in code was not accepted (HTTP ${r.status}). Codes work once and expire; make a new one.`)
  console.log('  signed in (session cookie received:', sa.jar.size > 0, ')')

  step(2, `Register the app "${APP_NAME}"`)
  let apps = (await sa.api('GET', '/hub/apps')).json
  let app = apps.find((a) => a.key === APP_KEY)
  if (!app) {
    app = (await sa.api('POST', '/hub/apps', { key: APP_KEY, name: APP_NAME })).json
    console.log('  created app', app.id)
  } else console.log('  app exists', app.id)

  const wantKeys = ['ProgramId']
  if (app.name !== APP_NAME || !app.isEnabled || !same(app.scopeKeys, wantKeys) || app.maxRecords !== 5000 || app.appAdminsCanManageConnections !== false) {
    app = (await sa.api('PUT', `/hub/apps/${app.id}`, {
      version: app.version, name: APP_NAME, isEnabled: true, scopeKeys: wantKeys, maxRecords: 5000, appAdminsCanManageConnections: false,
    })).json
    console.log('  settings updated (scope keys ProgramId, ceiling 5000, app admins cannot add data sources, enabled)')
  } else console.log('  settings already right')

  const livePem = createPublicKey(publicPem()).export({ type: 'spki', format: 'pem' })
  const existingKey = app.keys.find((k) => k.kid === KID)
  if (existingKey && !existingKey.revokedAtUtc) console.log(`  signing key ${KID} already registered`)
  else if (existingKey) fail(`Signing key ${KID} was revoked earlier; use a new QB_KEY_ID.`)
  else {
    await sa.api('POST', `/hub/apps/${app.id}/keys`, { kid: KID, algorithm: 'RS256', publicKeyPem: livePem })
    console.log(`  signing key ${KID} added`)
  }

  if (RETURN_URL) {
    if (!same([...app.returnUrls], [RETURN_URL])) {
      await sa.api('PUT', `/hub/apps/${app.id}/return-urls`, { urls: [RETURN_URL] })
      console.log('  return URLs set to', RETURN_URL)
    } else console.log('  return URLs already right')
  }
  return { sa, app }
}

// ---------- 3-5. Data source, policy, scope rules ----------

async function dataSourceSetup(sa, app) {
  step(3, 'Super admin switches into the app')
  await sa.api('POST', '/hub/view-app', { appId: app.id })

  step(4, `Data source "${DS_NAME}" (${DS_MODE})`)
  let list = (await sa.api('GET', '/admin/data-sources')).json
  let ds = list.find((d) => d.name === DS_NAME)
  if (DS_MODE === 'managed') {
    const connection = {
      host: env('QB_DS_HOST'), port: Number(env('QB_DS_PORT')), database: env('QB_DS_DATABASE'),
      username: env('QB_DS_USER'), password: env('QB_DS_PASSWORD'), sslMode: 'Require',
    }
    if (!ds) {
      const created = (await sa.api('POST', '/admin/data-sources', { name: DS_NAME, description: 'Made-up Northwind program data (read-only).', provider: 'SqlServer', connection })).json
      ds = { id: created.id ?? created.dataSourceId ?? created }
      console.log('  created data source', ds.id)
    } else {
      await sa.api('PUT', `/admin/data-sources/${ds.id}`, { name: DS_NAME, description: 'Made-up Northwind program data (read-only).', connection })
      console.log('  data source exists; connection re-applied', ds.id)
    }
  } else if (DS_MODE === 'existing') {
    if (!ds) fail(`No data source named "${DS_NAME}" in the app yet. Insert its row first (see README, "existing" mode), then run again.`)
    console.log('  found existing data source', ds.id)
  } else fail('QB_DS_MODE must be managed or existing.')

  let detail = (await sa.api('GET', `/admin/data-sources/${ds.id}`)).json
  if (!detail.isActive && DS_MODE === 'managed') await sa.api('PUT', `/admin/data-sources/${ds.id}/active`, { isActive: true })
  if (!detail.locked) {
    await sa.api('PUT', `/admin/data-sources/${ds.id}/lock`, { locked: true })
    console.log('  locked it explicitly (it was not locked)')
  }
  detail = (await sa.api('GET', `/admin/data-sources/${ds.id}`)).json
  console.log('  locked:', detail.locked, ' active:', detail.isActive, ' catalogError:', detail.catalogError ?? 'none')
  if (!detail.locked) fail('The data source is not locked.')

  step(5, 'Catalog policy, record limit, data scope rules')
  await sa.api('PUT', `/admin/data-sources/${ds.id}/catalog-policy`, { catalogScope: 'Views', allowedObjects: ALLOWED })
  await sa.api('PUT', `/admin/data-sources/${ds.id}/record-limit`, { maxRecords: 5000 })
  const scope = (await sa.api('GET', `/admin/data-sources/${ds.id}/data-scope`)).json
  if (!scope.declaredKeys.includes('ProgramId')) fail('ProgramId is not a scope key of the app (declared: ' + scope.declaredKeys.join(', ') + ').')
  const rules = [
    { objectName: 'sales.vw_ProgramEnrollments', state: 'Scoped', mappings: { ProgramId: 'ProgramId' } },
    { objectName: 'sales.vw_ProgramSummary', state: 'Scoped', mappings: { ProgramId: 'ProgramId' } },
    { objectName: 'sales.vw_ProgramBudgets', state: 'Scoped', mappings: { ProgramId: 'OwningProgramId' } },
    { objectName: 'sales.vw_Programs', state: 'NotScoped' },
    { objectName: 'sales.vw_ProgramMonthly', state: 'Scoped', mappings: { ProgramId: 'ProgramId' } },
    { objectName: 'sales.vw_ProgramFunnel', state: 'Scoped', mappings: { ProgramId: 'ProgramId' } },
    { objectName: 'sales.vw_ProgramSessions', state: 'Scoped', mappings: { ProgramId: 'ProgramId' } },
  ]
  await sa.api('PUT', `/admin/data-sources/${ds.id}/data-scope`, { objects: rules })
  const after = (await sa.api('GET', `/admin/data-sources/${ds.id}`)).json
  const scopeAfter = (await sa.api('GET', `/admin/data-sources/${ds.id}/data-scope`)).json
  console.log('  catalog scope:', after.catalogScope, ' allowlist:', after.allowedObjects.length, ' max records:', after.maxRecords)
  for (const o of scopeAfter.objects) console.log(`  ${o.objectName}: ${o.state}`, Object.entries(o.mappings ?? {}).map(([k, v]) => `${k}->${v}`).join(', '), o.staleKeys?.length ? 'STALE ' + o.staleKeys : '')
  return ds.id
}

// ---------- 6. Starting set as Alex ----------

const col = (alias, name, dataType, aggregate = 'None', outAlias = null, i = 0) => ({
  tableAlias: alias, columnName: name, alias: outAlias, dataType, aggregate, orderIndex: i, isVisible: true,
})
// A sort is a column name (ascending) or [column name, 'Desc'].
const def = (obj, columns, sorts = [], rowLimit = 1000) => ({
  source: { schemaName: 'sales', objectName: obj, kind: 'View', alias: 'v' },
  joins: [], columns: columns.map((c, i) => ({ ...c, orderIndex: i })),
  filters: { operator: 'And', conditions: [], groups: [] },
  groupBy: [], having: null,
  sorts: sorts.map((s, i) => ({ tableAlias: 'v', columnName: [].concat(s)[0], direction: [].concat(s)[1] ?? 'Asc', orderIndex: i })),
  parameters: [], rowLimit,
})

const CONTENT = [
  {
    name: 'Enrolments by program',
    description: 'How many people enrolled in each program, and the fees collected.',
    definition: def('vw_ProgramEnrollments', [
      col('v', 'ProgramId', 'Number'), col('v', 'ProgramName', 'Text'),
      col('v', 'EnrollmentId', 'Number', 'Count', 'Enrolments'), col('v', 'FeeAmount', 'Number', 'Sum', 'Total fees'),
    ], ['ProgramId']),
    chart: { name: 'Enrolments per program', type: 'Bar', options: { version: 1, x: 'ProgramName', measures: ['Enrolments'] } },
  },
  {
    name: 'Enrolment status',
    description: 'Enrolments by their current status.',
    definition: def('vw_ProgramEnrollments', [col('v', 'Status', 'Text'), col('v', 'EnrollmentId', 'Number', 'Count', 'Enrolments')], ['Status']),
    chart: { name: 'Enrolments by status', type: 'Pie', options: { version: 1, x: 'Status', measures: ['Enrolments'] } },
  },
  {
    name: 'Budget by program',
    description: 'Budget per program and fiscal year.',
    definition: def('vw_ProgramBudgets', [
      col('v', 'ProgramName', 'Text'), col('v', 'FiscalYear', 'Number'), col('v', 'Amount', 'Number', 'Sum', 'Budget'),
    ], ['ProgramName', 'FiscalYear']),
    chart: { name: 'Budget by program and year', type: 'Bar', options: { version: 1, x: 'ProgramName', series: 'FiscalYear', measures: ['Budget'], stacked: true } },
  },
  {
    name: 'Program list',
    description: 'All programs.',
    definition: def('vw_Programs', [col('v', 'ProgramId', 'Number'), col('v', 'ProgramName', 'Text'), col('v', 'Region', 'Text')], ['ProgramId']),
    chart: { name: 'Programs', type: 'Table', options: { version: 1, table: { columns: ['ProgramId', 'ProgramName', 'Region'] } } },
  },
]

const COMPACT = { style: 'compact', decimals: 1 }

// Showcase queries: demo/sql/programs-showcase.sql. Each has one chart; names are the identity used to find them again.
const SHOWCASE = {
  enrolTotal: {
    name: 'Total enrolments against target',
    description: 'All enrolments from January 2025 to December 2026, next to the target set for the same months.',
    definition: def('vw_ProgramMonthly', [col('v', 'Enrolments', 'Number', 'Sum', 'Total enrolments'), col('v', 'EnrolmentTarget', 'Number', 'Sum', 'Enrolment target')]),
    chart: { name: 'Enrolments vs target', type: 'Counter', options: { version: 1, measures: [], counter: { value: 'Total enrolments', label: 'Enrolments, Jan 2025 - Dec 2026', decimals: 0, target: 'Enrolment target' } } },
  },
  enrolTrend: {
    name: 'Enrolments by month',
    description: 'Enrolments across all programs, month by month, with the change from the month before.',
    definition: def('vw_ProgramMonthly', [col('v', 'MonthStart', 'Date'), col('v', 'Enrolments', 'Number', 'Sum', 'Enrolments')], ['MonthStart']),
    chart: { name: 'Enrolment trend', type: 'Trend', options: { version: 1, x: 'MonthStart', measures: ['Enrolments'] } },
  },
  enrolByProgram: {
    name: 'Enrolments by month and program',
    description: 'Monthly enrolments for each program, to compare size and seasonality.',
    definition: def('vw_ProgramMonthly', [col('v', 'MonthLabel', 'Text'), col('v', 'ProgramName', 'Text'), col('v', 'Enrolments', 'Number', 'Sum', 'Enrolments')], ['MonthLabel', 'ProgramName']),
    chart: { name: 'Enrolments by program over time', type: 'Line', options: { version: 1, x: 'MonthLabel', series: 'ProgramName', measures: ['Enrolments'] } },
  },
  completions: {
    name: 'Completions by month and program',
    description: 'People who finished each month, stacked by program.',
    definition: def('vw_ProgramMonthly', [col('v', 'MonthLabel', 'Text'), col('v', 'ProgramName', 'Text'), col('v', 'Completions', 'Number', 'Sum', 'Completions')], ['MonthLabel', 'ProgramName']),
    chart: { name: 'Completions stacked by program', type: 'Area', options: { version: 1, x: 'MonthLabel', series: 'ProgramName', measures: ['Completions'], stacked: true } },
  },
  spendBudget: {
    name: 'Spend and budget by month',
    description: 'What was spent each month (bars) against what was budgeted (line).',
    definition: def('vw_ProgramMonthly', [col('v', 'MonthLabel', 'Text'), col('v', 'Spend', 'Number', 'Sum', 'Spend'), col('v', 'Budget', 'Number', 'Sum', 'Budget')], ['MonthLabel']),
    chart: { name: 'Spend vs budget', type: 'Combo', options: { version: 1, x: 'MonthLabel', measures: ['Spend', 'Budget'], lineMeasures: ['Budget'], format: COMPACT } },
  },
  attendance: {
    name: 'Attendance rate by program',
    description: 'Average share of enrolled people who attended, per program.',
    definition: def('vw_ProgramMonthly', [col('v', 'ProgramName', 'Text'), col('v', 'AttendanceRate', 'Number', 'Avg', 'Attendance rate')], ['ProgramName']),
    chart: { name: 'Average attendance rate', type: 'HorizontalBar', options: { version: 1, x: 'ProgramName', measures: ['Attendance rate'], sortX: 'desc', format: { style: 'number', decimals: 1 } } },
  },
  funnel: {
    name: 'Participants by stage',
    description: 'How many people reach each stage, from applying to finding work.',
    definition: def('vw_ProgramFunnel', [col('v', 'StageOrder', 'Number'), col('v', 'Stage', 'Text'), col('v', 'Participants', 'Number', 'Sum', 'Participants')], ['StageOrder']),
    chart: { name: 'Participant funnel', type: 'Funnel', options: { version: 1, x: 'Stage', measures: ['Participants'], sortX: 'none' } },
  },
  variance: {
    name: 'Budget variance by program',
    description: 'Budget minus spend for each program over the two years: positive means under budget.',
    definition: def('vw_ProgramMonthly', [col('v', 'ProgramName', 'Text'), col('v', 'Variance', 'Number', 'Sum', 'Budget variance')], ['ProgramName']),
    chart: { name: 'Under and over budget', type: 'Waterfall', options: { version: 1, x: 'ProgramName', measures: ['Budget variance'], sortX: 'none', format: COMPACT } },
  },
  sessions: {
    name: 'Session size and cost',
    description: 'Every 2026 session: how many came, what each seat cost, and how people rated it.',
    definition: def('vw_ProgramSessions', [
      col('v', 'ProgramName', 'Text'), col('v', 'Attendees', 'Number'),
      col('v', 'CostPerAttendee', 'Number', 'None', 'Cost per attendee'), col('v', 'Satisfaction', 'Number'),
    ], ['ProgramName', 'Attendees']),
    chart: { name: 'Attendees vs cost per attendee', type: 'Scatter', options: { version: 1, x: 'Attendees', series: 'ProgramName', measures: ['Cost per attendee'], size: 'Satisfaction' } },
  },
  spendByYear: {
    name: 'Spend by program and year',
    description: 'Total spend per program in 2025 and 2026.',
    definition: def('vw_ProgramMonthly', [col('v', 'ProgramName', 'Text'), col('v', 'YearNumber', 'Number'), col('v', 'Spend', 'Number', 'Sum', 'Spend')], ['ProgramName', 'YearNumber']),
    chart: { name: 'Spend by program and year', type: 'Pivot', options: { version: 1, measures: [], pivot: { rows: ['ProgramName'], column: 'YearNumber', values: ['Spend'], totals: true, shade: true }, format: { style: 'currency', currency: 'USD', decimals: 0 } } },
  },
  topSessions: {
    name: 'Sessions ranked by satisfaction',
    description: 'Every session, best rated first. A program admin sees only their own program.',
    definition: def('vw_ProgramSessions', [
      col('v', 'SessionName', 'Text'), col('v', 'ProgramName', 'Text'), col('v', 'SessionDate', 'Date'),
      col('v', 'Attendees', 'Number'), col('v', 'CostPerAttendee', 'Number', 'None', 'Cost per attendee'), col('v', 'Satisfaction', 'Number'),
    ], [['Satisfaction', 'Desc'], 'SessionName']),
    chart: { name: 'Sessions by satisfaction', type: 'Table', options: { version: 1, measures: [], table: { columns: ['SessionName', 'ProgramName', 'SessionDate', 'Attendees', 'Cost per attendee', 'Satisfaction'] } } },
  },
}

const SHOWCASE_DASHBOARDS = [
  {
    name: 'Program performance',
    description: 'Enrolments, completions, spending and attendance across programs, month by month.',
    tiles: [
      { section: ['Headline numbers', 'Enrolments against target, and how they are moving'], y: 0 },
      { key: 'enrolTotal', x: 0, y: 1, w: 4, h: 6 },
      { key: 'enrolTrend', x: 4, y: 1, w: 8, h: 6 },
      { section: ['Seasonality and mix', 'Each program peaks at a different time of year'], y: 7 },
      { key: 'enrolByProgram', x: 0, y: 8, w: 6, h: 9 },
      { key: 'completions', x: 6, y: 8, w: 6, h: 9 },
      { section: ['Money and attendance', 'Spending against budget, and who turns up'], y: 17 },
      { key: 'spendBudget', x: 0, y: 18, w: 7, h: 9 },
      { key: 'attendance', x: 7, y: 18, w: 5, h: 9 },
    ],
  },
  {
    name: 'Funnel and outcomes',
    description: 'How people move through programs, where money goes, and which sessions work best.',
    tiles: [
      { section: ['From application to work', 'Where people drop out, and which programs are over or under budget'], y: 0 },
      { key: 'funnel', x: 0, y: 1, w: 5, h: 7 },
      { key: 'variance', x: 5, y: 1, w: 7, h: 7 },
      { section: ['Sessions and spending', 'Bigger sessions cost less per person'], y: 8 },
      { key: 'sessions', x: 0, y: 9, w: 7, h: 10 },
      { key: 'spendByYear', x: 7, y: 9, w: 5, h: 10 },
      { section: ['Sessions ranked', 'Every session, best rated first'], y: 19 },
      { key: 'topSessions', x: 0, y: 20, w: 12, h: 10 },
    ],
  },
]

async function ensureQuery(alex, queries, c, dataSourceId, { strict }) {
  const found = queries.find((q) => q.name === c.name)
  const saved = (await alex.api('POST', '/queries', { id: found?.id ?? null, name: c.name, description: c.description, dataSourceId, definition: c.definition })).json
  const queryId = saved.id
  const vizList = (await alex.api('GET', `/queries/${queryId}/visualizations`)).json
  const foundViz = vizList.find((v) => v.name === c.chart.name)
  const body = { name: c.chart.name, type: c.chart.type, options: c.chart.options }
  const viz = foundViz
    ? (await alex.api('PUT', `/queries/${queryId}/visualizations/${foundViz.id}`, body)).json
    : (await alex.api('POST', `/queries/${queryId}/visualizations`, body)).json
  const data = (await alex.api('POST', `/queries/${queryId}/visualizations/${viz.id}/data`, { parameterValues: {} })).json
  console.log(`  ${found ? 'updated' : 'created'} query "${c.name}" + ${c.chart.type} "${c.chart.name}": ${data.rowCount} rows${data.truncated ? ' (TRUNCATED)' : ''}`)
  if (strict && (!(data.rowCount > 0) || data.truncated)) fail(`Chart "${c.chart.name}" drew ${data.rowCount} rows${data.truncated ? ' and was truncated' : ''}.`)
  return { ...c, queryId, vizId: viz.id }
}

// Finds the dashboard by name (creates it if missing), puts the tiles in, publishes it.
async function ensureDashboard(alex, name, description, specs) {
  const dashboards = (await alex.api('GET', '/dashboards')).json
  let dash = dashboards.find((d) => d.name === name)
  if (!dash) dash = (await alex.api('POST', '/dashboards', { name, description })).json
  let detail = (await alex.api('GET', `/dashboards/${dash.id}`)).json
  const existing = (pred) => detail.tiles.find(pred)?.id ?? null
  const isSection = (t) => String(t.kind).toLowerCase() === 'section'
  const tiles = specs.map((t, order) => {
    if (t.section) {
      const [title, text] = t.section
      return { id: existing((x) => isSection(x) && x.title === title), kind: 'Section', order, x: 0, y: t.y, width: 12, height: 1, autoHeight: false, title, text, visualizationId: null }
    }
    return {
      id: existing((x) => x.chart?.visualizationId === t.made.vizId), kind: 'Chart', order, x: t.x, y: t.y, width: t.w, height: t.h, autoHeight: false,
      title: null, text: null, visualizationId: t.made.vizId,
    }
  })
  await alex.api('PUT', `/dashboards/${dash.id}/tiles`, { version: detail.version, tiles })
  await alex.api('PUT', `/dashboards/${dash.id}/published`, { published: true })
  detail = (await alex.api('GET', `/dashboards/${dash.id}`)).json
  console.log(`  dashboard "${name}" ${dash.id}: ${detail.tiles.length} tiles, published: ${detail.published}`)
  return { id: dash.id, name, detail }
}

async function startingSet(dataSourceId) {
  step(6, 'Launch as Alex and create the starting set')
  const alex = await launch(PEOPLE.alex)
  const queries = (await alex.api('GET', '/queries')).json
  const made = []
  for (const c of CONTENT) made.push(await ensureQuery(alex, queries, c, dataSourceId, { strict: false }))

  const overview = await ensureDashboard(alex, 'Program overview', 'Enrolments, budgets and programs at a glance.', [
    { section: ['Program overview', 'Enrolments, budgets and programs at a glance'], y: 0 },
    { made: made[0], x: 0, y: 1, w: 6, h: 8 },
    { made: made[1], x: 6, y: 1, w: 6, h: 8 },
    { made: made[2], x: 0, y: 9, w: 12, h: 8 },
    { made: made[3], x: 0, y: 17, w: 12, h: 8 },
  ])

  step('6b', 'Showcase queries, charts and dashboards (needs demo/sql/programs-showcase.sql)')
  const showcase = {}
  for (const [key, c] of Object.entries(SHOWCASE)) showcase[key] = await ensureQuery(alex, queries, c, dataSourceId, { strict: true })
  const dashboards = [overview]
  for (const d of SHOWCASE_DASHBOARDS) {
    dashboards.push(await ensureDashboard(alex, d.name, d.description, d.tiles.map((t) => (t.key ? { ...t, made: showcase[t.key] } : t))))
  }
  return { dashboardId: overview.id, made, detail: overview.detail, showcase, dashboards }
}

// ---------- verification as each person ----------

const rowsOf = (r) => r.json.rows
const distinct = (rows, key) => [...new Set(rows.map((x) => x[key]))].sort((a, b) => a - b)

let verifyFailures = 0
const check = (ok, text) => {
  if (!ok) verifyFailures++
  console.log(`  ${ok ? 'ok  ' : 'FAIL'} ${text}`)
}

const drawShowcase = async (session, m) => (await session.api('POST', `/queries/${m.queryId}/visualizations/${m.vizId}/data`, { parameterValues: {} })).json
const firstMeasureTotal = (m, data) => {
  const o = m.chart.options
  const name = o.counter?.value ?? o.measures[0] ?? o.pivot?.values[0]
  return data.rows.reduce((n, r) => n + Number(r[name]), 0)
}

// A program admin sees only their own program on every showcase chart; charts with no program column are checked by size.
async function verifyShowcaseScope(person, session, set) {
  const alexS = await launch(PEOPLE.alex)
  const programName = { 10: 'Youth Coding Academy', 20: 'Adult Literacy' }[person.programId]
  for (const m of Object.values(set.showcase)) {
    const mine = await drawShowcase(session, m)
    const all = await drawShowcase(alexS, m)
    const names = mine.columns.some((c) => c.name === 'ProgramName') ? [...new Set(mine.rows.map((r) => r['ProgramName']))] : null
    if (names) {
      check(mine.rowCount > 0 && names.length === 1 && names[0] === programName, `${person.label} "${m.chart.name}": ${mine.rowCount} row(s), programs ${JSON.stringify(names)} (Alex: ${all.rowCount} rows)`)
    } else {
      const a = firstMeasureTotal(m, all)
      const b = firstMeasureTotal(m, mine)
      check(mine.rowCount > 0 && b > 0 && b < a, `${person.label} "${m.chart.name}": no program column, measure total ${b} against Alex's ${a}`)
    }
  }
  // The counter has no program column, so tie it to the line chart, which does: the program's monthly enrolments must add up.
  const line = await drawShowcase(alexS, set.showcase.enrolByProgram)
  const expected = line.rows.filter((r) => r['ProgramName'] === programName).reduce((n, r) => n + Number(r['Enrolments']), 0)
  const counter = firstMeasureTotal(set.showcase.enrolTotal, await drawShowcase(session, set.showcase.enrolTotal))
  check(counter === expected, `${person.label} counter total ${counter} equals ${programName}'s monthly enrolments summed from Alex's line chart (${expected})`)
}

async function verify(set) {
  step(7, 'Verify what Mary, Raj and Lee see')
  const [q1, , q3] = set.made
  for (const key of ['mary', 'raj']) {
    const p = PEOPLE[key]
    const s = await launch(p)
    const enrol = rowsOf(await s.api('POST', `/queries/${q1.queryId}/visualizations/${q1.vizId}/data`, { parameterValues: {} }))
    const budget = rowsOf(await s.api('POST', `/queries/${q3.queryId}/visualizations/${q3.vizId}/data`, { parameterValues: {} }))
    const total = enrol.reduce((n, r) => n + Number(r['Enrolments']), 0)
    check(enrol.length === 1 && distinct(enrol, 'ProgramName').length === 1, `${p.label} (program ${p.programId}) "Enrolments by program": ${enrol.length} row(s), ${total} enrolments, programs: ${JSON.stringify(distinct(enrol, 'ProgramName'))}`)
    check(budget.length > 0 && distinct(budget, 'ProgramName').length === 1, `${p.label} "Budget by program": ${budget.length} row(s), programs: ${JSON.stringify(distinct(budget, 'ProgramName'))}`)
    const list = (await s.api('GET', '/queries')).json
    console.log(`       ${p.label} sees ${list.length} saved queries`)
    await verifyShowcaseScope(p, s, set)
  }
  const alexS = await launch(PEOPLE.alex)
  const all = rowsOf(await alexS.api('POST', `/queries/${q1.queryId}/visualizations/${q1.vizId}/data`, { parameterValues: {} }))
  check(all.length === 5, `Alex (unrestricted) "Enrolments by program": ${all.length} rows, ${all.reduce((n, r) => n + Number(r['Enrolments']), 0)} enrolments`)

  const lee = await launch(PEOPLE.lee)
  const queriesAsLee = await lee.api('GET', '/queries', undefined, [200, 401, 403, 404])
  check(queriesAsLee.status === 403 || (queriesAsLee.status === 200 && queriesAsLee.json.length === 0), `Lee cannot list queries (HTTP ${queriesAsLee.status}${queriesAsLee.json?.length != null ? ', ' + queriesAsLee.json.length + ' items' : ''})`)
  const dashes = (await lee.api('GET', '/dashboards')).json
  const wantNames = set.dashboards.map((d) => d.name).sort()
  check(same(dashes.map((d) => d.name).sort(), wantNames), `Lee sees ${dashes.length} dashboard(s): ${JSON.stringify(dashes.map((d) => d.name))}`)
  for (const dashboard of set.dashboards) {
    const ld = (await lee.api('GET', `/dashboards/${dashboard.id}`)).json
    const chartTiles = ld.tiles.filter((t) => String(t.kind).toLowerCase() === 'chart')
    check(chartTiles.length === dashboard.detail.tiles.filter((t) => String(t.kind).toLowerCase() === 'chart').length, `Lee opens "${dashboard.name}": ${chartTiles.length} chart tile(s)`)
    for (const t of chartTiles) {
      const meta = (await lee.api('GET', `/dashboards/${dashboard.id}/tiles/${t.id}/chart`)).json
      const data = (await lee.api('POST', `/queries/${meta.savedQueryId}/visualizations/${meta.visualization.id}/data`, { parameterValues: {}, dashboardId: dashboard.id, tileId: t.id })).json
      const programs = data.columns.some((c) => c.name === 'ProgramName') ? distinct(data.rows, 'ProgramName') : null
      const isPrograms = meta.visualization.name === 'Programs'
      check(isPrograms ? data.rowCount === 5 : data.rowCount > 0 && (programs?.length ?? 0) <= 1 && programs?.length !== 5,
        `Lee "${dashboard.name}" tile "${meta.visualization.name}": ${data.rowCount} row(s)${programs ? ', programs ' + JSON.stringify(programs) : ' (no program column)'}`)
      if (meta.visualization.name === 'Enrolments by status') {
        const n = data.rows.reduce((a, r) => a + Number(r['Enrolments']), 0)
        check(n === 20, `Lee "Enrolments by status" counts add up to ${n} (program 10 has 20 enrolments)`)
      }
    }
  }
  const direct = await lee.api('POST', `/queries/${q1.queryId}/visualizations/${q1.vizId}/data`, { parameterValues: {} }, [200, 403, 404])
  check(direct.status !== 200, `Lee asking for a chart outside a dashboard is refused (HTTP ${direct.status})`)
  return verifyFailures
}

// ---------- main ----------

const { sa, app } = await superAdminSetup()
if (args.has('--app-only')) {
  console.log(`\nApp "${APP_NAME}" registered. App id: ${app.id}`)
  process.exit(0)
}
const dsId = await dataSourceSetup(sa, app)
const set = await startingSet(dsId)
let failures = 0
if (args.has('--verify')) failures = await verify(set)

console.log('\nSummary')
console.log(`  app ${app.id} (${APP_KEY}), data source ${dsId}, ${set.made.length + Object.keys(set.showcase).length} queries with charts`)
for (const d of set.dashboards) console.log(`  dashboard "${d.name}" ${d.id} (${d.detail.tiles.length} tiles, published)`)
if (failures) {
  console.log(`  ${failures} verification check(s) FAILED`)
  process.exit(2)
}
