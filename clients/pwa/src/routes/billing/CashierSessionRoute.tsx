import { useState } from 'react'
import type { FormEvent } from 'react'
import { FormattedMessage, useIntl } from 'react-intl'
import { Link } from 'react-router'
import { useAdminResource } from '../../admin/useAdminResource'
import { AuthProblemAlert } from '../../auth/AuthProblemAlert'
import { BillingProblemAlert } from '../../billing/BillingProblemAlert'
import { billingProblemCode } from '../../billing/billingProblems'
import {
  closeCashierSession,
  listAvailablePaymentModes,
  listCashierSessions,
  openCashierSession,
} from '../../billing/billingApi'
import {
  NOTE_DENOMINATIONS,
  countedCashTotal,
  emptyCashCount,
  toDenominationRequests,
} from '../../billing/denominationCount'
import type { CashCount } from '../../billing/denominationCount'
import type { CashierSession } from '../../billing/types'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { LoadingState } from '../../components/states/LoadingState'
import { OfflineBlockedAction } from '../../components/states/OfflineBlockedAction'
import { useNetworkState } from '../../components/states/useNetworkState'
import { NumericStepper } from '../../design-system/components/forms/NumericStepper'
import { getFormatters } from '../../i18n/formatters'
import './billing.css'

/**
 * The cashier's day at the drawer: opening a session, and closing it against a denomination count
 * sheet (#165).
 *
 * ## Counted, expected and variance
 *
 * The count sheet computes and shows what the drawer counts to *before* the person presses Close —
 * `denominationCount.ts`'s arithmetic, so a typo is caught while it is still easy to fix. What the
 * session *expected* is a fact only the server holds, computed from every payment it recorded, so
 * the expected-versus-counted comparison and the variance the branch is accountable for are shown
 * from its answer once the session is actually closed, never guessed at here.
 *
 * ## Asking for a reason only when one turns out to be needed
 *
 * The close is tried first without a reason. If the server refuses with
 * `billing.variance-reason-required` — the variance turned out to be beyond the branch's configured
 * threshold, a number this client does not hold — the same confirmation reopens asking for one,
 * which is what turns a second, avoidable refusal into the one question that was actually needed.
 */
export function CashierSessionRoute() {
  const intl = useIntl()
  const network = useNetworkState()
  const formatters = getFormatters()

  // An array, never `CashierSession | null`: `useAdminResource`'s own `null` means "not read yet",
  // so a resource whose honest answer can be null needs a value type that has no null of its own —
  // an empty array is "read, and there is none", which is a fact rather than a wait.
  const openSessions = useAdminResource('cashier-session-open', (signal) =>
    listCashierSessions('Open', signal),
  )
  const openSession = { ...openSessions, value: openSessions.value?.[0] ?? null }
  const modes = useAdminResource('payment-modes-for-close', (signal) =>
    listAvailablePaymentModes(signal),
  )
  const nonCashModes = (modes.value ?? []).filter((mode) => mode.code !== 'CASH')

  /* Opening. ------------------------------------------------------------------------------- */
  const [openingFloat, setOpeningFloat] = useState<number | undefined>(undefined)
  const [openBusy, setOpenBusy] = useState(false)
  const [openFailure, setOpenFailure] = useState<unknown>(null)
  const [openKey, setOpenKey] = useState<string | null>(null)

  const open = async (event: FormEvent): Promise<void> => {
    event.preventDefault()
    if (openingFloat === undefined || openingFloat < 0) {
      return
    }
    setOpenBusy(true)
    setOpenFailure(null)
    const key = openKey ?? crypto.randomUUID()
    setOpenKey(key)
    try {
      await openCashierSession({ body: { openingFloat }, idempotencyKey: key })
      setOpenKey(null)
      openSession.reload()
    } catch (cause: unknown) {
      setOpenFailure(cause)
    } finally {
      setOpenBusy(false)
    }
  }

  /* Closing. --------------------------------------------------------------------------------- */
  const [count, setCount] = useState<CashCount>(() => emptyCashCount())
  const [modeCounts, setModeCounts] = useState<Readonly<Record<string, number>>>({})
  const [reason, setReason] = useState('')
  const [confirming, setConfirming] = useState(false)
  const [needsReason, setNeedsReason] = useState(false)
  const [closeKey, setCloseKey] = useState<{
    readonly fingerprint: string
    readonly key: string
  } | null>(null)
  const [closeBusy, setCloseBusy] = useState(false)
  const [closeFailure, setCloseFailure] = useState<unknown>(null)
  const [closed, setClosed] = useState<CashierSession | null>(null)

  const cashCounted = countedCashTotal(count)

  const setNoteQuantity = (denomination: number, quantity: number): void => {
    setCount((current) => ({
      ...current,
      notes: current.notes.map((note) =>
        note.denomination === denomination ? { ...note, quantity } : note,
      ),
    }))
  }

  const closeRequest = (reasonText: string) => ({
    denominations: toDenominationRequests(count),
    modeTotals: nonCashModes.map((mode) => ({
      modeCode: mode.code,
      counted: modeCounts[mode.code] ?? 0,
    })),
    reason: reasonText.trim() === '' ? null : reasonText.trim(),
  })

  const submitClose = async (outcome: { readonly reason?: string }): Promise<void> => {
    if (openSession.value === null) {
      return
    }
    const reasonText = outcome.reason ?? reason
    const body = closeRequest(reasonText)
    setCloseBusy(true)
    setCloseFailure(null)

    const fingerprint = `${openSession.value.id}:${JSON.stringify(body)}`
    const key =
      closeKey !== null && closeKey.fingerprint === fingerprint ? closeKey.key : crypto.randomUUID()
    setCloseKey({ fingerprint, key })

    try {
      const session = await closeCashierSession({
        sessionId: openSession.value.id,
        body,
        idempotencyKey: key,
      })
      setCloseKey(null)
      setConfirming(false)
      setClosed(session)
    } catch (cause: unknown) {
      if (billingProblemCode(cause) === 'billing.variance-reason-required' && !needsReason) {
        // The one refusal this screen answers by asking a question rather than showing an error:
        // reopen the same confirmation, now asking for the reason the server says it needs.
        setNeedsReason(true)
        setCloseFailure(null)
      } else {
        setCloseFailure(cause)
      }
    } finally {
      setCloseBusy(false)
    }
  }

  if (openSession.loading) {
    return (
      <section className="page billing">
        <h1>
          <FormattedMessage id="billing.cashier.title" />
        </h1>
        <LoadingState what={intl.formatMessage({ id: 'billing.cashier.loading' })} />
      </section>
    )
  }

  if (closed !== null) {
    return (
      <section className="page billing">
        <h1>
          <FormattedMessage id="billing.cashier.title" />
        </h1>
        <Alert
          live="polite"
          title={intl.formatMessage({ id: 'billing.cashier.closed.title' })}
          tone="success"
        >
          {intl.formatMessage(
            { id: 'billing.cashier.closed.body' },
            {
              counted: formatters.formatMoney(closed.countedTotal),
              expected: formatters.formatMoney(closed.expectedTotal),
            },
          )}
        </Alert>
        {closed.reconciliationBatch !== null && closed.reconciliationBatch.status !== 'Approved' ? (
          <Alert live="polite" tone="warning">
            {intl.formatMessage({ id: 'billing.cashier.closed.varianceNotice' })}{' '}
            <Link to={`/billing/cashier-sessions/${closed.id}/reconciliation`}>
              {intl.formatMessage({ id: 'billing.reconciliation.title' })}
            </Link>
          </Alert>
        ) : null}
      </section>
    )
  }

  return (
    <section className="page billing">
      <h1>
        <FormattedMessage id="billing.cashier.title" />
      </h1>

      <AuthProblemAlert failure={openSession.failure} />

      {openSession.value === null ? (
        <>
          <h2>
            <FormattedMessage id="billing.cashier.open.title" />
          </h2>
          <p className="billing__lede">
            <FormattedMessage id="billing.cashier.open.body" />
          </p>
          <form
            className="billing__form"
            noValidate
            onSubmit={(event) => {
              void open(event)
            }}
          >
            <NumericStepper
              decimalPlaces={2}
              id="cashier-opening-float"
              inputMode="decimal"
              label={intl.formatMessage({ id: 'billing.cashier.open.float.label' })}
              min={0}
              name="openingFloat"
              onValueChange={setOpeningFloat}
              required
              showRangeHint={false}
              size="primary"
              unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
              {...(openingFloat === undefined ? {} : { value: openingFloat })}
            />
            <BillingProblemAlert failure={openFailure} />
            {network.online ? (
              <Button
                busy={openBusy}
                iconName="rupee"
                size="primary"
                type="submit"
                variant="primary"
              >
                {intl.formatMessage({
                  id: openBusy ? 'billing.cashier.opening' : 'billing.cashier.open.action',
                })}
              </Button>
            ) : (
              <OfflineBlockedAction
                action={intl.formatMessage({ id: 'billing.cashier.open.offlineAction' })}
              />
            )}
          </form>
        </>
      ) : (
        <>
          <p>
            {intl.formatMessage(
              { id: 'billing.cashier.status.open' },
              { date: formatters.formatDateTime(openSession.value.openedAt) },
            )}
          </p>

          <h2>
            <FormattedMessage id="billing.cashier.close.title" />
          </h2>
          <p className="billing__lede">
            <FormattedMessage id="billing.cashier.close.body" />
          </p>

          <div className="billing__count-sheet">
            <h3>
              <FormattedMessage id="billing.cashier.close.notes" />
            </h3>
            {NOTE_DENOMINATIONS.map((denomination) => (
              <div className="billing__count-row" key={denomination}>
                <NumericStepper
                  id={`cashier-note-${String(denomination)}`}
                  label={intl.formatMessage(
                    { id: 'billing.cashier.close.note.label' },
                    { denomination },
                  )}
                  min={0}
                  name={`note-${String(denomination)}`}
                  onValueChange={(quantity) => {
                    setNoteQuantity(denomination, quantity)
                  }}
                  showRangeHint={false}
                  value={
                    count.notes.find((note) => note.denomination === denomination)?.quantity ?? 0
                  }
                />
              </div>
            ))}

            <NumericStepper
              decimalPlaces={2}
              description={intl.formatMessage({ id: 'billing.cashier.close.coins.hint' })}
              id="cashier-coins"
              inputMode="decimal"
              label={intl.formatMessage({ id: 'billing.cashier.close.coins.label' })}
              min={0}
              name="coinsValue"
              onValueChange={(coinsValue) => {
                setCount((current) => ({ ...current, coinsValue }))
              }}
              showRangeHint={false}
              unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
              value={count.coinsValue}
            />

            <p className="billing__total">
              {intl.formatMessage(
                { id: 'billing.cashier.close.cashTotal' },
                { amount: formatters.formatMoney(cashCounted) },
              )}
            </p>

            {nonCashModes.length === 0 ? null : (
              <div className="billing__mode-totals">
                <h3>
                  <FormattedMessage id="billing.cashier.close.modeTotals" />
                </h3>
                {nonCashModes.map((mode) => (
                  <NumericStepper
                    decimalPlaces={2}
                    id={`cashier-mode-${mode.code}`}
                    key={mode.code}
                    label={intl.formatMessage(
                      { id: 'billing.cashier.close.mode.label' },
                      { mode: mode.name },
                    )}
                    min={0}
                    name={`mode-${mode.code}`}
                    onValueChange={(counted) => {
                      setModeCounts((current) => ({ ...current, [mode.code]: counted }))
                    }}
                    showRangeHint={false}
                    unit={{ symbol: '₹', label: intl.formatMessage({ id: 'units.rupee.label' }) }}
                    value={modeCounts[mode.code] ?? 0}
                  />
                ))}
              </div>
            )}

            <BillingProblemAlert failure={closeFailure} />

            {network.online ? (
              <Button
                iconName="check"
                onClick={() => {
                  setConfirming(true)
                }}
                size="primary"
                variant="primary"
              >
                <FormattedMessage id="billing.cashier.close.action" />
              </Button>
            ) : (
              <OfflineBlockedAction
                action={intl.formatMessage({ id: 'billing.cashier.close.offlineAction' })}
              />
            )}
          </div>
        </>
      )}

      {confirming ? (
        <ConfirmDialog
          action={intl.formatMessage({ id: 'billing.cashier.close.confirm.action' })}
          busy={closeBusy}
          confirmLabel={intl.formatMessage({ id: 'billing.cashier.close.confirm.action' })}
          irreversible
          onCancel={() => {
            setConfirming(false)
            setNeedsReason(false)
          }}
          onConfirm={(outcome) => {
            if (needsReason && outcome.reason !== undefined) {
              setReason(outcome.reason)
            }
            void submitClose(outcome)
          }}
          open
          problem={<BillingProblemAlert failure={closeFailure} />}
          tier={needsReason ? 'reason' : 'confirm'}
          title={intl.formatMessage({ id: 'billing.cashier.close.confirm.title' })}
        >
          <FormattedMessage id="billing.cashier.close.confirm.body" />
          {needsReason ? null : (
            <p className="billing__total">
              {intl.formatMessage(
                { id: 'billing.cashier.close.cashTotal' },
                { amount: formatters.formatMoney(cashCounted) },
              )}
            </p>
          )}
        </ConfirmDialog>
      ) : null}
    </section>
  )
}
