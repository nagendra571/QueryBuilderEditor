// The async clipboard API is unavailable on plain http, so copy through the copy event.
export function copyText(text: string): boolean {
  const onCopy = (event: ClipboardEvent) => {
    event.clipboardData?.setData('text/plain', text)
    event.preventDefault()
  }
  document.addEventListener('copy', onCopy)
  try {
    return document.execCommand('copy')
  } catch {
    return false
  } finally {
    document.removeEventListener('copy', onCopy)
  }
}
