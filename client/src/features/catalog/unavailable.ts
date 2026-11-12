import type { DataSourceProvider } from '../../types/index.ts'

export const BUSINESS_UNAVAILABLE_REASON =
  "Can't be used: its full name clashes with another table or view. Ask your admin to add a view with a unique name."

export function adminUnavailableReason(clashesWith: string[]): string {
  return `Clashes with ${clashesWith.join(', ')}. QueryBuilder can't tell these names apart. Add a uniquely named view over the one you want to expose.`
}

const PROVIDER_LABELS: Record<DataSourceProvider, string> = {
  sqlServer: 'SQL Server',
  postgreSql: 'PostgreSQL',
  mySql: 'MySQL',
  sqlite: 'SQLite',
}

export function providerLabel(provider: DataSourceProvider): string {
  return PROVIDER_LABELS[provider] ?? provider
}

// Quoted when a dot would make the plain form ambiguous, matching the server's clashesWith text.
export function qualifiedName(schemaName: string, name: string): string {
  return schemaName.includes('.') || name.includes('.') ? `"${schemaName}"."${name}"` : `${schemaName}.${name}`
}

export function objectRowKey(schemaName: string, name: string): string {
  return JSON.stringify([schemaName, name])
}
