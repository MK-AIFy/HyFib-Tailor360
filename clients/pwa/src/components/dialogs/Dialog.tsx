import { useId } from 'react'
import type { MouseEvent, ReactNode, RefObject } from 'react'
import { createPortal } from 'react-dom'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import type { ShellKind } from '../../design-system/foundations/types'
import { IconButton } from '../primitives/IconButton'
import { resolveDialogPresentation } from './dialogVariants'
import type { DialogHeadingLevel, DialogPresentation } from './dialogVariants'
import { useDialogBehaviour } from './useDialogBehaviour'
import { useViewportShellKind } from './useViewportShellKind'
import './dialogs.css'

export interface DialogProps {
  /** Rendering is all-or-nothing: closed renders nothing at all, so nothing is left in the tree. */
  readonly open: boolean
  /** The dialog's name, rendered as its heading and used as its accessible name. */
  readonly title: string
  /** The body. */
  readonly children: ReactNode
  /**
   * Closes it. Called by Escape, by the close control and by the scrim. Escape always closes: 2.1.2
   * No Keyboard Trap is absolute, which is why there is no prop here that could switch it off.
   */
  readonly onClose: () => void
  /** Defaults to `auto`: a sheet on a phone, a centred dialog on a tablet or desktop. */
  readonly presentation?: DialogPresentation
  /**
   * What the dialog is about, announced with its name when it opens. A confirmation puts what will
   * happen here, so that a screen reader user hears the consequence before reaching the controls
   * (checklist item A11Y-39).
   */
  readonly description?: ReactNode
  /** The control row at the foot. On a sheet it is inside thumb reach and clear of the gesture bar. */
  readonly footer?: ReactNode
  /** Where focus lands on opening. Defaults to the first focusable element. Never the confirm. */
  readonly initialFocusRef?: RefObject<HTMLElement | null>
  /**
   * Whether a press on the scrim closes it. True by default; a dialog holding typed input passes
   * false, because losing a typed reason to a mis-touch while holding a garment is exactly the
   * accident 2.5.2 Pointer Cancellation exists to prevent.
   */
  readonly closeOnScrimPress?: boolean
  /** The heading level. A dialog starts its own context, so `h2` is the default rather than a guess. */
  readonly headingLevel?: DialogHeadingLevel
  /** Overrides the detected shell, for a story, a test, or a layout that already knows. */
  readonly shellKind?: ShellKind
  readonly className?: string
}

interface DialogSurfaceProps extends Omit<DialogProps, 'open'> {
  readonly presentation: DialogPresentation
  readonly closeOnScrimPress: boolean
  readonly headingLevel: DialogHeadingLevel
}

function DialogSurface({
  title,
  children,
  onClose,
  presentation,
  description,
  footer,
  initialFocusRef,
  closeOnScrimPress,
  headingLevel,
  shellKind,
  className,
}: DialogSurfaceProps) {
  const intl = useIntl()
  const titleId = useId()
  const descriptionId = useId()
  const shell = useViewportShellKind(shellKind)
  const resolved = resolveDialogPresentation(presentation, shell)
  const { surfaceRef, onKeyDown } = useDialogBehaviour({
    onClose,
    ...(initialFocusRef === undefined ? {} : { initialFocusRef }),
  })
  const Heading = `h${String(headingLevel)}` as 'h2'

  const onScrimPress = (event: MouseEvent<HTMLDivElement>) => {
    // Only a press that landed on the scrim itself, and only on release — a click event already
    // fires on release, which is what 2.5.2 asks for and why nothing here listens to pointerdown.
    if (closeOnScrimPress && event.target === event.currentTarget) {
      onClose()
    }
  }

  return (
    <div
      className="dialog-scrim"
      /* Paired with DIALOG_ROOT_ATTRIBUTE in useDialogBehaviour, which finds this node to work out
         what the rest of the document is. */
      data-dialog-root=""
      data-presentation={resolved}
      onClick={onScrimPress}
    >
      <div
        aria-labelledby={titleId}
        aria-modal="true"
        className={cx('dialog', className)}
        data-presentation={resolved}
        onKeyDown={onKeyDown}
        ref={surfaceRef}
        role="dialog"
        tabIndex={-1}
        {...(description === undefined ? {} : { 'aria-describedby': descriptionId })}
      >
        <div className="dialog__header">
          <Heading className="dialog__title" id={titleId}>
            {title}
          </Heading>
          <IconButton
            className="dialog__close"
            label={intl.formatMessage({ id: 'dialogs.close' })}
            name="close"
            onClick={onClose}
          />
        </div>
        {description === undefined ? null : (
          <div className="dialog__description" id={descriptionId}>
            {description}
          </div>
        )}
        <div className="dialog__body">{children}</div>
        {footer === undefined ? null : <div className="dialog__footer">{footer}</div>}
      </div>
    </div>
  )
}

/**
 * A modal: a bottom sheet on a phone, a centred dialog on a tablet or a desktop.
 *
 * The behaviour that matters is in `useDialogBehaviour` — focus moved in, the tab cycle trapped, the
 * rest of the document `inert`, Escape closing, focus returned to whatever opened it — and the
 * reasoning for each is documented there against checklist items A11Y-61 to A11Y-64.
 *
 * What this component adds is the shape, and the shape is a shop-floor decision rather than a
 * fashion. On a phone the dialog is a sheet at the bottom of the screen because the person is
 * holding a garment in the other hand and their thumb reaches the bottom third; its footer sits
 * clear of the system gesture bar, which the product rule in section 5 of
 * docs/nfr/accessibility-localisation.md reserves outright. On a tablet or a desktop it is centred,
 * because the pointer is already wherever the eye is.
 *
 * It renders into a portal at the end of `body`. That is not for stacking convenience: it is what
 * makes "everything else is inert" a single pass over the body's children rather than a walk up
 * whatever tree the screen happened to nest it in.
 */
export function Dialog({
  open,
  presentation = 'auto',
  closeOnScrimPress = true,
  headingLevel = 2,
  ...rest
}: DialogProps) {
  if (!open) {
    return null
  }

  return createPortal(
    <DialogSurface
      closeOnScrimPress={closeOnScrimPress}
      headingLevel={headingLevel}
      presentation={presentation}
      {...rest}
    />,
    document.body,
  )
}
