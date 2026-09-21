import { useState } from 'react'
import { ConfirmDialog } from '../../components/dialogs/ConfirmDialog'
import { Alert } from '../../components/primitives/Alert'
import { Button } from '../../components/primitives/Button'
import { ButtonGroup } from '../../components/primitives/ButtonGroup'
import { Card } from '../../components/primitives/Card'
import { StatusBadge } from '../../components/primitives/StatusBadge'
import { getFormatters } from '../../i18n/formatters'
import { CUSTOMERS } from '../fixtures/customers'
import { INVOICE } from '../fixtures/billing'
import { DATES } from '../fixtures/branch'

/**
 * Steps 7 and 8 of `A11Y-PZ-01` — review the draft order, then confirm it.
 *
 * ## What happened to `ReceptionIntakeScreen`
 *
 * This is what is left of it (#586). That component covered four steps of the Reception journey —
 * find the customer, record consent, review the draft order, confirm — because when #50 needed
 * something to walk `A11Y-RJ-01` against, none of the real screens existed.
 *
 * Two of those four now do. Finding a customer is `CustomerSearchRoute` and consent is
 * `CustomerConsentRoute`, both wired into the router, both talking to the real API, and both with
 * their own state and pseudo-locale stories under **Customers/Screens**. Keeping a second, mocked
 * copy of the same two steps would mean an accessibility walkthrough could pass against a screen
 * nobody can reach — which is the failure #586 exists to remove, and the more dangerous kind of
 * duplication because the mock is the easier one to keep green.
 *
 * The other two steps have no real screen: a draft order is Orders' (#23 onward), whose client
 * surface is not built. Deleting them along with the rest would have taken away the only thing
 * `A11Y-PZ-01` steps 7 and 8 can be walked against, so they stay here, named for what they are,
 * until Orders replaces them the same way Customers just did.
 *
 * ## What it is still proving
 *
 *  - **The money is formatted by the `formatters` module**, never by `toLocaleString` at a call
 *    site, so the Tamil and pseudo-locale stories show real lakh grouping.
 *  - **The confirmation states what will happen before the control that does it** (checklist item
 *    A11Y-39). It is the plain `confirm` tier: an order can still be revised until production
 *    starts, so it is significant but recoverable.
 *  - **The outcome is announced politely and is not a toast**, because an order number is something
 *    somebody has to be able to read back.
 *
 * There is no backend, and there deliberately is not one: this is a walkthrough target, not a
 * screen, and the moment it grows an API call it becomes a second implementation again.
 */
export function OrderDraftConfirmationScreen() {
  const formatters = getFormatters()
  const [confirming, setConfirming] = useState(false)
  const [confirmed, setConfirmed] = useState(false)

  const customer = CUSTOMERS[0]

  return (
    <section className="page journey-screen">
      <h1>Review the draft order</h1>

      <section aria-labelledby="order-draft" className="journey-section">
        <h2 id="order-draft">7. The draft order</h2>
        <Card headingLevel={3} title="Blouse — pattern, one garment">
          <dl className="journey-summary">
            <dt>Customer</dt>
            <dd>{customer === undefined ? '—' : `${customer.name} · ${customer.id}`}</dd>
            <dt>Promised</dt>
            <dd>{formatters.formatShortDate(DATES.nextWeek)}</dd>
            <dt>Total, including GST</dt>
            <dd className="journey-amount journey-total">
              {formatters.formatMoney(INVOICE.total)}
            </dd>
            <dt>Advance to take now</dt>
            <dd className="journey-amount">{formatters.formatMoney(INVOICE.advanceReceived)}</dd>
          </dl>
        </Card>
      </section>

      <ButtonGroup size="primary">
        <Button
          iconName="check"
          onClick={() => {
            setConfirming(true)
          }}
          size="primary"
          variant="primary"
        >
          Confirm order
        </Button>
      </ButtonGroup>

      {confirmed ? (
        <Alert live="polite" title="Order confirmed" tone="success">
          <p>
            {INVOICE.order} and job J-CBE01-2627-000512-01 have been allocated, and the price and
            measurement snapshots are frozen.
          </p>
          <StatusBadge detail="Label not yet printed" status="draft" />
        </Alert>
      ) : null}

      <ConfirmDialog
        action="confirming this order"
        confirmLabel="Confirm order"
        onCancel={() => {
          setConfirming(false)
        }}
        onConfirm={() => {
          setConfirming(false)
          setConfirmed(true)
        }}
        open={confirming}
        tier="confirm"
        title="Confirm this order?"
      >
        The order and job numbers are allocated now, and the price, design and measurement snapshots
        are frozen against them. The order can still be revised until production starts.
      </ConfirmDialog>
    </section>
  )
}
