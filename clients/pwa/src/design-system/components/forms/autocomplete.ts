/**
 * The `autocomplete` tokens this product uses, named rather than typed out at each call site.
 *
 * WCAG 1.3.5 Identify Input Purpose is an AA criterion and the #50 blueprint restates it: "customer
 * fields carry `autocomplete` and `inputmode="tel"`". The reason is not conformance for its own
 * sake. At a busy counter the alternative to a filled field is a member of staff typing a phone
 * number one-handed with a garment in the other, and a mistyped phone number is a customer who
 * never receives the ready-for-delivery message.
 *
 * `oneTimeCode` additionally carries the 3.3.8 promise: the field accepts paste and the device
 * offers the incoming code above the keyboard, which is what makes an accessible sign-in possible
 * without a puzzle (docs/nfr/accessibility-localisation.md section 4.5, checklist item A11Y-82).
 *
 * The values are the HTML tokens verbatim. They are not translated and never will be: the browser
 * matches on the token, not on the label beside it.
 */
export const AUTOCOMPLETE = {
  /** A person's full name as they give it. */
  name: 'name',
  /** The Latin-script given name, where a form splits the name. */
  givenName: 'given-name',
  /** The Latin-script family name. */
  familyName: 'family-name',
  /** A phone number in full. Pair it with `inputMode: 'tel'`. */
  tel: 'tel',
  /** The national part of a phone number, for a form that holds the country code separately. */
  telNational: 'tel-national',
  email: 'email',
  streetAddress: 'street-address',
  addressLevel1: 'address-level1',
  addressLevel2: 'address-level2',
  postalCode: 'postal-code',
  country: 'country',
  /** A date of birth, for the kids templates' age band. */
  birthDate: 'bday',
  organisation: 'organization',
  /**
   * The sign-in name field. Paired with `currentPassword` below, it is what lets a password manager
   * fill a sign-in in one action — which on a shared counter device is the difference between a
   * strong password and one somebody can remember while holding a garment.
   */
  username: 'username',
  /** The password field of a sign-in and of every step-up re-authentication. */
  currentPassword: 'current-password',
  /** The password field of a recovery or a change: it asks the manager to offer a new one. */
  newPassword: 'new-password',
  /** The one-time code field of sign-in and of every step-up re-authentication. */
  oneTimeCode: 'one-time-code',
  /** Explicitly off, for a field a browser must not remember — a reason, a note, a variance. */
  off: 'off',
} as const

export type AutocompleteToken = (typeof AUTOCOMPLETE)[keyof typeof AUTOCOMPLETE]
