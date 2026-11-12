// Mirrors UpdateScopeKeysCommand on the server; the server stays the authority.
export const MAX_SCOPE_KEYS = 20

const VALID_KEY = /^[A-Za-z_][A-Za-z0-9_]{0,127}$/

const sameKey = (a: string, b: string) => a.toLowerCase() === b.toLowerCase()

/** Null when `key` can be added; otherwise the sentence to show. Nothing is trimmed: surrounding spaces are invalid. */
export function validateNewScopeKey(key: string, hostKeys: string[], dataSourceKeys: string[]): string | null {
  if (!VALID_KEY.test(key)) {
    return `Key '${key}' is invalid — use letters, digits and underscores only (max 128 characters).`
  }
  if (hostKeys.some((k) => sameKey(k, key))) return `'${key}' is already set in the app's code.`
  if (dataSourceKeys.some((k) => sameKey(k, key))) return `'${key}' is already added.`
  if (dataSourceKeys.length >= MAX_SCOPE_KEYS) return `A data source can have at most ${MAX_SCOPE_KEYS} scope keys.`
  return null
}

interface KeyUse {
  mappings: Record<string, string>
  staleKeys: string[]
}

/** How many objects have a rule on `key`, whether it is mapped now or already stale. */
export function countObjectsUsingKey(objects: KeyUse[], key: string): number {
  return objects.filter(
    (o) => Object.keys(o.mappings).some((k) => sameKey(k, key)) || o.staleKeys.some((k) => sameKey(k, key)),
  ).length
}

export function removeKeyWarning(count: number, key: string): string {
  return count === 1
    ? `1 view uses ${key}. It'll be hidden from scoped users until the key is added back or it's re-mapped.`
    : `${count} views use ${key}. They'll be hidden from scoped users until the key is added back or they're re-mapped.`
}
