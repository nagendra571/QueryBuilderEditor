export function downloadBlob(blob: Blob, fileName: string) {
  const url = URL.createObjectURL(blob)
  const link = document.createElement('a')
  link.href = url
  link.download = fileName
  document.body.appendChild(link)
  link.click()
  document.body.removeChild(link)
  // Revoking at once can cancel the download in some browsers; a moment later the file is already handed over.
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
