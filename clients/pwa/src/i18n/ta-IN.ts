import type { MessageCatalogue } from './en-IN'

/**
 * Tamil (India) message catalogue.
 *
 * IMPORTANT: this catalogue awaits native-speaker review. Entries marked "not translated" carry the
 * English string on purpose, so that the interface stays usable rather than showing a key; they are
 * not a licence to ship Tamil as a launch language. Per the accessibility and localisation policy
 * (implementation plan #19 and #52) Tamil becomes a selectable UI language only once the catalogue is
 * at least 95% translated and the measurement and production glossary from #17 has been applied.
 *
 * Typed as MessageCatalogue, so adding a key to en-IN without adding it here fails the type check.
 */
export const taIN: MessageCatalogue = {
  'app.name': 'HyFib Tailor360',
  'app.shopName': 'HyFib Tailor360',
  'app.skipToContent': 'உள்ளடக்கத்திற்குச் செல்',
  'nav.label': 'முதன்மை வழிசெலுத்தல்',
  'nav.home': 'முகப்பு',
  'banner.training.title': 'பயிற்சி — உண்மையான தரவு அல்ல',
  'banner.training.detail':
    'நீங்கள் {environment} சூழலில் பணிபுரிகிறீர்கள். இங்கு நீங்கள் செய்வது வாடிக்கையாளரை அடையாது.',
  'home.title': 'முகப்பு',
  // not translated — awaiting native-speaker review
  'home.body':
    'The shop workspace is being built. Customers, orders, production, inventory and billing arrive in the coming milestones.',
  'notFound.title': 'பக்கம் கிடைக்கவில்லை',
  // not translated — awaiting native-speaker review
  'notFound.body':
    'The address you opened does not exist. Check the link, or go back to the start.',
  'notFound.back': 'முகப்புப் பக்கத்திற்குச் செல்',
  'error.title': 'ஏதோ தவறு நடந்துவிட்டது',
  // not translated — awaiting native-speaker review
  'error.body':
    'Reload the page. If it happens again, tell your shop administrator what you were doing.',
  'footer.version': 'பதிப்பு {version} · உருவாக்கம் {buildHash}',
  'footer.versionUnavailable': 'பதிப்புத் தகவல் கிடைக்கவில்லை.',
}
