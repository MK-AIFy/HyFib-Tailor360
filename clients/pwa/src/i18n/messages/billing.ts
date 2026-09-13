/**
 * Billing messages — taking a payment, outstanding balances, the cashier session,
 * reconciliation approval, dispatch exceptions and allocating an advance by hand (#161-#165).
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. The vocabulary
 * here is held back for native-speaker review with the rest of the shop-floor vocabulary —
 * every string is money, a legal document or an audited act, and a wrong word here is a wrong
 * receipt or a wrong approval.
 */
export const billingEn = {
  'billing.problem.noOpenSession': 'Open a cashier session before taking a payment.',
  'billing.problem.modeNotAvailable':
    'This branch does not take payments in that mode. Choose another.',
  'billing.problem.referenceRequired': 'This mode needs a reference. Enter it, then try again.',
  'billing.problem.referenceLooksLikeCard':
    'That reference reads as a card number, which is never stored. Enter the last four digits or the transaction reference instead.',
  'billing.problem.referenceDuplicated':
    'This mode and reference were already used for another payment.',
  'billing.problem.orderAtAnotherBranch': 'This order belongs to another branch.',
  'billing.problem.orderNotKnown': 'No order matches that reference.',
  'billing.problem.orderNotConfirmed':
    'This order is not yet confirmed, so nothing can be billed against it.',
  'billing.problem.orderCancelled': 'This order was cancelled.',
  'billing.problem.noAdvanceHeld': 'This payment holds no advance to allocate.',
  'billing.problem.allocationExceedsInvoice': 'That is more than the invoice still owes.',
  'billing.problem.allocationInvoiceNotOfOrder':
    'That invoice does not belong to the order this payment was taken against.',
  'billing.problem.invoiceNotFound': 'No invoice matches that reference.',
  'billing.problem.sessionNotYours': 'Only the cashier who opened this session may close it.',
  'billing.problem.sessionAlreadyClosed': 'This session is already closed.',
  'billing.problem.sessionAlreadyOpen': 'You already have a session open at this branch.',
  'billing.problem.varianceReasonRequired':
    'The count is outside what closes without a reason. Give one, then close again.',
  'billing.problem.reconciliationSameUser':
    'The cashier who closed this session cannot approve its own variance. Ask someone else.',
  'billing.problem.reconciliationNotRequired':
    'This session’s variance never went beyond the threshold that needs approval.',
  'billing.problem.reconciliationAlreadyApproved': 'This variance was already approved.',
  'billing.problem.dispatchBalanceExceeded':
    'The amount named is more than this order’s dispatch policy allows for an exception.',
  'billing.problem.dispatchJobNotOfOrder':
    'One of those jobs does not belong to this order, or is not a live job.',
  'billing.problem.dispatchAmountNotPositive': 'The amount must be more than zero.',
  'billing.payment.title': 'Take a payment',
  'billing.payment.body':
    'Record what the customer is paying against this order. It is taken in your open cashier session and receipted at once.',
  'billing.payment.order': 'Order {orderNumber}',
  'billing.payment.balance.loading': 'the order’s balance',
  'billing.payment.balance.outstanding': 'Outstanding: {amount}',
  'billing.payment.balance.advanceHeld': '{amount} already held as an advance on this order',
  'billing.payment.balance.none': 'Nothing is outstanding on this order.',
  'billing.payment.amount.label': 'Amount',
  'billing.payment.amount.hint':
    'Whatever is paid beyond the balance is held as an advance and applied to the next invoice.',
  'billing.payment.amount.required': 'Enter an amount before recording the payment.',
  'billing.payment.amount.notPositive': 'The amount must be more than zero.',
  'billing.payment.quickfill.balance': 'Full balance ({amount})',
  'billing.payment.quickfill.advance': 'Advance',
  'billing.payment.quickfill.advance.description':
    'Clears the amount so you can type what the customer is paying ahead of the balance.',
  'billing.payment.mode.label': 'Payment mode',
  'billing.payment.mode.loading': 'the payment modes this branch takes',
  'billing.payment.mode.empty.title': 'No payment mode is set up for this branch',
  'billing.payment.mode.empty':
    'An administrator configures which payment modes a branch may take money in.',
  'billing.payment.reference.label': 'Reference',
  'billing.payment.reference.hint':
    'The last four digits of a card, or the UPI or bank reference — never the full card number.',
  'billing.payment.reference.required': 'This mode needs a reference.',
  'billing.payment.tendered.label': 'Cash tendered',
  'billing.payment.tendered.hint':
    'What the customer physically handed over. Used only to work out the change — it is not sent anywhere.',
  'billing.payment.change': 'Change to give: {amount}',
  'billing.payment.change.short': '{amount} short of the amount being recorded',
  'billing.payment.confirm.title': 'Record this payment?',
  'billing.payment.confirm.body':
    '{amount} in {mode} against order {orderNumber}. A receipt is issued at once and cannot be undone by editing — only by a reversal.',
  'billing.payment.confirm.action': 'Record payment',
  'billing.payment.recording': 'Recording…',
  'billing.payment.record': 'Record payment',
  'billing.payment.recorded.title': 'Payment recorded',
  'billing.payment.recorded.body': 'Receipt {receiptNumber} for {amount}.',
  'billing.payment.recorded.another': 'Take another payment',
  'billing.payment.offlineAction': 'Recording a payment',
  'billing.payment.receipt.title': 'Receipt',
  'billing.payment.receipt.summary': '{receiptNumber} — {amount}, issued {date}',
  'billing.payment.receipt.allocated': '{amount} applied to this order’s posted invoices',
  'billing.payment.receipt.advance': '{amount} held as an advance',
  'billing.payment.receipt.allocateNow': 'Allocate this advance now',
  'billing.payment.receipt.outstanding': '{amount} still outstanding on this order',
  'billing.payment.receipt.print': 'Send to the print queue',
  'billing.payment.receipt.printing': 'Sending…',
  'billing.payment.receipt.printed':
    'Sent to the branch’s print queue as job {jobId}. Nothing prints yet — the print bridge is a later change.',
  'billing.payment.receipt.share.title': 'Share with the customer',
  'billing.payment.receipt.share.body':
    'A customer link for a receipt does not exist yet — estimate, status and feedback are the only purposes the link service supports today. Read the receipt number aloud, or hand over the printed copy.',
  'billing.payment.receipt.share.inApp': 'In-app reference: {receiptNumber}',
  'billing.outstanding.title': 'Outstanding balances',
  'billing.outstanding.body':
    'Every posted invoice at this branch with money still owed against it.',
  'billing.outstanding.loading': 'the branch’s outstanding balances',
  'billing.outstanding.empty.title': 'Nothing outstanding',
  'billing.outstanding.empty': 'Every posted invoice at this branch is paid in full.',
  'billing.outstanding.caption': 'Posted invoices with an outstanding balance',
  'billing.outstanding.column.order': 'Order',
  'billing.outstanding.column.customer': 'Customer',
  'billing.outstanding.column.invoice': 'Invoice',
  'billing.outstanding.column.total': 'Invoice total',
  'billing.outstanding.column.outstanding': 'Outstanding',
  'billing.outstanding.takePayment': 'Take payment',
  'billing.outstanding.takePayment.label': 'Take payment for order {order}',
  'billing.outstanding.loadMore': 'Show more',
  'billing.cashier.title': 'Cashier session',
  'billing.cashier.loading': 'your cashier session',
  'billing.cashier.open.title': 'Open a session',
  'billing.cashier.open.body':
    'Count the float before it goes in the drawer. Every payment you record from here on is recorded in this session, until you close it.',
  'billing.cashier.open.float.label': 'Opening float',
  'billing.cashier.open.action': 'Open session',
  'billing.cashier.opening': 'Opening…',
  'billing.cashier.open.offlineAction': 'Opening a cashier session',
  'billing.cashier.status.open': 'Open since {date}',
  'billing.cashier.status.closed': 'Closed {date}',
  'billing.cashier.status.expected': 'Expected: {amount}',
  'billing.cashier.close.title': 'Close the session',
  'billing.cashier.close.body':
    'Count every note and every coin, and enter what each other payment mode counted to. The session closes against what you enter here, and it cannot be recorded twice.',
  'billing.cashier.close.notes': 'Notes',
  'billing.cashier.close.note.label': '₹{denomination} notes',
  'billing.cashier.close.coins': 'Coins',
  'billing.cashier.close.coin.label': '₹{denomination} coins',
  'billing.cashier.close.cashTotal': 'Cash counted: {amount}',
  'billing.cashier.close.modeTotals': 'Other payment modes',
  'billing.cashier.close.mode.label': '{mode} counted',
  'billing.cashier.close.expected': 'Expected: {amount}',
  'billing.cashier.close.counted': 'Counted: {amount}',
  'billing.cashier.close.variance': 'Variance: {amount}',
  'billing.cashier.close.variance.over': '{amount} over',
  'billing.cashier.close.variance.short': '{amount} short',
  'billing.cashier.close.variance.none': 'Matches exactly',
  'billing.cashier.close.reason.label': 'Reason for the variance',
  'billing.cashier.close.reason.hint':
    'Required because the variance is outside what closes without one.',
  'billing.cashier.close.confirm.title': 'Close this session?',
  'billing.cashier.close.confirm.body':
    'The count becomes the record of today’s takings. It cannot be recorded twice — closing it again is refused, not repeated.',
  'billing.cashier.close.confirm.action': 'Close session',
  'billing.cashier.closing': 'Closing…',
  'billing.cashier.close.action': 'Close session',
  'billing.cashier.closed.title': 'Session closed',
  'billing.cashier.closed.body': '{counted} counted against {expected} expected.',
  'billing.cashier.closed.varianceNotice':
    'The variance is outside the threshold and needs another person’s approval before it is reconciled.',
  'billing.cashier.close.offlineAction': 'Closing the cashier session',
  'billing.cashier.none.title': 'No session open',
  'billing.cashier.none.body': 'Nothing can be taken at the counter until a session is opened.',
  'billing.reconciliation.title': 'Approve a session’s variance',
  'billing.reconciliation.loading': 'the closed session',
  'billing.reconciliation.body':
    'This session closed {amount} away from what was expected, which is beyond what closes without review.',
  'billing.reconciliation.notRequired.title': 'Nothing to approve',
  'billing.reconciliation.notRequired':
    'This session’s variance never went beyond the threshold that needs approval.',
  'billing.reconciliation.reason.label': 'Reason for approving',
  'billing.reconciliation.reason.hint':
    'Required because approving a variance is a step-up action, recorded on the audit trail.',
  'billing.reconciliation.confirm.title': 'Approve this variance?',
  'billing.reconciliation.confirm.body':
    'This closes the review of the session’s variance. It cannot be approved twice.',
  'billing.reconciliation.confirm.action': 'Approve variance',
  'billing.reconciliation.approving': 'Approving…',
  'billing.reconciliation.approve': 'Approve variance',
  'billing.reconciliation.approved.title': 'Variance approved',
  'billing.reconciliation.approved.body': 'Recorded against the session.',
  'billing.reconciliation.offlineAction': 'Approving a session’s variance',
  'billing.reconciliation.modeline': '{mode}: {variance}',
  'billing.dispatch.title': 'Approve a dispatch exception',
  'billing.dispatch.body':
    'Lets the named garment jobs of an order leave the branch despite an outstanding balance, once only and until it expires. Reserved for the Owner.',
  'billing.dispatch.order.label': 'Order reference',
  'billing.dispatch.order.hint': 'The order the exception applies to.',
  'billing.dispatch.order.required': 'Name the order before reading its balance.',
  'billing.dispatch.balance.loading': 'the order’s balance',
  'billing.dispatch.balance.outstanding': '{amount} outstanding on this order',
  'billing.dispatch.jobs.label': 'Garment jobs',
  'billing.dispatch.jobs.hint':
    'One job reference per line. Every job named must belong to this order and still be live.',
  'billing.dispatch.jobs.required': 'Name at least one garment job.',
  'billing.dispatch.maxOutstanding.label': 'Maximum outstanding allowed',
  'billing.dispatch.maxOutstanding.hint':
    'Pre-filled from the order’s current balance. The exception is refused if the balance is higher than this when it is used.',
  'billing.dispatch.reasonCode.label': 'Reason code',
  'billing.dispatch.reasonCode.hint':
    'A short code for the audit trail and for reporting — CUSTOMER_TRAVELLING, for instance.',
  'billing.dispatch.reasonText.label': 'Reason',
  'billing.dispatch.reasonText.hint': 'In full, for whoever reviews the audit trail.',
  'billing.dispatch.expiry.label': 'Hours until it expires',
  'billing.dispatch.expiry.hint':
    'At most 72 hours. The exception is refused if it is not used before then.',
  'billing.dispatch.confirm.title': 'Approve this exception?',
  'billing.dispatch.confirm.body':
    'The named jobs may be dispatched once, with up to {amount} outstanding, until {expiry}. It cannot be recalled once it is used.',
  'billing.dispatch.confirm.action': 'Approve exception',
  'billing.dispatch.approving': 'Approving…',
  'billing.dispatch.approve': 'Approve exception',
  'billing.dispatch.approved.title': 'Exception approved',
  'billing.dispatch.approved.body':
    'Valid until {expiry}, for up to {amount} outstanding. It is consumed the first time it is used and cannot be reused.',
  'billing.dispatch.approved.another': 'Approve another exception',
  'billing.dispatch.offlineAction': 'Approving a dispatch exception',
  'billing.allocate.title': 'Allocate an advance',
  'billing.allocate.body':
    'Move part of this payment’s held advance to a posted invoice of the same order, by hand and against the automatic rule.',
  'billing.allocate.loading': 'the payment and the order’s invoices',
  'billing.allocate.held': '{amount} held, unapplied',
  'billing.allocate.none.title': 'Nothing held',
  'billing.allocate.none': 'This payment holds no advance to allocate.',
  'billing.allocate.invoice.label': 'Invoice',
  'billing.allocate.invoice.choose': 'Choose an invoice',
  'billing.allocate.invoice.option': '{invoiceNumber} — {outstanding} outstanding',
  'billing.allocate.invoice.required': 'Choose an invoice.',
  'billing.allocate.invoice.none.title': 'No posted invoice to allocate against',
  'billing.allocate.invoice.none':
    'The order this payment was taken against has no other posted invoice with a balance still owing.',
  'billing.allocate.amount.label': 'Amount to allocate',
  'billing.allocate.amount.hint':
    'Never more than the advance still holds, and never more than the invoice still owes.',
  'billing.allocate.amount.required': 'Enter an amount before allocating.',
  'billing.allocate.reason.label': 'Reason',
  'billing.allocate.reason.hint': 'Required because this moves money against the automatic rule.',
  'billing.allocate.confirm.title': 'Allocate this advance?',
  'billing.allocate.confirm.body':
    '{amount} moves from the held advance to invoice {invoiceNumber}. It cannot be undone by editing — only by a fresh allocation or a reversal.',
  'billing.allocate.confirm.action': 'Allocate',
  'billing.allocate.allocating': 'Allocating…',
  'billing.allocate.allocate': 'Allocate',
  'billing.allocate.allocated.title': 'Advance allocated',
  'billing.allocate.allocated.body':
    '{amount} applied to invoice {invoiceNumber}. {remaining} remains held.',
  'billing.allocate.offlineAction': 'Allocating an advance',
} as const

export const billingTa: Record<keyof typeof billingEn, string> = {
  // not translated — awaiting native-speaker review
  'billing.problem.noOpenSession': 'Open a cashier session before taking a payment.',
  // not translated — awaiting native-speaker review
  'billing.problem.modeNotAvailable':
    'This branch does not take payments in that mode. Choose another.',
  // not translated — awaiting native-speaker review
  'billing.problem.referenceRequired': 'This mode needs a reference. Enter it, then try again.',
  // not translated — awaiting native-speaker review
  'billing.problem.referenceLooksLikeCard':
    'That reference reads as a card number, which is never stored. Enter the last four digits or the transaction reference instead.',
  // not translated — awaiting native-speaker review
  'billing.problem.referenceDuplicated':
    'This mode and reference were already used for another payment.',
  // not translated — awaiting native-speaker review
  'billing.problem.orderAtAnotherBranch': 'This order belongs to another branch.',
  // not translated — awaiting native-speaker review
  'billing.problem.orderNotKnown': 'No order matches that reference.',
  // not translated — awaiting native-speaker review
  'billing.problem.orderNotConfirmed':
    'This order is not yet confirmed, so nothing can be billed against it.',
  // not translated — awaiting native-speaker review
  'billing.problem.orderCancelled': 'This order was cancelled.',
  // not translated — awaiting native-speaker review
  'billing.problem.noAdvanceHeld': 'This payment holds no advance to allocate.',
  // not translated — awaiting native-speaker review
  'billing.problem.allocationExceedsInvoice': 'That is more than the invoice still owes.',
  // not translated — awaiting native-speaker review
  'billing.problem.allocationInvoiceNotOfOrder':
    'That invoice does not belong to the order this payment was taken against.',
  // not translated — awaiting native-speaker review
  'billing.problem.invoiceNotFound': 'No invoice matches that reference.',
  // not translated — awaiting native-speaker review
  'billing.problem.sessionNotYours': 'Only the cashier who opened this session may close it.',
  // not translated — awaiting native-speaker review
  'billing.problem.sessionAlreadyClosed': 'This session is already closed.',
  // not translated — awaiting native-speaker review
  'billing.problem.sessionAlreadyOpen': 'You already have a session open at this branch.',
  // not translated — awaiting native-speaker review
  'billing.problem.varianceReasonRequired':
    'The count is outside what closes without a reason. Give one, then close again.',
  // not translated — awaiting native-speaker review
  'billing.problem.reconciliationSameUser':
    'The cashier who closed this session cannot approve its own variance. Ask someone else.',
  // not translated — awaiting native-speaker review
  'billing.problem.reconciliationNotRequired':
    'This session’s variance never went beyond the threshold that needs approval.',
  // not translated — awaiting native-speaker review
  'billing.problem.reconciliationAlreadyApproved': 'This variance was already approved.',
  // not translated — awaiting native-speaker review
  'billing.problem.dispatchBalanceExceeded':
    'The amount named is more than this order’s dispatch policy allows for an exception.',
  // not translated — awaiting native-speaker review
  'billing.problem.dispatchJobNotOfOrder':
    'One of those jobs does not belong to this order, or is not a live job.',
  // not translated — awaiting native-speaker review
  'billing.problem.dispatchAmountNotPositive': 'The amount must be more than zero.',
  // not translated — awaiting native-speaker review
  'billing.payment.title': 'Take a payment',
  // not translated — awaiting native-speaker review
  'billing.payment.body':
    'Record what the customer is paying against this order. It is taken in your open cashier session and receipted at once.',
  // not translated — awaiting native-speaker review
  'billing.payment.order': 'Order {orderNumber}',
  // not translated — awaiting native-speaker review
  'billing.payment.balance.loading': 'the order’s balance',
  // not translated — awaiting native-speaker review
  'billing.payment.balance.outstanding': 'Outstanding: {amount}',
  // not translated — awaiting native-speaker review
  'billing.payment.balance.advanceHeld': '{amount} already held as an advance on this order',
  // not translated — awaiting native-speaker review
  'billing.payment.balance.none': 'Nothing is outstanding on this order.',
  // not translated — awaiting native-speaker review
  'billing.payment.amount.label': 'Amount',
  // not translated — awaiting native-speaker review
  'billing.payment.amount.hint':
    'Whatever is paid beyond the balance is held as an advance and applied to the next invoice.',
  // not translated — awaiting native-speaker review
  'billing.payment.amount.required': 'Enter an amount before recording the payment.',
  // not translated — awaiting native-speaker review
  'billing.payment.amount.notPositive': 'The amount must be more than zero.',
  // not translated — awaiting native-speaker review
  'billing.payment.quickfill.balance': 'Full balance ({amount})',
  // not translated — awaiting native-speaker review
  'billing.payment.quickfill.advance': 'Advance',
  // not translated — awaiting native-speaker review
  'billing.payment.quickfill.advance.description':
    'Clears the amount so you can type what the customer is paying ahead of the balance.',
  // not translated — awaiting native-speaker review
  'billing.payment.mode.label': 'Payment mode',
  // not translated — awaiting native-speaker review
  'billing.payment.mode.loading': 'the payment modes this branch takes',
  // not translated — awaiting native-speaker review
  'billing.payment.mode.empty.title': 'No payment mode is set up for this branch',
  // not translated — awaiting native-speaker review
  'billing.payment.mode.empty':
    'An administrator configures which payment modes a branch may take money in.',
  // not translated — awaiting native-speaker review
  'billing.payment.reference.label': 'Reference',
  // not translated — awaiting native-speaker review
  'billing.payment.reference.hint':
    'The last four digits of a card, or the UPI or bank reference — never the full card number.',
  // not translated — awaiting native-speaker review
  'billing.payment.reference.required': 'This mode needs a reference.',
  // not translated — awaiting native-speaker review
  'billing.payment.tendered.label': 'Cash tendered',
  // not translated — awaiting native-speaker review
  'billing.payment.tendered.hint':
    'What the customer physically handed over. Used only to work out the change — it is not sent anywhere.',
  // not translated — awaiting native-speaker review
  'billing.payment.change': 'Change to give: {amount}',
  // not translated — awaiting native-speaker review
  'billing.payment.change.short': '{amount} short of the amount being recorded',
  // not translated — awaiting native-speaker review
  'billing.payment.confirm.title': 'Record this payment?',
  // not translated — awaiting native-speaker review
  'billing.payment.confirm.body':
    '{amount} in {mode} against order {orderNumber}. A receipt is issued at once and cannot be undone by editing — only by a reversal.',
  // not translated — awaiting native-speaker review
  'billing.payment.confirm.action': 'Record payment',
  // not translated — awaiting native-speaker review
  'billing.payment.recording': 'Recording…',
  // not translated — awaiting native-speaker review
  'billing.payment.record': 'Record payment',
  // not translated — awaiting native-speaker review
  'billing.payment.recorded.title': 'Payment recorded',
  // not translated — awaiting native-speaker review
  'billing.payment.recorded.body': 'Receipt {receiptNumber} for {amount}.',
  // not translated — awaiting native-speaker review
  'billing.payment.recorded.another': 'Take another payment',
  // not translated — awaiting native-speaker review
  'billing.payment.offlineAction': 'Recording a payment',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.title': 'Receipt',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.summary': '{receiptNumber} — {amount}, issued {date}',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.allocated': '{amount} applied to this order’s posted invoices',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.advance': '{amount} held as an advance',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.allocateNow': 'Allocate this advance now',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.outstanding': '{amount} still outstanding on this order',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.print': 'Send to the print queue',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.printing': 'Sending…',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.printed':
    'Sent to the branch’s print queue as job {jobId}. Nothing prints yet — the print bridge is a later change.',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.share.title': 'Share with the customer',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.share.body':
    'A customer link for a receipt does not exist yet — estimate, status and feedback are the only purposes the link service supports today. Read the receipt number aloud, or hand over the printed copy.',
  // not translated — awaiting native-speaker review
  'billing.payment.receipt.share.inApp': 'In-app reference: {receiptNumber}',
  // not translated — awaiting native-speaker review
  'billing.outstanding.title': 'Outstanding balances',
  // not translated — awaiting native-speaker review
  'billing.outstanding.body':
    'Every posted invoice at this branch with money still owed against it.',
  // not translated — awaiting native-speaker review
  'billing.outstanding.loading': 'the branch’s outstanding balances',
  // not translated — awaiting native-speaker review
  'billing.outstanding.empty.title': 'Nothing outstanding',
  // not translated — awaiting native-speaker review
  'billing.outstanding.empty': 'Every posted invoice at this branch is paid in full.',
  // not translated — awaiting native-speaker review
  'billing.outstanding.caption': 'Posted invoices with an outstanding balance',
  // not translated — awaiting native-speaker review
  'billing.outstanding.column.order': 'Order',
  // not translated — awaiting native-speaker review
  'billing.outstanding.column.customer': 'Customer',
  // not translated — awaiting native-speaker review
  'billing.outstanding.column.invoice': 'Invoice',
  // not translated — awaiting native-speaker review
  'billing.outstanding.column.total': 'Invoice total',
  // not translated — awaiting native-speaker review
  'billing.outstanding.column.outstanding': 'Outstanding',
  // not translated — awaiting native-speaker review
  'billing.outstanding.takePayment': 'Take payment',
  // not translated — awaiting native-speaker review
  'billing.outstanding.takePayment.label': 'Take payment for order {order}',
  // not translated — awaiting native-speaker review
  'billing.outstanding.loadMore': 'Show more',
  // not translated — awaiting native-speaker review
  'billing.cashier.title': 'Cashier session',
  // not translated — awaiting native-speaker review
  'billing.cashier.loading': 'your cashier session',
  // not translated — awaiting native-speaker review
  'billing.cashier.open.title': 'Open a session',
  // not translated — awaiting native-speaker review
  'billing.cashier.open.body':
    'Count the float before it goes in the drawer. Every payment you record from here on is recorded in this session, until you close it.',
  // not translated — awaiting native-speaker review
  'billing.cashier.open.float.label': 'Opening float',
  // not translated — awaiting native-speaker review
  'billing.cashier.open.action': 'Open session',
  // not translated — awaiting native-speaker review
  'billing.cashier.opening': 'Opening…',
  // not translated — awaiting native-speaker review
  'billing.cashier.open.offlineAction': 'Opening a cashier session',
  // not translated — awaiting native-speaker review
  'billing.cashier.status.open': 'Open since {date}',
  // not translated — awaiting native-speaker review
  'billing.cashier.status.closed': 'Closed {date}',
  // not translated — awaiting native-speaker review
  'billing.cashier.status.expected': 'Expected: {amount}',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.title': 'Close the session',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.body':
    'Count every note and every coin, and enter what each other payment mode counted to. The session closes against what you enter here, and it cannot be recorded twice.',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.notes': 'Notes',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.note.label': '₹{denomination} notes',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.coins': 'Coins',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.coin.label': '₹{denomination} coins',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.cashTotal': 'Cash counted: {amount}',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.modeTotals': 'Other payment modes',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.mode.label': '{mode} counted',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.expected': 'Expected: {amount}',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.counted': 'Counted: {amount}',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.variance': 'Variance: {amount}',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.variance.over': '{amount} over',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.variance.short': '{amount} short',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.variance.none': 'Matches exactly',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.reason.label': 'Reason for the variance',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.reason.hint':
    'Required because the variance is outside what closes without one.',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.confirm.title': 'Close this session?',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.confirm.body':
    'The count becomes the record of today’s takings. It cannot be recorded twice — closing it again is refused, not repeated.',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.confirm.action': 'Close session',
  // not translated — awaiting native-speaker review
  'billing.cashier.closing': 'Closing…',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.action': 'Close session',
  // not translated — awaiting native-speaker review
  'billing.cashier.closed.title': 'Session closed',
  // not translated — awaiting native-speaker review
  'billing.cashier.closed.body': '{counted} counted against {expected} expected.',
  // not translated — awaiting native-speaker review
  'billing.cashier.closed.varianceNotice':
    'The variance is outside the threshold and needs another person’s approval before it is reconciled.',
  // not translated — awaiting native-speaker review
  'billing.cashier.close.offlineAction': 'Closing the cashier session',
  // not translated — awaiting native-speaker review
  'billing.cashier.none.title': 'No session open',
  // not translated — awaiting native-speaker review
  'billing.cashier.none.body': 'Nothing can be taken at the counter until a session is opened.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.title': 'Approve a session’s variance',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.loading': 'the closed session',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.body':
    'This session closed {amount} away from what was expected, which is beyond what closes without review.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.notRequired.title': 'Nothing to approve',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.notRequired':
    'This session’s variance never went beyond the threshold that needs approval.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.reason.label': 'Reason for approving',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.reason.hint':
    'Required because approving a variance is a step-up action, recorded on the audit trail.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.confirm.title': 'Approve this variance?',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.confirm.body':
    'This closes the review of the session’s variance. It cannot be approved twice.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.confirm.action': 'Approve variance',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.approving': 'Approving…',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.approve': 'Approve variance',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.approved.title': 'Variance approved',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.approved.body': 'Recorded against the session.',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.offlineAction': 'Approving a session’s variance',
  // not translated — awaiting native-speaker review
  'billing.reconciliation.modeline': '{mode}: {variance}',
  // not translated — awaiting native-speaker review
  'billing.dispatch.title': 'Approve a dispatch exception',
  // not translated — awaiting native-speaker review
  'billing.dispatch.body':
    'Lets the named garment jobs of an order leave the branch despite an outstanding balance, once only and until it expires. Reserved for the Owner.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.order.label': 'Order reference',
  // not translated — awaiting native-speaker review
  'billing.dispatch.order.hint': 'The order the exception applies to.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.order.required': 'Name the order before reading its balance.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.balance.loading': 'the order’s balance',
  // not translated — awaiting native-speaker review
  'billing.dispatch.balance.outstanding': '{amount} outstanding on this order',
  // not translated — awaiting native-speaker review
  'billing.dispatch.jobs.label': 'Garment jobs',
  // not translated — awaiting native-speaker review
  'billing.dispatch.jobs.hint':
    'One job reference per line. Every job named must belong to this order and still be live.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.jobs.required': 'Name at least one garment job.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.maxOutstanding.label': 'Maximum outstanding allowed',
  // not translated — awaiting native-speaker review
  'billing.dispatch.maxOutstanding.hint':
    'Pre-filled from the order’s current balance. The exception is refused if the balance is higher than this when it is used.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.reasonCode.label': 'Reason code',
  // not translated — awaiting native-speaker review
  'billing.dispatch.reasonCode.hint':
    'A short code for the audit trail and for reporting — CUSTOMER_TRAVELLING, for instance.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.reasonText.label': 'Reason',
  // not translated — awaiting native-speaker review
  'billing.dispatch.reasonText.hint': 'In full, for whoever reviews the audit trail.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.expiry.label': 'Hours until it expires',
  // not translated — awaiting native-speaker review
  'billing.dispatch.expiry.hint':
    'At most 72 hours. The exception is refused if it is not used before then.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.confirm.title': 'Approve this exception?',
  // not translated — awaiting native-speaker review
  'billing.dispatch.confirm.body':
    'The named jobs may be dispatched once, with up to {amount} outstanding, until {expiry}. It cannot be recalled once it is used.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.confirm.action': 'Approve exception',
  // not translated — awaiting native-speaker review
  'billing.dispatch.approving': 'Approving…',
  // not translated — awaiting native-speaker review
  'billing.dispatch.approve': 'Approve exception',
  // not translated — awaiting native-speaker review
  'billing.dispatch.approved.title': 'Exception approved',
  // not translated — awaiting native-speaker review
  'billing.dispatch.approved.body':
    'Valid until {expiry}, for up to {amount} outstanding. It is consumed the first time it is used and cannot be reused.',
  // not translated — awaiting native-speaker review
  'billing.dispatch.approved.another': 'Approve another exception',
  // not translated — awaiting native-speaker review
  'billing.dispatch.offlineAction': 'Approving a dispatch exception',
  // not translated — awaiting native-speaker review
  'billing.allocate.title': 'Allocate an advance',
  // not translated — awaiting native-speaker review
  'billing.allocate.body':
    'Move part of this payment’s held advance to a posted invoice of the same order, by hand and against the automatic rule.',
  // not translated — awaiting native-speaker review
  'billing.allocate.loading': 'the payment and the order’s invoices',
  // not translated — awaiting native-speaker review
  'billing.allocate.held': '{amount} held, unapplied',
  // not translated — awaiting native-speaker review
  'billing.allocate.none.title': 'Nothing held',
  // not translated — awaiting native-speaker review
  'billing.allocate.none': 'This payment holds no advance to allocate.',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.label': 'Invoice',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.choose': 'Choose an invoice',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.option': '{invoiceNumber} — {outstanding} outstanding',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.required': 'Choose an invoice.',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.none.title': 'No posted invoice to allocate against',
  // not translated — awaiting native-speaker review
  'billing.allocate.invoice.none':
    'The order this payment was taken against has no other posted invoice with a balance still owing.',
  // not translated — awaiting native-speaker review
  'billing.allocate.amount.label': 'Amount to allocate',
  // not translated — awaiting native-speaker review
  'billing.allocate.amount.hint':
    'Never more than the advance still holds, and never more than the invoice still owes.',
  // not translated — awaiting native-speaker review
  'billing.allocate.amount.required': 'Enter an amount before allocating.',
  // not translated — awaiting native-speaker review
  'billing.allocate.reason.label': 'Reason',
  // not translated — awaiting native-speaker review
  'billing.allocate.reason.hint': 'Required because this moves money against the automatic rule.',
  // not translated — awaiting native-speaker review
  'billing.allocate.confirm.title': 'Allocate this advance?',
  // not translated — awaiting native-speaker review
  'billing.allocate.confirm.body':
    '{amount} moves from the held advance to invoice {invoiceNumber}. It cannot be undone by editing — only by a fresh allocation or a reversal.',
  // not translated — awaiting native-speaker review
  'billing.allocate.confirm.action': 'Allocate',
  // not translated — awaiting native-speaker review
  'billing.allocate.allocating': 'Allocating…',
  // not translated — awaiting native-speaker review
  'billing.allocate.allocate': 'Allocate',
  // not translated — awaiting native-speaker review
  'billing.allocate.allocated.title': 'Advance allocated',
  // not translated — awaiting native-speaker review
  'billing.allocate.allocated.body':
    '{amount} applied to invoice {invoiceNumber}. {remaining} remains held.',
  // not translated — awaiting native-speaker review
  'billing.allocate.offlineAction': 'Allocating an advance',
}
