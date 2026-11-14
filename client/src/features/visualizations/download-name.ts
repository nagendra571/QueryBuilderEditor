const MAX_LENGTH = 100
// Characters Windows, macOS or Linux refuse in a file name, plus control characters.
// eslint-disable-next-line no-control-regex
const FORBIDDEN = /[<>:"/\\|?*\u0000-\u001f\u007f]/g
const RESERVED = /^(con|prn|aux|nul|com[1-9]|lpt[1-9])$/i

/** The file name a download is saved under: the chart's name cleaned the same way the server cleans it. */
export function downloadFileName(name: string | null | undefined, extension: string, fallback = 'chart'): string {
  let cleaned = (name ?? '').replace(FORBIDDEN, ' ').split(' ').filter(Boolean).join(' ')
  cleaned = cleaned.replace(/^[. ]+|[. ]+$/g, '')
  if (cleaned.length > MAX_LENGTH) {
    // Never keep half of a surrogate pair (an emoji, say).
    const end = /[\uD800-\uDBFF]/.test(cleaned[MAX_LENGTH - 1]) ? MAX_LENGTH - 1 : MAX_LENGTH
    cleaned = cleaned.slice(0, end).replace(/[. ]+$/, '')
  }
  if (cleaned.length === 0) cleaned = fallback
  else if (RESERVED.test(cleaned.split('.')[0].trimEnd())) cleaned = `_${cleaned}`
  return `${cleaned}.${extension}`
}
