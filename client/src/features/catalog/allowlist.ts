type Scope = 'views' | 'tables' | 'tablesAndViews'

function inScope(kind: string | undefined, scope: Scope): boolean {
  if (!kind) return false
  if (scope === 'tables') return kind === 'table'
  if (scope === 'views') return kind === 'view'
  return true
}

// Unavailable objects can't be re-ticked (their checkbox is disabled and the server only accepts keys it already
// stores), so a scope switch or a rebuild must never drop one.
export function pruneAllowlist(
  selected: ReadonlySet<string>,
  kindByKey: ReadonlyMap<string, string>,
  unavailableKeys: ReadonlySet<string>,
  scope: Scope,
): Set<string> {
  return new Set(Array.from(selected).filter((key) => unavailableKeys.has(key) || inScope(kindByKey.get(key), scope)))
}

export function initialAllowlist(
  visibleKeys: readonly string[],
  existingAllowed: readonly string[],
  unavailableKeys: ReadonlySet<string>,
): Set<string> {
  return new Set([...visibleKeys, ...existingAllowed.filter((key) => unavailableKeys.has(key))])
}
