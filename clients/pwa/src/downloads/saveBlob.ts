/**
 * Hands a downloaded document to the browser's save dialogue (#302).
 *
 * Kept apart from the screens for the reason the lint rule names: it is not a component, and a
 * component file that also exports a plain function cannot be hot-reloaded.
 *
 * It lived under `billing/` until the customer export needed it too (#619). Nothing about it was
 * ever billing's — it takes bytes and a name — so it moved here rather than being imported across a
 * module boundary to reach a utility that belongs to neither.
 *
 * Every document this application hands over — a billing PDF, a subject-access export — is streamed
 * by an endpoint that re-authorises and audits the request, and is never given a URL of its own
 * (`CLAUDE.md` section 4, rule 9). So what arrives here is bytes already in hand rather than an
 * address: the object URL below is minted from those bytes, used once, and revoked in the same tick.
 * That is also why the file name is a parameter — the server chooses it, and for an export it is
 * deliberately the export's identifier rather than anything about the person.
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
