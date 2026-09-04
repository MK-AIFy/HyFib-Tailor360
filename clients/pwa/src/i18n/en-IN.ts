/**
 * English (India) message catalogue — the source of truth for every user-visible string.
 *
 * Keys are dot-separated and grouped by the screen or component that owns them. ICU MessageFormat
 * placeholders are used for values that vary; never build a sentence by concatenation, because word
 * order differs in Tamil.
 */
export const enIN = {
  'app.name': 'HyFib Tailor360',
  'app.shopName': 'HyFib Tailor360',
  'app.skipToContent': 'Skip to main content',
  'nav.label': 'Main navigation',
  'nav.home': 'Home',
  'banner.training.title': 'TRAINING — not real data',
  'banner.training.detail':
    'You are working in the {environment} environment. Nothing you do here reaches a customer.',
  'home.title': 'Home',
  'home.body':
    'The shop workspace is being built. Customers, orders, production, inventory and billing arrive in the coming milestones.',
  'notFound.title': 'Page not found',
  'notFound.body':
    'The address you opened does not exist. Check the link, or go back to the start.',
  'notFound.back': 'Go to the home page',
  'error.title': 'Something went wrong',
  'error.body':
    'Reload the page. If it happens again, tell your shop administrator what you were doing.',
  'footer.version': 'Version {version} · build {buildHash}',
  'footer.versionUnavailable': 'Version information is not available.',
} as const

/** Every key the application may ask for. A missing key in another catalogue is a type error. */
export type MessageKey = keyof typeof enIN

/** The shape every locale catalogue must satisfy. */
export type MessageCatalogue = Record<MessageKey, string>
