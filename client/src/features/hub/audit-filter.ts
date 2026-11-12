/** The actions an operator looks for across apps; the server accepts any AuditAction name. */
export const HUB_AUDIT_ACTIONS = [
  'launchSucceeded',
  'launchRefused',
  'superAdminSignedIn',
  'superAdminSignInRefused',
  'superAdminViewedApp',
  'appRegistryChanged',
  'dashboardPublished',
  'dashboardUnpublished',
  'dataSourceCreated',
  'dataSourceUpdated',
  'dataSourceActivated',
  'dataSourceDeactivated',
  'dataSourceLockChanged',
  'dataSourceMoved',
  'encryptionKeyRotated',
] as const

/** `dataSourceLockChanged` -> `Data source lock changed`. */
export function actionLabel(action: string): string {
  const words = action.replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase()
  return words.charAt(0).toUpperCase() + words.slice(1)
}
