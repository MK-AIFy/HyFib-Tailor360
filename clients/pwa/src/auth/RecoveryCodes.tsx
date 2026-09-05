import { useState } from 'react'
import { useIntl } from 'react-intl'
import { Checkbox } from '../design-system/components/forms/Checkbox'
import { Alert } from '../components/primitives/Alert'
import { Button } from '../components/primitives/Button'
import { CopyButton } from './CopyButton'
import './auth.css'

/**
 * The recovery codes, shown once.
 *
 * Everything here follows from that "once". The server hashes them and keeps no readable copy, so
 * this render is the only moment they exist in a form a person can read — which makes the screen
 * responsible for three things it would otherwise be free to leave out:
 *
 *  - **Saying so, plainly and before the codes.** Somebody who scrolls past this and closes the tab
 *    has lost the only way back into their account that does not need an administrator.
 *  - **Two ways to keep them.** Printing is the one that survives a lost phone, which is the case
 *    these exist for; copying is for a password manager. Neither is assumed.
 *  - **An acknowledgement that does not lie.** The checkbox is not a legal formality — it is the
 *    thing that stops "Finish" from being the button somebody presses to make a screen go away.
 *
 * The codes are rendered into a list and nowhere else. They are not put in a query string, not in a
 * `download` attribute, not in `localStorage`, and not in any log or telemetry event.
 */
export interface RecoveryCodesProps {
  readonly codes: readonly string[]
  /** Called when the person has confirmed they kept them. */
  readonly onDone: () => void
  /** The heading level, which the screen around this knows and this component does not. */
  readonly headingLevel?: 2 | 3
}

export function RecoveryCodes({ codes, onDone, headingLevel = 2 }: RecoveryCodesProps) {
  const intl = useIntl()
  const [acknowledged, setAcknowledged] = useState(false)
  const [error, setError] = useState<string | undefined>(undefined)
  const Heading = headingLevel === 3 ? 'h3' : 'h2'

  const print = () => {
    // jsdom, and a webview with printing disabled, both throw rather than doing nothing. Printing
    // is the alternative to copying, not the only way out, so a refusal is not worth an error state.
    try {
      window.print()
    } catch {
      /* Nothing to do: the codes are on the screen and can still be copied or written down. */
    }
  }

  return (
    <section className="recovery-sheet">
      <Heading>{intl.formatMessage({ id: 'auth.codes.title' })}</Heading>
      <p>{intl.formatMessage({ id: 'auth.codes.intro' })}</p>

      <Alert tone="warning">{intl.formatMessage({ id: 'auth.codes.once' })}</Alert>

      <ol
        aria-label={intl.formatMessage({ id: 'auth.codes.list.label' })}
        className="recovery-sheet__codes"
      >
        {codes.map((code) => (
          <li className="recovery-sheet__code" key={code}>
            {code}
          </li>
        ))}
      </ol>

      <div className="recovery-sheet__actions">
        <Button iconName="receipt" onClick={print} variant="secondary">
          {intl.formatMessage({ id: 'auth.codes.print' })}
        </Button>
        <CopyButton
          confirmation={intl.formatMessage({ id: 'auth.codes.copied' })}
          failure={intl.formatMessage({ id: 'auth.enrol.copyFailed' })}
          label={intl.formatMessage({ id: 'auth.codes.copy' })}
          value={codes.join('\n')}
        />
      </div>

      <Checkbox
        label={intl.formatMessage({ id: 'auth.codes.acknowledge' })}
        name="recovery-codes-kept"
        onValueChange={(next) => {
          setAcknowledged(next)
          setError(undefined)
        }}
        required
        value={acknowledged}
        {...(error === undefined ? {} : { error })}
      />

      <Button
        onClick={() => {
          if (!acknowledged) {
            setError(intl.formatMessage({ id: 'auth.codes.acknowledge.missing' }))
            return
          }
          onDone()
        }}
        size="primary"
        variant="primary"
      >
        {intl.formatMessage({ id: 'auth.codes.continue' })}
      </Button>
    </section>
  )
}
