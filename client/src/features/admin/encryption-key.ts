import type { ReencryptResultDto } from '../../types/index.ts'

export const KEY_BYTES = 32

/** Base64 of 32 random bytes, made in the browser and never sent anywhere. getRandomValues works on a plain-http page. */
export function generateEncryptionKey(): string {
  const bytes = new Uint8Array(KEY_BYTES)
  globalThis.crypto.getRandomValues(bytes)
  let binary = ''
  for (const byte of bytes) binary += String.fromCharCode(byte)
  return btoa(binary)
}

export function plural(count: number, one: string, many: string): string {
  return `${count.toLocaleString('en-US')} ${count === 1 ? one : many}`
}

/** One line per non-zero outcome, so the result reads as a plain list. Always at least one line. */
export function reencryptSummary(result: ReencryptResultDto): string[] {
  const lines: string[] = []
  if (result.reencrypted > 0) lines.push(`${plural(result.reencrypted, 'data source', 'data sources')} re-encrypted with the current key.`)
  if (result.alreadyCurrent > 0) lines.push(`${plural(result.alreadyCurrent, 'data source was', 'data sources were')} already on the current key.`)
  if (result.unreadable > 0) {
    lines.push(`${plural(result.unreadable, 'data source', 'data sources')} couldn't be read with any configured key. Open each one and re-enter its password.`)
  }
  if (result.skippedBecauseChanged > 0) {
    lines.push(
      `${plural(result.skippedBecauseChanged, 'data source', 'data sources')} skipped because it changed while re-encrypting. Run it again.`,
    )
  }
  return lines.length > 0 ? lines : ['There are no managed data sources to re-encrypt.']
}
