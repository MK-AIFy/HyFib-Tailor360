/**
 * The permission keys the measurement screens ask for, named once.
 *
 * The same strings the server's catalogue declares (`docs/security/permission-matrix.md` section 4).
 * A screen never decides anything from them — the server re-checks every request — but naming them
 * here means a rename on the server is one edit rather than a search through the routes.
 */
export const MEASUREMENT_PERMISSIONS = {
  /** Starting, saving and confirming a draft, and reading the template it is pinned to. */
  capture: 'measurements.capture',
  /** Finding the customer to measure. Granted to every counter role that may capture. */
  customersRead: 'customers.read',
  /** Reading what the branch may order, which is where the template for a garment comes from. */
  catalogRead: 'catalog.read',
  /**
   * Reading and printing a measurement sheet. Narrower than capture on purpose: a sheet is the
   * widest audience a measurement gets, and the right to produce one is held by fewer people.
   */
  readSheet: 'measurements.read_sheet',
} as const
