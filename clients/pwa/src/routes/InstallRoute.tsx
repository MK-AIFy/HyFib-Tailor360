import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { Alert } from '../components/primitives/Alert'
import { Button } from '../components/primitives/Button'
import { Card } from '../components/primitives/Card'
import { detectInstallPlatform, readPlatformProbe } from './installPlatform'
import type { InstallPlatform } from './installPlatform'
import { useInstallPrompt } from './useInstallPrompt'
import type { InstallPromptState } from './useInstallPrompt'
import './install.css'

export interface InstallInstructionsProps {
  /** Which set of instructions to show. */
  readonly platform: InstallPlatform
  /** The browser's install offer, and whether the application is already installed. */
  readonly prompt: InstallPromptState
}

/** The message id of the heading for each platform's instructions. */
const PLATFORM_TITLES: Record<InstallPlatform, string> = {
  android: 'install.android.title',
  'ios-safari': 'install.ios.title',
  'ios-other': 'install.iosOtherBrowser.title',
  desktop: 'install.desktop.title',
  other: 'install.other.title',
}

const PLATFORM_BODIES: Record<InstallPlatform, string> = {
  android: 'install.android.body',
  'ios-safari': 'install.ios.body',
  'ios-other': 'install.iosOtherBrowser.body',
  desktop: 'install.desktop.body',
  other: 'install.other.body',
}

/**
 * The per-platform half of the Install screen, separated from the route so that a story and a test
 * can render all five branches without stubbing a user agent or dispatching a browser event.
 */
export function InstallInstructions({ platform, prompt }: InstallInstructionsProps) {
  const intl = useIntl()
  const offersButton = platform === 'android' || platform === 'desktop'

  return (
    <Card headingLevel={2} title={intl.formatMessage({ id: PLATFORM_TITLES[platform] })}>
      <p>
        <FormattedMessage id={PLATFORM_BODIES[platform]} />
      </p>

      {platform === 'ios-safari' ? (
        <ol
          aria-label={intl.formatMessage({ id: 'install.steps.label' })}
          className="install__steps"
        >
          <li>
            <FormattedMessage id="install.ios.step1" />
          </li>
          <li>
            <FormattedMessage id="install.ios.step2" />
          </li>
          <li>
            <FormattedMessage id="install.ios.step3" />
          </li>
        </ol>
      ) : null}

      {offersButton && prompt.available ? (
        <p>
          <Button
            busy={prompt.busy}
            iconName="plus"
            onClick={prompt.promptToInstall}
            size="primary"
            variant="primary"
          >
            <FormattedMessage id="install.android.action" />
          </Button>
        </p>
      ) : null}

      {/*
       * The manual route is shown whether or not the button is, and never as a fallback that
       * appears only when something failed. `beforeinstallprompt` does not fire a second time, it
       * does not fire at all in some Chromium builds and on some engines that can still install
       * from their menu, and a screen that hides the menu path until the button is missing leaves
       * exactly those people with nothing.
       */}
      {offersButton ? (
        <p className="install__manual">
          <FormattedMessage id="install.android.manual" />
        </p>
      ) : null}

      {prompt.outcome === 'dismissed' ? (
        <Alert live="polite" tone="info">
          <FormattedMessage id="install.android.dismissed" />
        </Alert>
      ) : null}
    </Card>
  )
}

/**
 * The Install screen.
 *
 * ## Why a page at all, when browsers have their own install affordances
 *
 * Because two of the four device classes in docs/nfr/support-matrix.md have no affordance a page can
 * trigger, and one of them cannot install in the browser the person is holding. Safari on iOS
 * installs only from the Share menu; a non-Safari browser on iOS cannot install at all, which
 * section 6 of the support matrix records as an accepted limitation whose mitigation is "the Install
 * page detects iOS plus a non-Safari browser and tells the user to open the same URL in Safari".
 * That sentence is this screen.
 *
 * ## What it does not promise
 *
 * Installing changes where the application opens from. It does not make the shop work without a
 * connection, because this issue deliberately registers no service worker — that is #51, together
 * with the bounded offline queue. The screen says so in a standing alert rather than leaving people
 * to discover it at the counter, because a Delivery Staff member who believes the doorstep
 * confirmation will be saved offline is a data problem, not a disappointment.
 *
 * ## What the screen assumes about the person reading it
 *
 * That they are standing up, holding a phone, and have been sent here by somebody else. So the
 * instructions name the control they will actually see — "the Share button in the Safari toolbar" —
 * rather than describing an icon, and every branch ends with the browser-menu route so nobody is
 * left with only a button that did not appear.
 */
export function InstallRoute() {
  const intl = useIntl()
  const prompt = useInstallPrompt()
  // Read once. The device does not change under the person while they read the page, and a probe on
  // every render would be a user-agent string parsed dozens of times for no new answer.
  const platform = detectInstallPlatform(readPlatformProbe())

  return (
    <section className="page install">
      <h1>
        <FormattedMessage id="install.title" />
      </h1>
      <p>
        <FormattedMessage id="install.intro" />
      </p>

      {prompt.installed ? (
        <Alert title={intl.formatMessage({ id: 'install.installed.title' })} tone="success">
          <FormattedMessage id="install.installed.body" />
        </Alert>
      ) : (
        <InstallInstructions platform={platform} prompt={prompt} />
      )}

      <Card headingLevel={2} title={intl.formatMessage({ id: 'install.benefits.title' })}>
        <ul className="install__benefits">
          <li>
            <FormattedMessage id="install.benefits.homeScreen" />
          </li>
          <li>
            <FormattedMessage id="install.benefits.fullScreen" />
          </li>
          <li>
            <FormattedMessage id="install.benefits.separateWindow" />
          </li>
        </ul>
      </Card>

      <Alert title={intl.formatMessage({ id: 'install.notOffline.title' })} tone="warning">
        <FormattedMessage id="install.notOffline.body" />
      </Alert>

      <p>
        <Link to="/about">
          <FormattedMessage id="install.about.link" />
        </Link>
      </p>
    </section>
  )
}
