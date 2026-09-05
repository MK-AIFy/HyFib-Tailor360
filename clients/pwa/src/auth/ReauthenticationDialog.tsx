import { useCallback, useId, useRef, useState } from 'react'
import { useIntl } from 'react-intl'
import { AUTOCOMPLETE } from '../design-system/components/forms/autocomplete'
import { TextField } from '../design-system/components/forms/TextField'
import { Dialog } from '../components/dialogs/Dialog'
import { Alert } from '../components/primitives/Alert'
import { Button } from '../components/primitives/Button'
import { FAILURE_CAUSE_MESSAGES, failureCauseForStatus } from '../components/states/problemDetails'
import { ApiError } from './apiClient'
import { answerChallenge, signIn } from './authApi'
import { authProblemMessage } from './authProblems'
import { FactorFields } from './FactorFields'
import type { ReauthenticationRequest } from './sessionContext'
import type { ChallengeFactor, CurrentUser, SignInStep } from './types'
import './auth.css'

/**
 * Signing back in without leaving the screen.
 *
 * This is the component the brief calls "a real interceptor, not a redirect to login", and the
 * reason it is a dialog rather than a route is the measurement wizard behind it. A person taking a
 * customer's measurements has four steps of typed numbers on screen and a tape measure in the other
 * hand. Their session times out. A redirect throws all of it away and asks them to start again in
 * front of the customer; a dialog asks for the password, gets out of the way, and the request that
 * was refused is sent again by `apiClient`. The screen never unmounts.
 *
 * ## What it asks for, and what it does not
 *
 * It asks for the password only. The sign-in name is already known — the account is still in memory,
 * which is not a secret and is on the person's own screen anyway — and asking for it again is a
 * second chance to mistype while under pressure. If the account owes a second factor, the dialog
 * moves to a second step in place rather than sending anybody to another screen.
 *
 * ## Why it can be dismissed, and what happens then
 *
 * 2.1.2 No Keyboard Trap is absolute: Escape closes this like every other dialog, and there is no
 * prop that could switch that off. Dismissing it resolves the request as false, which for the
 * interceptor means the original failure is thrown and the screen shows it — with everything the
 * person typed still on it. Nothing is lost by declining; the work is simply not saved yet.
 */
export interface ReauthenticationDialogProps {
  /**
   * Why this is being asked. There is no `open` prop: the dialog is mounted for the life of one
   * request and unmounted when it is answered, so a second question starts from an empty form rather
   * than from whatever was typed into the first one and abandoned.
   */
  readonly request: ReauthenticationRequest
  /** The account being re-authenticated. Its sign-in name is what the password is checked against. */
  readonly user: CurrentUser
  /**
   * Records what the authentication answered and re-reads the account, so that the shell and the
   * route guard both see the new session's facts before the suspended request is replayed.
   */
  readonly onAuthenticated: (step: SignInStep) => Promise<unknown>
  /** True when the person signed back in, false when they abandoned it. */
  readonly onResolve: (signedBackIn: boolean) => void
}

type Phase = 'password' | 'factor'

export function ReauthenticationDialog({
  request,
  user,
  onAuthenticated,
  onResolve,
}: ReauthenticationDialogProps) {
  const intl = useIntl()
  /*
   * The confirming control lives in the dialog's footer, which is outside this component's `<form>`.
   * A `form` attribute joins them, so Enter in a field and a press on the footer button are the same
   * submit. The alternative — a second, visually hidden submit button inside the form — puts a
   * duplicate control with the same accessible name into the accessibility tree, which is a defect
   * a screen-reader user meets before anybody else does.
   */
  const formId = useId()
  /*
   * Where focus lands when the dialog opens.
   *
   * `Dialog` reads `initialFocusRef.current` in an effect, and React attaches refs during commit —
   * before any effect — so a callback ref on the wrapper has already found the control by the time
   * the dialog looks. It is resolved from the DOM rather than forwarded through the field because
   * the forms family deliberately exposes no `ref`: the one contract every control implements is
   * `FieldProps`, and widening it for one dialog would be widening it for all of them.
   */
  const focusRef = useRef<HTMLElement | null>(null)
  const captureFirstControl = useCallback((node: HTMLDivElement | null) => {
    focusRef.current = node?.querySelector('input') ?? null
  }, [])
  const [phase, setPhase] = useState<Phase>('password')
  const [password, setPassword] = useState('')
  const [code, setCode] = useState('')
  const [factor, setFactor] = useState<ChallengeFactor>('totp')
  const [busy, setBusy] = useState(false)
  const [failure, setFailure] = useState<unknown>(null)

  const title = intl.formatMessage({
    id:
      request.reason === 'step-up'
        ? 'auth.reauth.title.stepUp'
        : request.reason === 'revoked'
          ? 'auth.reauth.title.revoked'
          : 'auth.reauth.title.expired',
  })

  const body =
    request.reason === 'step-up'
      ? intl.formatMessage(
          { id: 'auth.reauth.body.stepUp' },
          { action: request.action ?? intl.formatMessage({ id: 'auth.reauth.action' }) },
        )
      : intl.formatMessage({
          id:
            request.reason === 'revoked' ? 'auth.reauth.body.revoked' : 'auth.reauth.body.expired',
        })

  const submitPassword = () => {
    if (busy) {
      return
    }
    setBusy(true)
    setFailure(null)

    void signIn({ identifier: user.userName, password })
      .then(async (result) => {
        setPassword('')
        if (result.step === 'multiFactorRequired') {
          setFactor(result.factors.authenticator ? 'totp' : 'recoveryCode')
          setPhase('factor')
          return
        }
        // `complete`, and also `multiFactorEnrolmentRequired`: the session exists again either way,
        // and the enrolment requirement is the route guard's business rather than this dialog's.
        await onAuthenticated(result.step)
        onResolve(true)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const submitCode = () => {
    if (busy) {
      return
    }
    setBusy(true)
    setFailure(null)

    void answerChallenge({ factor, code, rememberDevice: false })
      .then(async () => {
        setCode('')
        await onAuthenticated('complete')
        onResolve(true)
      })
      .catch((cause: unknown) => {
        setFailure(cause)
      })
      .finally(() => {
        setBusy(false)
      })
  }

  const problem = authProblemMessage(failure)
  const fallback =
    failure instanceof ApiError && problem === undefined
      ? FAILURE_CAUSE_MESSAGES[failureCauseForStatus(failure.status)]
      : undefined

  return (
    <Dialog
      // Typed input inside: a mis-touch on the scrim while holding a garment must not throw it away.
      closeOnScrimPress={false}
      description={<p>{body}</p>}
      footer={
        <>
          <Button
            onClick={() => {
              onResolve(false)
            }}
            variant="secondary"
          >
            {intl.formatMessage({ id: 'auth.reauth.abandon' })}
          </Button>
          <Button busy={busy} form={formId} type="submit" variant="primary">
            {intl.formatMessage({
              id: request.reason === 'step-up' ? 'auth.reauth.submit.stepUp' : 'auth.reauth.submit',
            })}
          </Button>
        </>
      }
      initialFocusRef={focusRef}
      onClose={() => {
        onResolve(false)
      }}
      open
      title={title}
    >
      <form
        className="auth-form"
        id={formId}
        onSubmit={(event) => {
          event.preventDefault()
          if (phase === 'password') {
            submitPassword()
          } else {
            submitCode()
          }
        }}
      >
        <p className="auth-identity">
          {intl.formatMessage({ id: 'auth.reauth.as' }, { name: user.displayName })}
        </p>

        {problem === undefined && fallback === undefined ? null : (
          <Alert live="assertive" tone="danger">
            {intl.formatMessage(
              { id: problem?.id ?? fallback ?? 'states.problem.unknown' },
              problem?.values,
            )}
          </Alert>
        )}

        <div ref={captureFirstControl}>
          {phase === 'password' ? (
            <TextField
              autoComplete={AUTOCOMPLETE.currentPassword}
              description={intl.formatMessage({ id: 'auth.signIn.password.description' })}
              enterKeyHint="go"
              label={intl.formatMessage({ id: 'auth.reauth.password.label' })}
              name="password"
              onValueChange={setPassword}
              required
              type="password"
              value={password}
            />
          ) : (
            <>
              <p className="auth-hint">{intl.formatMessage({ id: 'auth.reauth.code.body' })}</p>
              <FactorFields
                code={code}
                factor={factor}
                factors={user.security.factors}
                onCodeChange={setCode}
                onFactorChange={setFactor}
              />
            </>
          )}
        </div>
      </form>
    </Dialog>
  )
}
