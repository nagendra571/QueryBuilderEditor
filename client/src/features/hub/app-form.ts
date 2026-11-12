// Mirrors RegistryRules on the server, which stays the authority: these only spare a round trip and say what to fix.
export const MAX_APP_NAME = 200
export const MAX_KID = 64
export const MAX_PEM = 4000
export const MAX_RETURN_URLS = 20
export const MAX_RETURN_URL = 800
export const MAX_SCOPE_KEYS = 20
export const CEILING_MIN = 1
export const CEILING_MAX = 100_000

const APP_KEY = /^[a-z0-9][a-z0-9-]{1,62}$/
const KID = /^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$/
const SCOPE_KEY = /^[A-Za-z_][A-Za-z0-9_]{0,127}$/

export function validateAppKey(key: string): string | null {
  return APP_KEY.test(key) ? null : 'An app key is 2 to 63 characters: lowercase letters, digits and hyphens, starting with a letter or digit.'
}

export function validateAppName(name: string): string | null {
  const trimmed = name.trim()
  return trimmed.length > 0 && trimmed.length <= MAX_APP_NAME ? null : `A name is required and at most ${MAX_APP_NAME} characters.`
}

export function validateKid(kid: string): string | null {
  return KID.test(kid)
    ? null
    : `A key id is 1 to ${MAX_KID} characters: letters, digits, '.', '_' and '-' only, starting with a letter or digit.`
}

export function validateScopeKeys(keys: string[]): string | null {
  if (keys.length > MAX_SCOPE_KEYS) return `An app can have at most ${MAX_SCOPE_KEYS} scope keys.`
  const seen = new Set<string>()
  for (const key of keys) {
    if (!SCOPE_KEY.test(key)) return 'A scope key uses letters, digits and underscores only (max 128 characters).'
    const folded = key.toLowerCase()
    if (seen.has(folded)) return `'${key}' is listed more than once.`
    seen.add(folded)
  }
  return null
}

/** Empty text is "no ceiling". */
export function parseCeiling(text: string): { value: number | null; problem: string | null } {
  const trimmed = text.trim()
  if (trimmed === '') return { value: null, problem: null }
  const value = /^\d+$/.test(trimmed) ? Number(trimmed) : NaN
  if (!Number.isInteger(value) || value < CEILING_MIN || value > CEILING_MAX) {
    return { value: null, problem: `The record ceiling is a whole number from ${CEILING_MIN.toLocaleString('en-US')} to ${CEILING_MAX.toLocaleString('en-US')}, or empty for none.` }
  }
  return { value, problem: null }
}

export function validateKeyInput(kid: string, algorithm: string, pem: string): string | null {
  const kidProblem = validateKid(kid)
  if (kidProblem) return kidProblem
  if (algorithm !== 'RS256' && algorithm !== 'ES256') return 'The algorithm must be RS256 or ES256.'
  if (pem.trim() === '' || pem.length > MAX_PEM) return `A public key in PEM form is required, at most ${MAX_PEM} characters.`
  if (!pem.includes('-----BEGIN PUBLIC KEY-----') || !pem.includes('-----END PUBLIC KEY-----')) {
    return 'The key must be one public key in PEM form (-----BEGIN PUBLIC KEY-----).'
  }
  return null
}

const URL_SHAPE = `A return URL must be an absolute https URL of at most ${MAX_RETURN_URL} characters, without a fragment or credentials.`

export function validateReturnUrl(url: string): string | null {
  // eslint-disable-next-line no-control-regex
  if (url === '' || url.length > MAX_RETURN_URL || url !== url.trim() || /[\u0000-\u001f\u007f-\u009f]/.test(url) || url.includes('#') || !url.includes('//')) {
    return URL_SHAPE
  }
  let parsed: URL
  try {
    parsed = new URL(url)
  } catch {
    return URL_SHAPE
  }
  if (parsed.username !== '' || parsed.password !== '') return URL_SHAPE
  // What is checked must be what a launch can redirect to, so the URL has to be in the form a browser will follow.
  if (parsed.href !== url) {
    return `Write the return URL exactly as \`${parsed.href}\` (lowercase host, \`/\` after the host, no default port).`
  }
  if (parsed.protocol !== 'https:' && !(parsed.protocol === 'http:' && parsed.hostname === 'localhost')) {
    return 'A return URL must use https (http is allowed only for localhost).'
  }
  return null
}

/** Null when the whole list can be saved. Exact duplicates collapse, as on the server. */
export function validateReturnUrls(urls: string[]): string | null {
  const unique: string[] = []
  const seen = new Set<string>()
  for (const url of urls) {
    const problem = validateReturnUrl(url)
    if (problem) return problem
    if (!seen.has(url)) {
      seen.add(url)
      unique.push(url)
      if (unique.length > MAX_RETURN_URLS) return `An app can have at most ${MAX_RETURN_URLS} return URLs.`
    }
  }
  if (new Set(unique.map((u) => u.toLowerCase())).size !== unique.length) return 'Two return URLs differ only by letter case; list one.'
  return null
}

const MOVE_KINDS: [string, string, string][] = [
  ['savedQueries', 'saved query', 'saved queries'],
  ['dashboardTiles', 'dashboard tile', 'dashboard tiles'],
  ['parameterDropdowns', 'parameter dropdown', 'parameter dropdowns'],
  ['dataScopeRules', 'scope rule', 'scope rules'],
]

/** What still uses a data source that cannot move, one line per kind with a non-zero count. */
export function moveCountsSummary(counts: Record<string, number> | undefined): string[] {
  if (!counts) return []
  return MOVE_KINDS.filter(([key]) => (counts[key] ?? 0) > 0).map(([key, one, many]) => `${counts[key]} ${counts[key] === 1 ? one : many}`)
}
