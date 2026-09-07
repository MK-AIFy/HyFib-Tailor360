/**
 * Shell messages — the application chrome that exists on every screen.
 *
 * ## How a message family works
 *
 * One file per family, holding **both** languages. That is deliberate: the type gate below means an
 * English key with no Tamil counterpart is a compile error, so a contributor who has to edit two
 * files edits the two most contended files in the repository. Keeping the pair together means each
 * family is owned by exactly one change and two families never collide.
 *
 * ## Rules for every family file
 *
 *  - Keys are dot-separated and start with the family name.
 *  - ICU MessageFormat placeholders carry anything that varies. Never build a sentence by
 *    concatenation: word order differs in Tamil.
 *  - A string that is not yet translated carries the English text and a
 *    `// not translated — awaiting native-speaker review` comment on the line above, so the build
 *    report that counts translated identifiers for the 95% enablement gate can find it.
 */
export const shellEn = {
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
  'footer.version': 'Version {version}',
  'footer.versionWithBuild': 'Version {version} · build {buildHash}',
  'footer.versionUnavailable': 'Version information is not available.',
} as const

export const shellTa: Record<keyof typeof shellEn, string> = {
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
  'footer.version': 'பதிப்பு {version}',
  'footer.versionWithBuild': 'பதிப்பு {version} · உருவாக்கம் {buildHash}',
  'footer.versionUnavailable': 'பதிப்புத் தகவல் கிடைக்கவில்லை.',
}
