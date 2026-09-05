/**
 * Which install path this device actually has.
 *
 * Installing a progressive web application is the one place where the honest answer really is "it
 * depends what you are holding". Chromium fires `beforeinstallprompt` and a page can offer a button;
 * Safari fires nothing and the only route is a menu the page cannot open, cannot name a control
 * inside, and cannot detect the outcome of; a non-Safari browser on iOS cannot install at all,
 * whatever it looks like. So the Install screen has to say different things to different people, and
 * this module is where the sniffing is contained.
 *
 * User-agent sniffing is otherwise forbidden in this client — the layouts are chosen by measuring a
 * container, never by asking what the device is (docs/nfr/support-matrix.md section 5). This is the
 * documented exception, and it is narrow on purpose: **nothing here changes a layout, a capability
 * or a code path.** It changes which set of instructions a person reads. Getting it wrong shows
 * somebody the wrong paragraph; it never breaks a screen, and every branch still names the manual
 * route through the browser menu so a person whose device is misread is not stuck.
 *
 * The functions are pure and take the probe as an argument, so the whole matrix is testable without
 * a browser and without stubbing globals.
 */

export const INSTALL_PLATFORMS = [
  /** Android with a Chromium engine: `beforeinstallprompt` fires and a button can be offered. */
  'android',
  /** iOS or iPadOS in Safari: Share → Add to Home Screen, and no event of any kind. */
  'ios-safari',
  /** iOS or iPadOS in something that is not Safari: installing is impossible, not merely different. */
  'ios-other',
  /** A desktop Chromium: the address-bar install control, and usually `beforeinstallprompt` too. */
  'desktop',
  /** Everything else, including desktop Firefox and Safari: works in a tab, does not install. */
  'other',
] as const

export type InstallPlatform = (typeof INSTALL_PLATFORMS)[number]

/**
 * What the detection reads. Two values, both cheap, and `maxTouchPoints` is not optional: an iPad on
 * iPadOS 13 and later reports itself as a Macintosh, and the touch-point count is the only thing in
 * the platform that still tells the two apart.
 */
export interface PlatformProbe {
  readonly userAgent: string
  readonly maxTouchPoints: number
}

/** Browsers on iOS that are Safari's engine wearing another badge. None of them can install. */
const IOS_NON_SAFARI = /CriOS|FxiOS|EdgiOS|OPiOS|DuckDuckGo|YaBrowser|Brave/

/** The Android engines that fire `beforeinstallprompt`. */
const ANDROID_INSTALLABLE = /Chrome\/|SamsungBrowser\//

/** An Android WebView. It carries `Chrome/` but has no menu and cannot install. */
const ANDROID_WEBVIEW = /;\s*wv\)/

/** The desktop engines with an install control. */
const DESKTOP_INSTALLABLE = /Chrome\/|Chromium\/|Edg\//

export function detectInstallPlatform(probe: PlatformProbe): InstallPlatform {
  const { userAgent, maxTouchPoints } = probe

  /*
   * iPadOS first, because it lies. Since iPadOS 13 the default user agent is a Macintosh one, so a
   * counter iPad would otherwise be told to look for an install control in an address bar that does
   * not exist. A Mac reports zero touch points; an iPad reports five.
   */
  const isIos =
    /iPad|iPhone|iPod/.test(userAgent) || (/Macintosh/.test(userAgent) && maxTouchPoints > 1)

  if (isIos) {
    return IOS_NON_SAFARI.test(userAgent) || !/Safari/.test(userAgent) ? 'ios-other' : 'ios-safari'
  }

  if (/Android/.test(userAgent)) {
    if (ANDROID_WEBVIEW.test(userAgent) || !ANDROID_INSTALLABLE.test(userAgent)) {
      return 'other'
    }
    return 'android'
  }

  // Mobile from here on is a phone that is neither Android nor iOS, which has no documented path.
  if (/Mobile|Tablet/.test(userAgent)) {
    return 'other'
  }

  return DESKTOP_INSTALLABLE.test(userAgent) ? 'desktop' : 'other'
}

/**
 * The display modes that mean "this is the installed application" rather than a browser tab.
 *
 * `minimal-ui` and `fullscreen` are here because `display_override` in the manifest lists them as
 * acceptable fallbacks: a device that refuses `standalone` still installed the application, and
 * telling that person to install it again would be wrong.
 */
export const INSTALLED_DISPLAY_MODES = [
  'standalone',
  'minimal-ui',
  'fullscreen',
  'window-controls-overlay',
] as const

/**
 * Whether the page is running as an installed application.
 *
 * Two mechanisms because one of them is Safari's. Every other engine answers the `display-mode`
 * media query; iOS answers `navigator.standalone`, a non-standard boolean that has never been
 * replaced. Both are wrapped, because `matchMedia` with an unknown feature throws in some engines
 * and a locked-down profile can make either unavailable — and a screen that cannot tell should say
 * "browser tab" and show the instructions, which is the harmless answer.
 */
export function isInstalledDisplayMode(view: Window = window): boolean {
  const legacy = (view.navigator as Navigator & { standalone?: boolean }).standalone
  if (legacy === true) {
    return true
  }

  for (const mode of INSTALLED_DISPLAY_MODES) {
    try {
      if (view.matchMedia(`(display-mode: ${mode})`).matches) {
        return true
      }
    } catch {
      // An engine that does not understand the feature has not installed anything through it.
    }
  }

  return false
}

/** Reads the probe from the running browser. Separated so the detection itself stays pure. */
export function readPlatformProbe(view: Window = window): PlatformProbe {
  return {
    userAgent: view.navigator.userAgent,
    maxTouchPoints: view.navigator.maxTouchPoints,
  }
}
