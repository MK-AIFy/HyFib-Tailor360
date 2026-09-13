/**
 * The permission keys the billing screens ask for, named once.
 *
 * The same strings the server's catalogue declares (`docs/security/permission-matrix.md` section
 * "matrix:permissions"). A screen never decides anything from them — the server re-checks every
 * request — but naming them here means a rename on the server is one edit rather than a search
 * through the routes.
 *
 * Four of these carry step-up on the server (`allocateAdvanceManual`, `approveReconciliation`,
 * `approveDispatchException`) or multi-factor alone (`cashierSession`); the matrix is authoritative
 * and this file does not repeat it.
 */
export const BILLING_PERMISSIONS = {
  /**
   * Recording a payment, reading one, and reading the branch's payment modes and an order's
   * balance — `GetOrderBalance` is read behind this key, not a Billing one.
   */
  recordPayment: 'payments.record',
  /**
   * Moving part of a held advance to a posted invoice by hand, against the automatic rule. Step-up
   * and a reason.
   */
  allocateAdvanceManual: 'payments.allocate_manual',
  /** Opening, reading and closing the branch's cashier sessions. */
  cashierSession: 'payments.session',
  /** Approving a closed session's variance, by someone other than the cashier who closed it. */
  approveReconciliation: 'payments.approve_reconciliation',
  /**
   * Approving a single-use dispatch exception. The plan's interim position is the Owner only
   * (`raci.md` footnote (15), XQ-02).
   */
  approveDispatchException: 'billing.approve_dispatch_exception',
  /**
   * Drafting and reading invoices — the same key `ListInvoices` is read behind, since no narrower
   * read permission exists for it.
   */
  createInvoice: 'billing.create_invoice',
  /** Printing and downloading a receipt. */
  printReceipt: 'billing.print_receipt',
} as const
