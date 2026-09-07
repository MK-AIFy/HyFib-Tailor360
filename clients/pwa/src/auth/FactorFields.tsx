import { useIntl } from 'react-intl'
import { AUTOCOMPLETE } from '../design-system/components/forms/autocomplete'
import { RadioGroup } from '../design-system/components/forms/RadioGroup'
import { TextField } from '../design-system/components/forms/TextField'
import type { ChallengeFactor, FactorAvailability } from './types'

/**
 * The second-factor question, shared by the challenge screen and the re-authentication dialog.
 *
 * One component rather than two because it is the part that must not diverge: the code field carries
 * `autocomplete="one-time-code"`, which is what makes a device offer the incoming code above the
 * keyboard and what 3.3.8 Accessible Authentication requires — a person must be able to paste it
 * rather than transcribe it. A second implementation is the one that would forget.
 *
 * The choice of factor only appears when there is a choice. An account with an authenticator and no
 * recovery codes left is asked for a code, not asked to choose between one option and one that is
 * not there.
 */
export interface FactorFieldsProps {
  /** What the account can answer with, from the sign-in response or from `GET /me`. */
  readonly factors: FactorAvailability
  readonly factor: ChallengeFactor
  readonly onFactorChange: (factor: ChallengeFactor) => void
  readonly code: string
  readonly onCodeChange: (code: string) => void
  /** The code field's error, in words. */
  readonly error?: string
  /** An explicit id, so an error summary can move focus to the field. */
  readonly codeId?: string
}

export function FactorFields({
  factors,
  factor,
  onFactorChange,
  code,
  onCodeChange,
  error,
  codeId,
}: FactorFieldsProps) {
  const intl = useIntl()
  const canChoose = factors.authenticator && factors.recoveryCode

  return (
    <>
      {canChoose ? (
        <RadioGroup
          label={intl.formatMessage({ id: 'auth.challenge.factor.legend' })}
          name="factor"
          onValueChange={(value) => {
            onFactorChange(value === 'recoveryCode' ? 'recoveryCode' : 'totp')
          }}
          options={[
            {
              value: 'totp',
              label: intl.formatMessage({ id: 'auth.challenge.factor.totp' }),
            },
            {
              value: 'recoveryCode',
              label: intl.formatMessage({ id: 'auth.challenge.factor.recoveryCode' }),
            },
          ]}
          value={factor}
        />
      ) : null}

      <TextField
        // Never remembered by the browser, and never announced as a password: it is a code that is
        // valid for thirty seconds, and `one-time-code` is what puts it above the keyboard.
        autoComplete={AUTOCOMPLETE.oneTimeCode}
        description={intl.formatMessage({ id: `auth.challenge.code.description.${factor}` })}
        // A recovery code carries letters; an authenticator code does not. `numeric` on the second
        // is what makes it typeable one-handed, and would hide the letters on the first.
        inputMode={factor === 'totp' ? 'numeric' : 'text'}
        label={intl.formatMessage({ id: `auth.challenge.code.label.${factor}` })}
        name="code"
        onValueChange={onCodeChange}
        required
        value={code}
        {...(codeId === undefined ? {} : { id: codeId })}
        {...(error === undefined ? {} : { error })}
      />
    </>
  )
}
