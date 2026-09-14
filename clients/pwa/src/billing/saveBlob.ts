/**
 * Hands a downloaded document to the browser's save dialogue (#302).
 *
 * Kept apart from the screens for the reason the lint rule names: it is not a component, and a
 * component file that also exports a plain function cannot be hot-reloaded. Three screens now share
 * it — the invoice detail screen's document and per-note downloads, and the note screen's download
 * of the note it has just posted (#354).
 *
 * Every billing document is streamed by an authorised endpoint and never given a URL of its own
 * (`CLAUDE.md` section 4, rule 9), so what arrives here is bytes already in hand rather than an
 * address: the object URL below is minted from those bytes, used once, and revoked in the same tick.
 */
export function saveBlob(blob: Blob, fileName: string): void {
  const url = URL.createObjectURL(blob)
  try {
    const link = document.createElement('a')
    link.href = url
    link.download = fileName
    link.click()
  } finally {
    URL.revokeObjectURL(url)
  }
}
