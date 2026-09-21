/**
 * The permission keys the customer screens ask for, named once.
 *
 * The same strings the server's catalogue declares (`docs/security/permission-matrix.md` section 4).
 * A screen never decides anything from them — the server re-checks every request — but naming them
 * here means a rename on the server is one edit rather than a search through the routes.
 */
export const CUSTOMERS_PERMISSIONS = {
  /** Finding a customer, reading one record, and reading the duplicate-candidate list. */
  read: 'customers.read',
  /** Reading the six contact fields, where the caller may. Gates nothing on this screen by itself. */
  readContact: 'customers.read_contact',
  /** Creating a customer record at the branch the caller is working in. */
  create: 'customers.create',
  /** Correcting what a customer record says about the person. */
  update: 'customers.update',
  /** Folding one customer record into another. Irreversible, and needs a fresh proof of identity. */
  merge: 'customers.merge',
  /** Reading a customer's consent record and her communication preferences. */
  readConsent: 'customers.read_consent',
} as const
