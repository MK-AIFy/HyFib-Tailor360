import { messagesTa } from './messages'
import type { MessageCatalogue } from './en-IN'

/**
 * Tamil (India).
 *
 * IMPORTANT: this catalogue awaits native-speaker review. Entries marked "not translated" in the
 * family files carry the English string on purpose, so the interface stays usable rather than
 * showing a key; they are not a licence to ship Tamil as a launch language. Tamil becomes a
 * selectable interface language only once the eight criteria of the enablement gate in
 * docs/nfr/accessibility-localisation.md section 10.3 pass — at least 95% of identifiers
 * translated, 100% of the shop-floor journeys, and the glossary signed off by a native speaker.
 *
 * Typed as MessageCatalogue, so adding a key to en-IN without adding it here fails the type check.
 */
export const taIN: MessageCatalogue = messagesTa
