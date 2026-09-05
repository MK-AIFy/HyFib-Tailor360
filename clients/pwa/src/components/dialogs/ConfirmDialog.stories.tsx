import { useState } from 'react'
import type { Meta, StoryObj } from '@storybook/react-vite'
import type { ShellKind } from '../../design-system/foundations/types'
import { Button } from '../primitives/Button'
import { ConfirmDialog } from './ConfirmDialog'
import type { ConfirmTier } from './confirmTiers'
import './dialogsStories.css'

/**
 * The three-tier confirmation.
 *
 * WCAG 3.3.4 names five actions on this product that must be reviewable, confirmable and reversible
 * only by a compensating action: posting an invoice, recording a payment, approving a dispatch
 * exception, posting a stocktake and cancelling an order. Order confirmation joins them. The tiers
 * are graded by what is at stake, not by how alarming the action sounds:
 *
 *  1. **confirm** — significant but recoverable.
 *  2. **confirm with a reason** — corrections, reprints, holds, cancellations and manual lookups.
 *     The reason goes on the audit event, and is never optional and never defaulted.
 *  3. **typed confirmation** — desktop and tablet administration only. Switch the story's shell to
 *     `phone` and watch it become tier two plus a second explicit press, which is the substitute the
 *     blueprint names and checklist item A11Y-BI-13 checks for.
 */
const meta = {
  title: 'Dialogs/ConfirmDialog',
  component: ConfirmDialog,
  args: {
    open: true,
    tier: 'confirm',
    title: 'Cancel order O-CBE01-2627-000188?',
    action: 'cancelling this order',
    confirmLabel: 'Cancel order',
    children: 'The job stops, the fabric is returned, and the customer is told.',
    onCancel: () => undefined,
    onConfirm: () => undefined,
  },
  parameters: { layout: 'fullscreen' },
} satisfies Meta<typeof ConfirmDialog>

export default meta
type Story = StoryObj<typeof meta>

interface OpenerProps {
  readonly tier: ConfirmTier
  readonly irreversible?: boolean
  readonly shellKind?: ShellKind
  readonly typedPhrase?: string
  readonly title: string
  readonly action: string
  readonly confirmLabel: string
  readonly consequence: string
}

/** A page with the control that opens it, so focus has somewhere real to come from and go back to. */
function Opener({
  tier,
  irreversible = false,
  shellKind,
  typedPhrase,
  title,
  action,
  confirmLabel,
  consequence,
}: OpenerProps) {
  const [open, setOpen] = useState(false)
  const [outcome, setOutcome] = useState<string | null>(null)

  return (
    <div className="dialogs-story-page">
      <p>Focus opens on Cancel, never on the control that does the thing.</p>
      <div>
        <Button
          onClick={() => {
            setOpen(true)
            setOutcome(null)
          }}
          variant="danger"
        >
          {confirmLabel}
        </Button>
      </div>
      {outcome === null ? null : <p>Confirmed. Reason recorded: {outcome}</p>}
      <ConfirmDialog
        action={action}
        confirmLabel={confirmLabel}
        irreversible={irreversible}
        onCancel={() => {
          setOpen(false)
        }}
        onConfirm={({ reason }) => {
          setOutcome(reason ?? 'none asked for')
          setOpen(false)
        }}
        open={open}
        tier={tier}
        title={title}
        {...(shellKind === undefined ? {} : { shellKind })}
        {...(typedPhrase === undefined ? {} : { typedPhrase })}
      >
        {consequence}
      </ConfirmDialog>
    </div>
  )
}

/** Tier one. Significant, and recoverable. */
export const Confirm: Story = {
  render: () => (
    <Opener
      action="marking this job ready"
      confirmLabel="Mark ready"
      consequence="The job leaves the workboard and joins the delivery queue."
      tier="confirm"
      title="Mark job J-CBE01-2627-000512-01 ready?"
    />
  ),
}

/** Tier one, on something nothing puts back. The sentence is fixed word for word by the blueprint. */
export const Irreversible: Story = {
  render: () => (
    <Opener
      action="posting this invoice"
      confirmLabel="Post invoice"
      consequence="Invoice INV-CBE01-2627-000441 for ₹12,34,567.89 is posted. Only a credit note reverses it."
      irreversible
      tier="confirm"
      title="Post invoice INV-CBE01-2627-000441?"
    />
  ),
}

/**
 * Tier two: a reason, on the audit event.
 *
 * Checklist item A11Y-87 asks whether the reason field is labelled, announced as required, and
 * announced with **what it will be attached to** — rather than an unlabelled box inside a dialog. It
 * is a real field from the forms family, with the one `FieldProps` contract, for exactly that reason.
 */
export const ConfirmWithReason: Story = {
  render: () => (
    <Opener
      action="cancelling this order"
      confirmLabel="Cancel order"
      consequence="The job stops, the fabric is returned, and the customer is told."
      irreversible
      tier="reason"
      title="Cancel order O-CBE01-2627-000188?"
    />
  ),
}

/** Tier three on a desktop: the phrase is typed exactly as it is shown. */
export const TypedConfirmationOnDesktop: Story = {
  render: () => (
    <Opener
      action="closing this branch for new orders"
      confirmLabel="Close branch"
      consequence="No new orders can be taken at Coimbatore 1 until it is reopened."
      shellKind="desktop"
      tier="typed"
      title="Close Coimbatore 1 for new orders?"
      typedPhrase="CLOSE COIMBATORE 1"
    />
  ),
}

/**
 * The same action on a phone.
 *
 * There is no typed field, and there never is one: typing a phrase one-handed in a workshop in
 * front of a waiting customer is a reason to hand the phone to somebody else, which is how a
 * confirmation gets answered by the wrong person. The substitute is a reason plus a second explicit
 * press, and the first press says so.
 */
export const TypedConfirmationOnAPhone: Story = {
  globals: { viewport: { value: 'referencePhone' } },
  render: () => (
    <Opener
      action="closing this branch for new orders"
      confirmLabel="Close branch"
      consequence="No new orders can be taken at Coimbatore 1 until it is reopened."
      shellKind="phone"
      tier="typed"
      title="Close Coimbatore 1 for new orders?"
      typedPhrase="CLOSE COIMBATORE 1"
    />
  ),
}

/** The pseudo-locale, at 40% growth, on a phone shell. */
export const PseudoLocale: Story = {
  globals: { locale: 'en-XA' },
  render: () => (
    <Opener
      action="cancelling this order"
      confirmLabel="Cancel order"
      consequence="The job stops, the fabric is returned, and the customer is told."
      irreversible
      shellKind="phone"
      tier="reason"
      title="Cancel order O-CBE01-2627-000188?"
    />
  ),
}
