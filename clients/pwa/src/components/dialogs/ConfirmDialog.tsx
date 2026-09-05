import { useRef, useState } from 'react'
import type { ReactNode } from 'react'
import { useIntl } from 'react-intl'
import type { ShellKind } from '../../design-system/foundations/types'
import { TextArea } from '../../design-system/components/forms/TextArea'
import { TextField } from '../../design-system/components/forms/TextField'
import { Button } from '../primitives/Button'
import { Icon } from '../primitives/Icon'
import { Dialog } from './Dialog'
import { resolveConfirmTier } from './confirmTiers'
import type { ConfirmTier } from './confirmTiers'
import { useViewportShellKind } from './useViewportShellKind'
import './dialogs.css'

/** What the confirmation produced. The reason is present exactly when the tier asked for one. */
export interface ConfirmOutcome {
  readonly reason?: string
}

export interface ConfirmDialogProps {
  readonly open: boolean
  /** The question, as a heading: "Cancel order O-CBE01-2627-000188?" */
  readonly title: string
  /**
   * What will happen, in words, announced with the dialog when it opens. Checklist item A11Y-39
   * asks for exactly this: the confirmation states what will happen and what cannot be undone,
   * before the person reaches the control that does it.
   */
  readonly children: ReactNode
  /** Which tier the action carries. See `confirmTiers.ts`. */
  readonly tier: ConfirmTier
  /**
   * The action as a verb phrase, for the reason field's description and the second-press prompt:
   * "cancelling this order", "reprinting the label". It is what the audit trail will be about.
   */
  readonly action: string
  /**
   * The confirming control's name, which must say what it does rather than "OK" — 2.5.3 Label in
   * Name is what makes "tap Cancel order" work on voice control (checklist item A11Y-39).
   */
  readonly confirmLabel: string
  readonly cancelLabel?: string
  readonly onConfirm: (outcome: ConfirmOutcome) => void
  readonly onCancel: () => void
  /**
   * True when nothing the person can do afterwards puts this back. Renders the sentence the
   * blueprint fixes word for word — "Cannot be undone — a supervisor correction is needed" — as part
   * of what is announced when the dialog opens.
   */
  readonly irreversible?: boolean
  /** The phrase to type, for the typed tier. Required whenever that tier is actually rendered. */
  readonly typedPhrase?: string
  /** The confirmation is in flight. Keeps the control focusable and swallows a second press. */
  readonly busy?: boolean
  /** Overrides the detected shell — a story, a test, or a layout that already knows. */
  readonly shellKind?: ShellKind
}

/**
 * The three-tier confirmation.
 *
 * WCAG 3.3.4 Error Prevention names five actions on this product that must be reviewable,
 * confirmable and reversible only by a compensating action: posting an invoice, recording a payment,
 * approving a dispatch exception, posting a stocktake and cancelling an order. Order confirmation
 * joins them. This component is where that promise is kept, and the tiers are graded by what is at
 * stake rather than by how alarming the action sounds.
 *
 * Two rules are absolute, and both are here rather than in a review checklist:
 *
 *  - **The typed tier never appears on a phone** (`resolveConfirmTier`, checklist item A11Y-BI-13).
 *    A phone gets confirm-with-reason plus a second explicit press instead. Typing a phrase
 *    one-handed in a workshop, in front of a waiting customer, is not a safety measure — it is a
 *    reason to hand the phone to somebody else, which is how confirmations get answered by the
 *    wrong person.
 *  - **The reason is never defaulted and never optional.** Checklist item A11Y-87 asks whether the
 *    reason field is labelled, announced as required, and announced with what it will be attached
 *    to. It is a real field from the forms family, with the one `FieldProps` contract, for that
 *    reason: an unlabelled box inside a confirmation dialog is the defect the item is looking for.
 *
 * Focus opens on Cancel, not on Confirm. A dialog that opens with the destructive control focused is
 * a dialog answered by a held Enter key.
 */
export function ConfirmDialog({
  open,
  title,
  children,
  tier,
  action,
  confirmLabel,
  cancelLabel,
  onConfirm,
  onCancel,
  irreversible = false,
  typedPhrase,
  busy = false,
  shellKind,
}: ConfirmDialogProps) {
  const intl = useIntl()
  const shell = useViewportShellKind(shellKind)
  const resolved = resolveConfirmTier(tier, shell)
  const cancelRef = useRef<HTMLButtonElement | null>(null)

  const [reason, setReason] = useState('')
  const [typed, setTyped] = useState('')
  const [reasonError, setReasonError] = useState<string | undefined>(undefined)
  const [typedError, setTypedError] = useState<string | undefined>(undefined)
  const [armed, setArmed] = useState(false)

  const needsReason = resolved.tier === 'reason'
  const needsTyped = resolved.tier === 'typed'
  const phrase = typedPhrase ?? confirmLabel

  const confirm = () => {
    if (busy) {
      return
    }

    if (needsReason && reason.trim().length === 0) {
      setReasonError(intl.formatMessage({ id: 'dialogs.reason.missing' }))
      return
    }
    if (needsTyped && typed.trim() !== phrase) {
      setTypedError(intl.formatMessage({ id: 'dialogs.typed.mismatch' }, { phrase }))
      return
    }

    // The phone substitute for the typed tier: the first press arms, the second confirms. Editing
    // the reason disarms it again, so the second press is always the one that follows a read.
    if (resolved.requiresSecondPress && !armed) {
      setArmed(true)
      return
    }

    onConfirm(needsReason ? { reason: reason.trim() } : {})
  }

  return (
    <Dialog
      closeOnScrimPress={false}
      description={
        <>
          <div className="confirm-dialog__consequence">{children}</div>
          {irreversible ? (
            <p className="confirm-dialog__irreversible">
              <Icon name="alert-triangle" />
              <span>{intl.formatMessage({ id: 'dialogs.irreversible' })}</span>
            </p>
          ) : null}
        </>
      }
      footer={
        <>
          <Button onClick={onCancel} ref={cancelRef} variant="secondary">
            {cancelLabel ?? intl.formatMessage({ id: 'dialogs.cancel' })}
          </Button>
          <Button busy={busy} onClick={confirm} variant="danger">
            {armed ? intl.formatMessage({ id: 'dialogs.secondTap.label' }) : confirmLabel}
          </Button>
        </>
      }
      initialFocusRef={cancelRef}
      onClose={onCancel}
      open={open}
      title={title}
      {...(shellKind === undefined ? {} : { shellKind })}
    >
      {needsReason ? (
        <TextArea
          description={intl.formatMessage({ id: 'dialogs.reason.description' }, { action })}
          label={intl.formatMessage({ id: 'dialogs.reason.label' })}
          name="reason"
          onValueChange={(value) => {
            setReason(value)
            setReasonError(undefined)
            setArmed(false)
          }}
          required
          value={reason}
          {...(reasonError === undefined ? {} : { error: reasonError })}
        />
      ) : null}

      {needsTyped ? (
        <>
          <p className="confirm-dialog__phrase">
            <code>{phrase}</code>
          </p>
          <TextField
            autoComplete="off"
            description={intl.formatMessage({ id: 'dialogs.typed.description' })}
            label={intl.formatMessage({ id: 'dialogs.typed.label' }, { phrase })}
            name="typed-confirmation"
            onValueChange={(value) => {
              setTyped(value)
              setTypedError(undefined)
            }}
            required
            value={typed}
            {...(typedError === undefined ? {} : { error: typedError })}
          />
        </>
      ) : null}

      {/*
        A live region that is present from the moment the dialog opens rather than appearing with
        its message: a region inserted at the same time as its content is the commonest reason an
        announcement is missed.
      */}
      <p className="confirm-dialog__prompt" role="status">
        {armed ? intl.formatMessage({ id: 'dialogs.secondTap.prompt' }, { action }) : ''}
      </p>
    </Dialog>
  )
}
