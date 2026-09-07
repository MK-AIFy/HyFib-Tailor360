/**
 * Install and About messages — the two screens that talk about the application itself.
 *
 * Follows the family rules documented at the top of `messages/shell.ts`: one file, both languages,
 * ICU placeholders for anything that varies, and never a sentence built by concatenation.
 *
 * Two rules from docs/nfr/accessibility-localisation.md shape the wording here:
 *
 *  - Section 8.2: instructions are steps a person can carry out, naming the control they will see on
 *    the device — "the Share button in the Safari toolbar", not "the share icon". The words on the
 *    button are the operating system's, so they are quoted rather than described.
 *  - Section 6: nothing claims a capability the build does not have. Installing today changes where
 *    the application opens from; it does not make the shop work offline, because there is no service
 *    worker yet (#51). `install.notOffline.*` says so on the screen rather than in a release note.
 */
export const installEn = {
  // ---------------------------------------------------------------------------------------------
  // The Install screen
  // ---------------------------------------------------------------------------------------------
  'install.title': 'Install Tailor360',
  'install.intro':
    'Installing puts Tailor360 on the home screen of this device, so it opens like any other application: full screen, without the browser address bar taking a row of the display.',

  'install.installed.title': 'Already installed on this device',
  'install.installed.body': 'You are using the installed application. Nothing more is needed here.',

  'install.benefits.title': 'What installing changes',
  'install.benefits.homeScreen': 'An icon on the home screen, opened without typing an address.',
  'install.benefits.fullScreen':
    'The full height of the display, which is one more job on screen at the counter.',
  'install.benefits.separateWindow':
    'Its own window and its own task, so the application is not closed with a browser tab by mistake.',

  'install.notOffline.title': 'Installing does not make the shop work without a connection',
  'install.notOffline.body':
    'The installed application still needs the network for every action. Working offline arrives in a later release; until then an action attempted without a connection is refused and told to you, never saved quietly.',

  'install.android.title': 'Install on an Android phone',
  'install.android.body': 'Chrome and Samsung Internet install this application directly.',
  'install.android.action': 'Install Tailor360',
  'install.android.manual':
    'If the button is not offered, open the browser menu and choose Install app, or Add to Home screen.',
  'install.android.dismissed':
    'Installation was not completed. You can start it again whenever you like.',

  'install.ios.title': 'Install on an iPhone or iPad',
  'install.ios.body':
    'Safari installs from the Share menu. There is no install button a page is allowed to offer.',
  'install.ios.step1':
    'Tap the Share button in the Safari toolbar — the square with an arrow pointing up.',
  'install.ios.step2': 'Scroll the list of actions and tap Add to Home Screen.',
  'install.ios.step3': 'Tap Add. The Tailor360 icon appears on the home screen.',

  'install.iosOtherBrowser.title': 'Open this page in Safari to install it',
  'install.iosOtherBrowser.body':
    'On an iPhone or iPad only Safari can install an application, whichever browser you prefer for everything else. Open the same address in Safari and this screen will show the steps.',

  'install.desktop.title': 'Install on a computer',
  'install.desktop.body':
    'Chrome and Edge show an install control at the right-hand end of the address bar. You can also open the browser menu and choose Install Tailor360.',

  'install.other.title': 'This browser cannot install the application',
  'install.other.body':
    'Everything works in a browser tab, and nothing is missing from it. To install, open Tailor360 in Chrome or Edge on a computer or an Android phone, or in Safari on an iPhone or iPad.',

  'install.steps.label': 'Steps to install',
  'install.about.link': 'See the version and environment on the About screen',

  // ---------------------------------------------------------------------------------------------
  // The About screen
  // ---------------------------------------------------------------------------------------------
  'about.title': 'About this build',
  'about.body':
    'What is running on this device. Quote it when you report a problem, so the shop administrator knows which build you were using.',
  'about.details.label': 'Build details',
  'about.version.label': 'Version',
  'about.build.label': 'Build',
  'about.api.label': 'API',
  'about.schema.label': 'Data version',
  'about.environment.label': 'Environment',
  'about.displayMode.label': 'Opened as',
  'about.displayMode.installed': 'Installed application',
  'about.displayMode.browser': 'Browser tab',
  'about.loading.what': 'the build information',
  'about.unavailable.title': 'The build information could not be read',
  'about.unavailable.body':
    'The application could not reach the server to ask which build it is running. Try again, or reload the page once the connection is back.',
  'about.install.link': 'How to install Tailor360 on this device',
  'about.support.title': 'Reporting a problem',
  'about.support.body':
    'Tell your shop administrator the version and the build above, what you were doing, and roughly when. Do not put a customer name, phone number or measurement in the message.',
} as const

export const installTa: Record<keyof typeof installEn, string> = {
  'install.title': 'Tailor360 ஐ நிறுவுங்கள்',
  // not translated — awaiting native-speaker review
  'install.intro':
    'Installing puts Tailor360 on the home screen of this device, so it opens like any other application: full screen, without the browser address bar taking a row of the display.',

  'install.installed.title': 'இந்தச் சாதனத்தில் ஏற்கனவே நிறுவப்பட்டுள்ளது',
  // not translated — awaiting native-speaker review
  'install.installed.body': 'You are using the installed application. Nothing more is needed here.',

  // not translated — awaiting native-speaker review
  'install.benefits.title': 'What installing changes',
  // not translated — awaiting native-speaker review
  'install.benefits.homeScreen': 'An icon on the home screen, opened without typing an address.',
  // not translated — awaiting native-speaker review
  'install.benefits.fullScreen':
    'The full height of the display, which is one more job on screen at the counter.',
  // not translated — awaiting native-speaker review
  'install.benefits.separateWindow':
    'Its own window and its own task, so the application is not closed with a browser tab by mistake.',

  // not translated — awaiting native-speaker review
  'install.notOffline.title': 'Installing does not make the shop work without a connection',
  // not translated — awaiting native-speaker review
  'install.notOffline.body':
    'The installed application still needs the network for every action. Working offline arrives in a later release; until then an action attempted without a connection is refused and told to you, never saved quietly.',

  'install.android.title': 'Android தொலைபேசியில் நிறுவுதல்',
  // not translated — awaiting native-speaker review
  'install.android.body': 'Chrome and Samsung Internet install this application directly.',
  'install.android.action': 'Tailor360 ஐ நிறுவுங்கள்',
  // not translated — awaiting native-speaker review
  'install.android.manual':
    'If the button is not offered, open the browser menu and choose Install app, or Add to Home screen.',
  // not translated — awaiting native-speaker review
  'install.android.dismissed':
    'Installation was not completed. You can start it again whenever you like.',

  'install.ios.title': 'iPhone அல்லது iPad இல் நிறுவுதல்',
  // not translated — awaiting native-speaker review
  'install.ios.body':
    'Safari installs from the Share menu. There is no install button a page is allowed to offer.',
  // not translated — awaiting native-speaker review
  'install.ios.step1':
    'Tap the Share button in the Safari toolbar — the square with an arrow pointing up.',
  // not translated — awaiting native-speaker review
  'install.ios.step2': 'Scroll the list of actions and tap Add to Home Screen.',
  // not translated — awaiting native-speaker review
  'install.ios.step3': 'Tap Add. The Tailor360 icon appears on the home screen.',

  // not translated — awaiting native-speaker review
  'install.iosOtherBrowser.title': 'Open this page in Safari to install it',
  // not translated — awaiting native-speaker review
  'install.iosOtherBrowser.body':
    'On an iPhone or iPad only Safari can install an application, whichever browser you prefer for everything else. Open the same address in Safari and this screen will show the steps.',

  'install.desktop.title': 'கணினியில் நிறுவுதல்',
  // not translated — awaiting native-speaker review
  'install.desktop.body':
    'Chrome and Edge show an install control at the right-hand end of the address bar. You can also open the browser menu and choose Install Tailor360.',

  // not translated — awaiting native-speaker review
  'install.other.title': 'This browser cannot install the application',
  // not translated — awaiting native-speaker review
  'install.other.body':
    'Everything works in a browser tab, and nothing is missing from it. To install, open Tailor360 in Chrome or Edge on a computer or an Android phone, or in Safari on an iPhone or iPad.',

  // not translated — awaiting native-speaker review
  'install.steps.label': 'Steps to install',
  // not translated — awaiting native-speaker review
  'install.about.link': 'See the version and environment on the About screen',

  // not translated — awaiting native-speaker review
  'about.title': 'About this build',
  // not translated — awaiting native-speaker review
  'about.body':
    'What is running on this device. Quote it when you report a problem, so the shop administrator knows which build you were using.',
  // not translated — awaiting native-speaker review
  'about.details.label': 'Build details',
  'about.version.label': 'பதிப்பு',
  'about.build.label': 'உருவாக்கம்',
  'about.api.label': 'API',
  'about.schema.label': 'தரவுப் பதிப்பு',
  'about.environment.label': 'சூழல்',
  // not translated — awaiting native-speaker review
  'about.displayMode.label': 'Opened as',
  // not translated — awaiting native-speaker review
  'about.displayMode.installed': 'Installed application',
  // not translated — awaiting native-speaker review
  'about.displayMode.browser': 'Browser tab',
  // not translated — awaiting native-speaker review
  'about.loading.what': 'the build information',
  // not translated — awaiting native-speaker review
  'about.unavailable.title': 'The build information could not be read',
  // not translated — awaiting native-speaker review
  'about.unavailable.body':
    'The application could not reach the server to ask which build it is running. Try again, or reload the page once the connection is back.',
  // not translated — awaiting native-speaker review
  'about.install.link': 'How to install Tailor360 on this device',
  // not translated — awaiting native-speaker review
  'about.support.title': 'Reporting a problem',
  // not translated — awaiting native-speaker review
  'about.support.body':
    'Tell your shop administrator the version and the build above, what you were doing, and roughly when. Do not put a customer name, phone number or measurement in the message.',
}
