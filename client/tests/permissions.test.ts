import assert from 'node:assert/strict'
import { test } from 'node:test'
import { canSee, connectionReason, homeRouteFor, isHub, lockReason, navItemsFor, needsAppChoice } from '../src/features/hub/permissions.ts'
import type { MeDto } from '../src/lib/api.ts'

const app = { id: 'a1', key: 'a', name: 'App A' }
function hubMe(role: 'Admin' | 'Author' | 'Viewer' | 'Super', viewing: boolean = true): MeDto {
  const viewer = role === 'Viewer'
  const whole = role === 'Admin' || role === 'Super'
  return {
    userId: 'u',
    displayName: 'U',
    zone: null,
    app: viewing ? app : null,
    role: role === 'Super' ? 'Admin' : role,
    isSuperAdmin: role === 'Super',
    returnUrl: null,
    viewingApp: role === 'Super' && viewing ? app : null,
    permissions: { canBuildQueries: !viewer, canManageDashboards: !viewer, canPublish: whole, canAdmin: whole, isViewer: viewer },
  } as MeDto
}
const embedded: MeDto = { userId: 'u', displayName: 'U', zone: null }
const ids = (me: MeDto, probe = false) => navItemsFor(me, probe).map((i) => i.id)

test('embedded mode keeps the old nav and admin follows the probe', () => {
  assert.equal(isHub(embedded), false)
  assert.deepEqual(ids(embedded), ['queries', 'dashboards'])
  assert.deepEqual(ids(embedded, true), ['queries', 'dashboards', 'admin'])
})

test('a hub Viewer sees only Dashboards', () => {
  assert.deepEqual(ids(hubMe('Viewer'), true), ['dashboards'])
})

test('a hub Author sees Queries and Dashboards, never Admin even if the probe passed', () => {
  assert.deepEqual(ids(hubMe('Author'), true), ['queries', 'dashboards'])
})

test('a hub Admin and a super admin viewing an app see everything', () => {
  assert.deepEqual(ids(hubMe('Admin')), ['queries', 'dashboards', 'admin'])
  assert.deepEqual(ids(hubMe('Super')), ['queries', 'dashboards', 'admin'])
})

test('a super admin with no app has no app nav and is sent to choose one', () => {
  const me = hubMe('Super', false)
  assert.equal(needsAppChoice(me), true)
  assert.deepEqual(ids(me), [])
  assert.equal(homeRouteFor(me), '/choose-app')
  assert.equal(canSee('/dashboards', me), false)
  assert.equal(canSee('/choose-app', me), true)
})

test('canSee for the home route, the builder, admin and the apps console', () => {
  const routes = ['/', '/queries/new', '/admin', '/hub/apps']
  const seen = (me: MeDto) => routes.map((r) => canSee(r, me))
  assert.deepEqual(seen(embedded), [true, true, true, false])
  assert.deepEqual(seen(hubMe('Viewer')), [false, false, false, false])
  assert.deepEqual(seen(hubMe('Author')), [true, true, false, false])
  assert.deepEqual(seen(hubMe('Admin')), [true, true, true, false])
  assert.deepEqual(seen(hubMe('Super')), [true, true, true, true])
})

test('dashboards are open to every role, and a Viewer lands on them', () => {
  for (const role of ['Viewer', 'Author', 'Admin', 'Super'] as const) assert.equal(canSee('/dashboards/abc', hubMe(role)), true)
  assert.equal(homeRouteFor(hubMe('Viewer')), '/dashboards')
  assert.equal(homeRouteFor(hubMe('Author')), '/')
  assert.equal(homeRouteFor(embedded), '/')
})

test('lock and connection reasons', () => {
  assert.equal(lockReason({ locked: true, canEdit: false }), 'Locked by a super admin')
  assert.equal(lockReason({ locked: true, canEdit: true }), null)
  assert.equal(lockReason({}), null)
  assert.notEqual(connectionReason({ canManageConnection: false }), null)
  assert.equal(connectionReason({ canManageConnection: true }), null)
  assert.equal(connectionReason({}), null)
})
