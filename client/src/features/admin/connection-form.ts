import type { ConnectionDetailsDto, ConnectionInput, ConnectionSslMode, DataSourceProvider } from '../../types/index.ts'

export const MANAGEABLE_PROVIDERS: DataSourceProvider[] = ['sqlServer', 'postgreSql']

export const SSL_OPTIONS: { value: ConnectionSslMode; label: string }[] = [
  { value: 'disable', label: 'Off' },
  { value: 'require', label: 'Required' },
  { value: 'verifyFull', label: 'Required and verify certificate' },
]

export const REENTER_PASSWORD_MESSAGE = 'Re-enter the password when changing the host, port, username or lowering encryption.'
export const REENTER_UNREADABLE_MESSAGE = "Re-enter the password: the saved details can't be read with the current key."

const SSL_RANK: Record<ConnectionSslMode, number> = { disable: 0, require: 1, verifyFull: 2 }

export function defaultPort(provider: DataSourceProvider): number {
  return provider === 'postgreSql' ? 5432 : 1433
}

export function sslLabel(mode: ConnectionSslMode): string {
  return SSL_OPTIONS.find((o) => o.value === mode)?.label ?? mode
}

export interface ConnectionForm {
  host: string
  /** Text, so a half-typed port isn't forced to a number. Blank means the provider's default. */
  port: string
  database: string
  username: string
  password: string
  sslMode: ConnectionSslMode
}

export function emptyForm(provider: DataSourceProvider): ConnectionForm {
  return { host: '', port: String(defaultPort(provider)), database: '', username: '', password: '', sslMode: 'require' }
}

export function formFromDetails(details: ConnectionDetailsDto): ConnectionForm {
  return {
    host: details.host,
    port: String(details.port),
    database: details.database,
    username: details.username,
    password: '',
    sslMode: details.sslMode,
  }
}

/** Switching provider moves the port only if it still holds the old provider's default. */
export function portAfterProviderChange(current: string, from: DataSourceProvider, to: DataSourceProvider): string {
  return current.trim() === String(defaultPort(from)) || current.trim() === '' ? String(defaultPort(to)) : current
}

export function parsePort(text: string, provider: DataSourceProvider): number | null {
  const trimmed = text.trim()
  if (trimmed === '') return defaultPort(provider)
  return /^\d{1,5}$/.test(trimmed) ? Number(trimmed) : null
}

// Mirrors the server's allowlist, which stays the authority. Names, IPv4/IPv6 addresses and, for SQL Server,
// host\instance. Prefixes (np:, tcp:), (localdb), paths and connection-string syntax are all outside it.
const HOST_NAME = /^[A-Za-z0-9._-]+$/
const HOST_IPV6 = /^\[[0-9A-Fa-f:.]+\]$/
const HOST_INSTANCE = /^[A-Za-z0-9._-]+\\[A-Za-z0-9_$#-]+$/
// eslint-disable-next-line no-control-regex
const CONTROL = /[\u0000-\u001f\u007f]/

// SQL Server limits the final Data Source value, which is built as "tcp:" + host + "," + port.
export function sqlServerDataSourceLength(host: string, port: number | null): number {
  return `tcp:${host},${port ?? ''}`.length
}

/** True once anything has been typed or chosen, so dismissing the dialog would lose work. */
export function isFormDirty(form: ConnectionForm, provider: DataSourceProvider): boolean {
  return (
    form.host !== '' ||
    form.database !== '' ||
    form.username !== '' ||
    form.password !== '' ||
    form.sslMode !== 'require' ||
    form.port.trim() !== String(defaultPort(provider))
  )
}

export function isAcceptableHost(host: string, provider: DataSourceProvider): boolean {
  return HOST_NAME.test(host) || HOST_IPV6.test(host) || (provider === 'sqlServer' && HOST_INSTANCE.test(host))
}

export type FormField = 'host' | 'port' | 'database' | 'username' | 'password'

export function validateConnection(form: ConnectionForm, provider: DataSourceProvider): Partial<Record<FormField, string>> {
  const errors: Partial<Record<FormField, string>> = {}
  const host = form.host.trim()
  if (host === '') errors.host = 'Enter the host.'
  else if (host.length > 255) errors.host = 'The host can be at most 255 characters.'
  else if (!isAcceptableHost(host, provider)) {
    errors.host =
      provider === 'sqlServer'
        ? 'Use a host name or IP address, or host\\instance. Prefixes such as np: or tcp:, paths and (localdb) are not accepted.'
        : 'Use a host name or IP address. Prefixes, paths and special characters are not accepted.'
  } else if (provider === 'sqlServer' && sqlServerDataSourceLength(host, parsePort(form.port, provider)) > 128) {
    errors.host = 'The host is too long: with the port, SQL Server limits it to 128 characters in all.'
  }

  const port = parsePort(form.port, provider)
  if (port === null || port < 1 || port > 65535) errors.port = 'Enter a port from 1 to 65535.'

  const database = form.database.trim()
  if (database === '') errors.database = 'Enter the database name.'
  else if (database.length > 128) errors.database = 'The database name can be at most 128 characters.'
  else if (CONTROL.test(database)) errors.database = "The database name can't contain control characters."

  if (form.username === '') errors.username = 'Enter the username.'
  else if (form.username.length > 128) errors.username = 'The username can be at most 128 characters.'
  else if (CONTROL.test(form.username)) errors.username = "The username can't contain control characters."

  const passwordLimit = provider === 'sqlServer' ? 128 : 1024
  if (form.password.length > passwordLimit) errors.password = `The password can be at most ${passwordLimit.toLocaleString('en-US')} characters.`
  else if (CONTROL.test(form.password)) errors.password = "The password can't contain control characters."
  return errors
}

/**
 * A blank password keeps the saved one only while host, port and username are unchanged; the server enforces the
 * same rule, this just says so before the request is sent.
 */
export function needsPasswordReentry(
  saved: ConnectionDetailsDto | null,
  form: ConnectionForm,
  provider: DataSourceProvider,
): boolean {
  if (form.password !== '' || saved === null) return false
  return (
    saved.host.toLowerCase() !== form.host.trim().toLowerCase() ||
    saved.port !== parsePort(form.port, provider) ||
    saved.username !== form.username ||
    SSL_RANK[form.sslMode] < SSL_RANK[saved.sslMode]
  )
}

/** What is wrong with leaving the password blank, if anything. The server enforces the same rules. */
export function blankPasswordProblem(
  saved: ConnectionDetailsDto | null,
  unreadable: boolean,
  form: ConnectionForm,
  provider: DataSourceProvider,
): string | null {
  if (form.password !== '') return null
  if (unreadable) return REENTER_UNREADABLE_MESSAGE
  return needsPasswordReentry(saved, form, provider) ? REENTER_PASSWORD_MESSAGE : null
}

/**
 * Rules and "not scoped" decisions are kept by object name, so pointing the source at another server or database
 * applies them to whatever has the same names there.
 */
export function connectionTargetChanged(saved: ConnectionDetailsDto | null, form: ConnectionForm): boolean {
  if (saved === null) return false
  return saved.host.toLowerCase() !== form.host.trim().toLowerCase() || saved.database !== form.database.trim()
}

/** While the data source can't be read, its object list is empty, and acting on that would prune the allowlist. */
export function catalogControlsLocked(catalogError: string | null | undefined): boolean {
  return catalogError !== null && catalogError !== undefined && catalogError !== ''
}

export function toConnectionInput(form: ConnectionForm, provider: DataSourceProvider): ConnectionInput {
  return {
    host: form.host.trim(),
    port: parsePort(form.port, provider),
    database: form.database.trim(),
    username: form.username,
    password: form.password,
    sslMode: form.sslMode,
  }
}

export function isConnectionValid(form: ConnectionForm, provider: DataSourceProvider): boolean {
  return Object.keys(validateConnection(form, provider)).length === 0
}

export function isNameValid(name: string): boolean {
  const trimmed = name.trim()
  return trimmed.length >= 1 && trimmed.length <= 200
}

export function isDescriptionValid(description: string): boolean {
  return description.length <= 1000
}

/** Plain http sends the password readable on the wire; the page says so next to the field. */
export function shouldWarnInsecure(isSecureContext: boolean | undefined): boolean {
  return isSecureContext === false
}

export const INSECURE_PASSWORD_WARNING =
  "This page isn't using HTTPS, so the password is sent unencrypted. Use HTTPS before entering a real password."
