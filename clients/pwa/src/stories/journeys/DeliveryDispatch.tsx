import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { useShellStatus } from '../../components/layout/useShellStatus'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { Timeline } from '../../components/primitives/Timeline'
import { useDemoText } from '../../components/primitives/demoText'
import { Select } from '../../design-system/components/forms/Select'
import { Switch } from '../../design-system/components/forms/Switch'
import { TextField } from '../../design-system/components/forms/TextField'
import { getFormatters } from '../../i18n/formatters'
import { DATES, NOW, STAFF } from '../fixtures/branch'
import { DELIVERY_FAILURE_REASONS, DELIVERY_QUEUE, DOORSTEP, INVOICE } from '../fixtures/billing'
import type { DeliveryEntry } from '../fixtures/billing'

/**
 * `A11Y-RJ-07` — Delivery Staff: the branch queue through to a doorstep.
 *
 * Walked as `A11Y-PZ-03` (the dispatch gate) in docs/nfr/a11y-checklist.md, which section 6.8 says
 * is not to be walked twice. All nine steps of that priority-zero record are reachable here.
 *
 * ## The whole screen exists for the refusal
 *
 * The record says it outright: the screen-reader question is whether a **blocked** dispatch is
 * understandable, "because a blocked dispatch that sounds like a broken screen is how the control
 * gets worked around". So an unpaid job is not hidden from the queue and its control is not
 * disabled: it stays readable, reachable and reorderable, and scanning it produces a sentence with
 * three parts — what was refused, the amount that refused it, and the two ways out. A greyed row
 * with no explanation is how a shop learns to dispatch on paper instead.
 *
 * ## Dispatch says it is irreversible before the control that does it
 *
 * Step 6. The confirmation states the consequence first and is reached before the button, not after
 * it — checklist item A11Y-39.
 *
 * ## Two factors at the doorstep, and neither of them is a signature
 *
 * A signature pad is a drag, and 2.5.7 requires a button alternative to every drag; the #50
 * blueprint makes the alternative primary. Typing the name of whoever actually took the garment,
 * plus the one-time password sent to the customer, is that alternative — and it is the better record
 * besides, because a scrawl proves nobody was there.
 *
 * ## Offline queues the confirmation and says what queued means
 *
 * A delivery confirmation is an idempotent submission, so it queues (unlike a payment). The screen
 * says it is on the device and not on the server, which is the part somebody has to know before they
 * drive away.
 *
 * There is no backend: the state is `useState` over the fixtures.
 */
export function DeliveryDispatchScreen() {
  const t = useDemoText()
  const formatters = getFormatters()
  const status = useShellStatus()

  const [open, setOpen] = useState<DeliveryEntry | null>(null)
  const [authorised, setAuthorised] = useState<string | null>(null)
  const [refused, setRefused] = useState<string | null>(null)
  const [exceptionRequested, setExceptionRequested] = useState(false)
  const [confirmingDispatch, setConfirmingDispatch] = useState(false)
  const [dispatched, setDispatched] = useState(false)
  const [recipient, setRecipient] = useState('')
  const [password, setPassword] = useState('')
  const [passwordError, setPasswordError] = useState<string | undefined>(undefined)
  const [failureReason, setFailureReason] = useState<string>(
    DELIVERY_FAILURE_REASONS[0]?.value ?? '',
  )
  const [outcome, setOutcome] = useState<'delivered' | 'failed' | 'queued' | null>(null)
  const [online, setOnline] = useState(true)

  /** The receive scan: the gate, and the only place it is evaluated. */
  function receiveScan(entry: DeliveryEntry) {
    setOpen(entry)
    setDispatched(false)
    setOutcome(null)

    /*
     * Everything the last doorstep left behind goes with it.
     *
     * Hiding the form is not clearing it: a second job scanned after the first was delivered would
     * reopen a filled-in, enabled confirmation carrying the previous recipient's name and their
     * one-time password, and one press would sign that person for somebody else's garment. The
     * custody record is the whole point of this screen, so the state that makes it is reset at the
     * scan rather than at the render.
     */
    setRecipient('')
    setPassword('')
    setPasswordError(undefined)
    setExceptionRequested(false)

    if (entry.eligibility === 'paid') {
      setRefused(null)
      setAuthorised(entry.job)
      status.announceScan({
        outcome: 'accepted',
        message: t(
          `${entry.job} authorised for dispatch to ${entry.customer}, ${entry.address}. Nothing is outstanding.`,
        ),
      })
      return
    }

    setAuthorised(null)
    const reason = t(
      `${entry.job} cannot be dispatched: ${formatters.formatMoney(entry.balance)} is outstanding on ${entry.customer}’s order. Take the balance at the counter, or ask a manager to approve an exception. The garment stays at the branch until one of those happens.`,
    )
    setRefused(reason)
    status.announceScan({ outcome: 'rejected', message: reason })
  }

  return (
    <section className="page journey-screen">
      <h1>{t('Delivery')}</h1>
      <p>
        {t(STAFF.delivery)} · {formatters.formatDateTime(NOW)}
      </p>

      <section aria-labelledby="delivery-queue" className="journey-section">
        <h2 id="delivery-queue">{t('1. Today’s run')}</h2>
        <ul className="journey-list">
          {DELIVERY_QUEUE.map((entry) => (
            <li key={entry.id}>
              <Card
                actions={
                  <ButtonGroup size="primary">
                    <Button
                      iconName="scan"
                      onClick={() => {
                        receiveScan(entry)
                      }}
                      size="primary"
                      variant="primary"
                    >
                      {t(`Receive scan on ${entry.job}`)}
                    </Button>
                  </ButtonGroup>
                }
                headingLevel={3}
                meta={
                  <StatusBadge
                    detail={
                      entry.eligibility === 'paid'
                        ? t('nothing outstanding')
                        : formatters.formatMoney(entry.balance)
                    }
                    status={entry.eligibility === 'paid' ? 'ready' : 'unpaid'}
                  />
                }
                selected={open?.id === entry.id}
                title={`${t(entry.customer)} — ${t(entry.garment)}`}
              >
                <p className="journey-row__meta">
                  {entry.job} · {t(entry.address)}
                </p>
              </Card>
            </li>
          ))}
        </ul>
      </section>

      {open === null ? null : (
        <>
          <section aria-labelledby="delivery-history" className="journey-section">
            <h2 id="delivery-history">{t('2. Where this job has been')}</h2>
            <Timeline
              entries={[
                {
                  id: 'ready',
                  title: t('Ready for collection'),
                  absoluteTime: formatters.formatDateTime(DATES.yesterday),
                  dateTime: DATES.yesterday,
                  actor: t(STAFF.tailorMaster),
                  status: 'ready',
                },
                ...(open.eligibility === 'paid'
                  ? [
                      {
                        id: 'paid',
                        title: t('Invoice settled in full'),
                        absoluteTime: formatters.formatDateTime(DATES.today),
                        dateTime: DATES.today,
                        actor: t(STAFF.cashier),
                        status: 'paid' as const,
                      },
                    ]
                  : [
                      {
                        id: 'blocked',
                        title: t('Dispatch blocked — balance outstanding'),
                        absoluteTime: formatters.formatDateTime(DATES.today),
                        dateTime: DATES.today,
                        actor: t('The dispatch gate'),
                        status: 'unpaid' as const,
                        detail: t(
                          `${formatters.formatMoney(open.balance)} outstanding. The blocking reason is on the job, not only on this screen, so the counter sees it too.`,
                        ),
                      },
                    ]),
              ]}
              label={t(`History of ${open.job}`)}
            />
          </section>

          {authorised === null ? null : (
            <Alert live="polite" title={t('Dispatch authorised')} tone="success">
              {t(`${authorised} is authorised. The authorisation is recorded against your scan.`)}
            </Alert>
          )}

          {refused === null ? null : (
            <section aria-labelledby="delivery-refused" className="journey-section">
              <h2 id="delivery-refused">{t('4. This one is blocked')}</h2>
              <Alert live="assertive" title={t('Dispatch refused')} tone="danger">
                {refused}
              </Alert>

              <h3>{t('5. The two ways out')}</h3>
              <ButtonGroup>
                <Button
                  iconName="rupee"
                  onClick={() => {
                    status.announceSync({
                      tone: 'info',
                      message: t(
                        `Sent to the counter: take ${formatters.formatMoney(open.balance)} on ${INVOICE.order}. The dispatch unblocks when the payment is recorded.`,
                      ),
                    })
                  }}
                  variant="primary"
                >
                  {t('Take the balance at the counter')}
                </Button>
                <Button
                  iconName="alert-circle"
                  onClick={() => {
                    setExceptionRequested(true)
                  }}
                >
                  {t('Request an exception approval')}
                </Button>
              </ButtonGroup>

              {!exceptionRequested ? null : (
                <Alert live="polite" title={t('Exception requested')} tone="info">
                  {t(
                    `A manager has to approve dispatching ${open.job} with ${formatters.formatMoney(open.balance)} outstanding. Until somebody approves it, the garment stays at the branch — the request does not release it.`,
                  )}
                </Alert>
              )}
            </section>
          )}

          {authorised === null ? null : (
            <section aria-labelledby="delivery-dispatch" className="journey-section">
              <h2 id="delivery-dispatch">{t('6. Dispatch')}</h2>
              <ButtonGroup size="primary">
                <Button
                  iconName="truck"
                  onClick={() => {
                    setConfirmingDispatch(true)
                  }}
                  size="primary"
                  variant="primary"
                >
                  {t('Dispatch scan')}
                </Button>
              </ButtonGroup>
            </section>
          )}

          {!dispatched ? null : (
            <section aria-labelledby="delivery-doorstep" className="journey-section">
              <h2 id="delivery-doorstep">{t('7. At the door')}</h2>
              <div className="journey-fields">
                <TextField
                  autoComplete="off"
                  description={t(
                    'Who actually took the garment. It need not be the customer — write the name they gave you.',
                  )}
                  label={t('Name of the person receiving it')}
                  name="recipientName"
                  onValueChange={setRecipient}
                  required
                  value={recipient}
                />
                <TextField
                  autoComplete="one-time-code"
                  description={t(
                    `The ${String(DOORSTEP.passwordLength)} digits sent to the customer’s phone when the garment left the branch.`,
                  )}
                  enterKeyHint="done"
                  inputMode="numeric"
                  label={t('One-time password')}
                  maxLength={DOORSTEP.passwordLength}
                  name="doorstepPassword"
                  onValueChange={(next) => {
                    setPassword(next)
                    setPasswordError(undefined)
                  }}
                  required
                  value={password}
                  {...(passwordError === undefined ? {} : { error: passwordError })}
                />
              </div>

              {/* Story control: the application reads the real connection state. */}
              <Switch
                description={t(
                  'A delivery confirmation is queued when there is no signal. A payment never is.',
                )}
                label={t('Working offline')}
                name="offline"
                onValueChange={(next) => {
                  setOnline(!next)
                }}
                value={!online}
              />

              <ButtonGroup
                destructiveAction={
                  <Button
                    iconName="x-circle"
                    onClick={() => {
                      setOutcome('failed')
                      status.announceSync({
                        tone: 'warning',
                        message: t(
                          `${open.job} returns to the branch: ${DELIVERY_FAILURE_REASONS.find((reason) => reason.value === failureReason)?.label ?? ''}. The customer is told, and the job goes back on the delivery queue.`,
                        ),
                      })
                    }}
                    variant="danger"
                  >
                    {t('Record a failed delivery')}
                  </Button>
                }
                size="primary"
              >
                <Button
                  iconName="check"
                  onClick={() => {
                    if (password !== DOORSTEP.correctPassword) {
                      setPasswordError(
                        t(
                          'Those digits do not match the ones sent to the customer. Ask them to read the message again, or send a new password to their phone.',
                        ),
                      )
                      return
                    }
                    if (!online) {
                      setOutcome('queued')
                      status.announceSync({
                        tone: 'info',
                        message: t(
                          `Queued on this device: ${open.job} received by ${recipient}. It is not on the server yet and nobody at the branch can see it. It sends when you have signal.`,
                        ),
                      })
                      return
                    }
                    setOutcome('delivered')
                    status.announceSync({
                      tone: 'success',
                      message: t(
                        `${open.job} delivered to ${recipient} at ${formatters.formatDateTime(NOW)}.`,
                      ),
                    })
                  }}
                  size="primary"
                  unavailable={recipient.trim() === '' || password === ''}
                  variant="primary"
                >
                  {t('Confirm the handover')}
                </Button>
              </ButtonGroup>

              <Select
                emptyLabel={null}
                label={t('If it failed, why')}
                name="failureReason"
                onValueChange={setFailureReason}
                options={DELIVERY_FAILURE_REASONS.map((reason) => ({
                  value: reason.value,
                  label: t(reason.label),
                }))}
                value={failureReason}
              />

              {outcome === null ? null : (
                <Alert
                  live="polite"
                  title={t(
                    outcome === 'delivered'
                      ? 'Delivered'
                      : outcome === 'queued'
                        ? 'Waiting on this device'
                        : 'Returned to the branch',
                  )}
                  tone={
                    outcome === 'delivered' ? 'success' : outcome === 'queued' ? 'info' : 'warning'
                  }
                >
                  {t(
                    outcome === 'delivered'
                      ? `${open.job} was received by ${recipient}. The customer gets the feedback link.`
                      : outcome === 'queued'
                        ? 'It is stored here only. Nobody at the branch can see it, and it is not confirmed until it has been sent and accepted.'
                        : `${open.job} is back on the branch queue and the customer has been told.`,
                  )}
                </Alert>
              )}
            </section>
          )}
        </>
      )}

      <ConfirmDialog
        action="dispatching this garment"
        confirmLabel={t('Dispatch it')}
        irreversible
        onCancel={() => {
          setConfirmingDispatch(false)
        }}
        onConfirm={() => {
          setConfirmingDispatch(false)
          setDispatched(true)
        }}
        open={confirmingDispatch}
        tier="confirm"
        title={t('Dispatch this garment?')}
      >
        {t(
          `Dispatch cannot be undone. ${open?.job ?? ''} leaves the branch's custody and becomes yours until somebody signs for it. If it comes back, that is a failed delivery, recorded as its own entry.`,
        )}
      </ConfirmDialog>
    </section>
  )
}
