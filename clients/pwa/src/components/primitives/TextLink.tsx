import type { AnchorHTMLAttributes, ReactNode, Ref } from 'react'
import { useIntl } from 'react-intl'
import { cx } from '../../design-system/foundations/cx'
import { Icon } from './Icon'
import './TextLink.css'

type PassThroughAttributes = Omit<
  AnchorHTMLAttributes<HTMLAnchorElement>,
  'className' | 'children' | 'style' | 'href' | 'target' | 'rel'
>

export interface TextLinkProps extends PassThroughAttributes {
  readonly children: ReactNode
  readonly href: string
  /**
   * Opens in a new tab.
   *
   * Off by default, and it should stay off nearly everywhere: a new tab on a phone is a lost back
   * button, and on a shared counter device it is a session somebody else finds open. When it is
   * genuinely right — an external supplier portal, a document that must stay open beside a form —
   * this adds `rel="noopener noreferrer"` and appends "opens in a new tab" to the accessible name,
   * because a change of context that is not announced is a 3.2.5 failure.
   */
  readonly external?: boolean
  /** A subdued link inside dense content, where the ordinary weight would be noise. */
  readonly quiet?: boolean
  readonly className?: string
  readonly ref?: Ref<HTMLAnchorElement>
}

/**
 * A text link.
 *
 * Underlined, always, and that is not a stylistic preference: an underline is the non-colour cue
 * 1.4.1 Use of Colour asks for, and inside a paragraph a link distinguished by colour alone is
 * invisible to a reader with a colour vision deficiency and to everybody at the counter in the
 * afternoon sun. The underline is offset rather than removed on hover.
 *
 * Target size: an inline link is exempt from 2.5.8 (section 5 names it explicitly), and the base
 * line height of 1.5 already gives it at least 24 px. A link that is *not* inline — one that stands
 * alone as a navigation action — should be a `Button` or a navigation item instead, both of which
 * carry a real target.
 *
 * This component renders a plain anchor and knows nothing about the router, which is deliberate: it
 * is used for external addresses, `tel:` and `mailto:` links, in-document anchors and the customer
 * pages that are served outside the application shell. In-application navigation is what the
 * navigation family is for.
 */
export function TextLink({
  children,
  href,
  external = false,
  quiet = false,
  className,
  ref,
  ...rest
}: TextLinkProps) {
  const intl = useIntl()

  return (
    <a
      {...rest}
      ref={ref}
      href={href}
      className={cx('text-link', className)}
      data-quiet={quiet ? 'true' : undefined}
      {...(external ? { target: '_blank', rel: 'noopener noreferrer' } : {})}
    >
      {children}
      {external ? (
        <>
          <Icon name="external-link" className="text-link__icon" />
          <span className="visually-hidden">
            {' '}
            {intl.formatMessage({ id: 'primitives.link.opensInNewTab' })}
          </span>
        </>
      ) : null}
    </a>
  )
}
