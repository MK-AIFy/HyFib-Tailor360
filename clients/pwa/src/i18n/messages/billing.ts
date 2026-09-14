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
  'billing.outstanding.count':
    '{count, plural, one {# invoice outstanding} other {# invoices outstanding}}',
  'billing.outstanding.column.order': 'Order',
  'billing.outstanding.column.customer': 'Customer',
  'billing.outstanding.column.invoice': 'Invoice',
  'billing.outstanding.column.total': 'Invoice total',
  'billing.outstanding.column.outstanding': 'Outstanding',
  'billing.outstanding.takePayment': 'Take payment',
  'billing.outstanding.takePayment.label': 'Take payment for order {order}',
  'billing.outstanding.loadMore': 'Show more',
  'billing.problem.barcodeNotFound': 'No invoice matches that barcode.',
  'billing.invoices.status.draft': 'Draft',
  'billing.invoices.status.posted': 'Posted',
  'billing.invoices.status.discarded': 'Discarded',
  'billing.invoices.title': 'Invoices',
  'billing.invoices.loading': 'invoices',
  'billing.invoices.empty.title': 'No invoices at this branch yet',
  'billing.invoices.empty': 'Draft one from a confirmed order to see it here.',
  'billing.invoices.empty.filtered.title': 'No invoices match this filter',
  'billing.invoices.empty.filtered': 'Clear the status filter to see every invoice.',
  'billing.invoices.caption': "The branch's invoices, newest first",
  'billing.invoices.column.order': 'Order',
  'billing.invoices.column.invoice': 'Invoice',
  'billing.invoices.column.customer': 'Customer',
  'billing.invoices.column.total': 'Total',
  'billing.invoices.column.status': 'Status',
  'billing.invoices.column.created': 'Created',
  'billing.invoices.filter.status.label': 'Status',
  'billing.invoices.filter.status.all': 'All statuses',
  'billing.invoices.filter.applied': 'Status: {status}',
  'billing.invoices.loadMore': 'Show more',
  'billing.invoices.loadMore.offlineAction': 'Loading more invoices',
  'billing.invoices.open': 'Open',
  'billing.invoices.open.label': 'Open invoice {reference}',
  'billing.invoices.lookup.title': 'Find by barcode',
  'billing.invoices.lookup.label': 'Barcode',
  'billing.invoices.lookup.hint': 'Scan or type the I- payload printed on the invoice.',
  'billing.invoices.lookup.action': 'Find',
  'billing.invoices.lookup.finding': 'Finding…',
  'billing.invoices.lookup.required': 'Enter a barcode before searching.',
  'billing.invoices.lookup.offlineAction': 'Looking up a barcode',
  'billing.invoices.lookup.branchRequired':
    'Your session needs a branch before a barcode can be looked up.',
  'billing.invoice.title': 'Invoice',
  'billing.invoice.loading': 'the invoice',
  'billing.invoice.header.draft': 'Draft',
  'billing.invoice.header.order': 'Order {orderNumber}',
  'billing.invoice.header.financialYear': 'Financial year {year}',
  'billing.invoice.header.posted': 'Posted {date}',
  'billing.invoice.header.notPosted': 'Not yet posted',
  'billing.invoice.header.discarded': 'Discarded {date}',
  'billing.invoice.customer.title': 'Billed to',
  'billing.invoice.lines.caption': 'Invoice lines',
  'billing.invoice.lines.column.line': '#',
  'billing.invoice.lines.column.description': 'Description',
  'billing.invoice.lines.column.classification': 'HSN/SAC',
  'billing.invoice.lines.column.quantity': 'Qty',
  'billing.invoice.lines.column.rate': 'Rate',
  'billing.invoice.lines.column.discount': 'Discount',
  'billing.invoice.lines.column.taxableValue': 'Taxable value',
  'billing.invoice.lines.column.tax': 'Tax',
  'billing.invoice.lines.column.total': 'Line total',
  'billing.invoice.lines.discount.value': '{ruleCode} −{amount}',
  'billing.invoice.lines.discount.none': '—',
  'billing.invoice.lines.tax.component': '{kind} {rate}: {amount}',
  'billing.invoice.totals.title': 'Totals',
  'billing.invoice.totals.subtotal': 'Subtotal',
  'billing.invoice.totals.discountTotal': 'Discount',
  'billing.invoice.totals.taxableValue': 'Taxable value',
  'billing.invoice.totals.centralTax': 'CGST',
  'billing.invoice.totals.stateTax': 'SGST',
  'billing.invoice.totals.integratedTax': 'IGST',
  'billing.invoice.totals.cess': 'Cess',
  'billing.invoice.totals.roundOff': 'Round-off',
  'billing.invoice.totals.grandTotal': 'Grand total',
  'billing.invoice.cancelled.title': 'This invoice is cancelled',
  'billing.invoice.cancelled.body': 'Cancelled {date}. {reason}',
  'billing.invoice.notes.title': 'Credit and debit notes',
  'billing.invoice.notes.credit': 'Credit note {number}',
  'billing.invoice.notes.debit': 'Debit note {number}',
  'billing.invoice.notes.posted': 'Posted {date}',
  'billing.invoice.notes.reason': 'Reason: {reason}',
  'billing.invoice.notes.download': 'Download PDF',
  'billing.invoice.notes.download.label': 'Download {number} as a PDF',
  'billing.invoice.document.title.invoice': 'Tax invoice',
  'billing.invoice.document.title.credit': 'Credit note',
  'billing.invoice.document.title.debit': 'Debit note',
  'billing.invoice.document.issued': 'Date {date} · FY {year}',
  'billing.invoice.document.cancelledMark': 'CANCELLED',
  'billing.invoice.document.supplier.title': 'Supplier',
  'billing.invoice.document.supplier.gstin': 'GSTIN {gstin} · State {stateCode}',
  'billing.invoice.document.artefactNote':
    'The downloadable PDF is the artefact of record and carries the letterhead and the terms.',
  'billing.invoice.document.customer.number': 'Customer {number}',
  'billing.invoice.document.reference.order': 'Order',
  'billing.invoice.document.reference.note': 'Against invoice',
  'billing.invoice.document.reference.placeOfSupply': 'Place of supply {state} · {scheme}',
  'billing.invoice.document.line.itemAndClassification': '{itemCode} · HSN/SAC {classification}',
  'billing.invoice.document.line.surcharge': '+ {description} {amount}',
  'billing.invoice.document.balanceDue': 'Balance due',
  'billing.invoice.print.title': 'Print and download',
  'billing.invoice.print.page': 'Print this page',
  'billing.invoice.print.station': 'Send to print station',
  'billing.invoice.print.station.sending': 'Sending…',
  'billing.invoice.print.station.copies.label': 'Copies',
  'billing.invoice.print.station.copies.hint': 'One to five.',
  'billing.invoice.print.station.copies.outOfRange': 'Choose between 1 and 5 copies.',
  'billing.invoice.print.station.sent':
    'Sent to the branch’s print queue as job {jobId}. Nothing prints yet — the print bridge is a later change.',
  'billing.invoice.print.download': 'Download PDF',
  'billing.invoice.print.download.downloading': 'Downloading…',
  'billing.invoice.print.offlineAction.station': 'Sending to the print station',
  'billing.invoice.print.offlineAction.download': 'Downloading the invoice',
  'billing.problem.documentNotAvailable':
    'This document has not finished rendering yet. You can still print this page — try downloading again shortly.',
  'billing.problem.copiesOutOfRange': 'Choose between 1 and 5 copies.',
  'billing.problem.calculationNotFound': 'No calculation is stored under that reference.',
  'billing.problem.customerNotFound': 'The order’s customer could not be read.',
  'billing.problem.orderRevisedSinceDraft':
    'The order changed since this draft was made. Re-price it against the order as it now stands before posting.',
  'billing.problem.jobAlreadyInvoiced': 'One of these garment jobs is already on another invoice.',
  'billing.problem.snapshotMismatch':
    'The stored price no longer adds up. Re-price the order rather than posting this draft.',
  'billing.problem.totalsMismatch':
    'This draft’s totals no longer match its calculation. Re-price it before posting.',
  'billing.problem.calculationForAnotherBranch': 'That calculation was priced for another branch.',
  'billing.problem.branchNotKnown': 'This branch is not set up to draw invoice numbers yet.',
  'billing.problem.invoiceNotEditable':
    'Only a draft invoice changes. A posted invoice is corrected by a credit or debit note.',
  'billing.problem.invoiceChanged': 'Somebody changed this invoice. Here it is again.',
  'billing.problem.jobCancelled':
    'One of these garment jobs was cancelled, so it cannot be invoiced.',
  'billing.problem.jobRepeated': 'Two lines name the same garment job.',
  'billing.problem.lineNotAGarmentJob': 'That line does not name one of the order’s garment jobs.',
  'billing.problem.configurationMissing':
    'Something needed to price this order is not configured yet.',
  'billing.problem.reasonRequired': 'Say why.',
  'billing.problem.reasonNotWellFormed': 'A reason is plain text.',
  'billing.invoice.draft.title': 'Raise an invoice',
  'billing.invoice.draft.creating': 'the invoice',
  'billing.invoice.draft.missingParams.title': 'This screen is opened from an order',
  'billing.invoice.draft.missingParams':
    'There is nothing to draft without an order and a stored calculation. Reach this screen from the order it is for.',
  'billing.invoice.draft.review': 'Review this draft',
  'billing.invoice.draft.open': 'Open this invoice',
  'billing.invoice.draft.offlineAction': 'Drafting this invoice',
  'billing.invoice.post.action': 'Post',
  'billing.invoice.post.posting': 'Posting…',
  'billing.invoice.post.confirm.title': 'Post this invoice?',
  'billing.invoice.post.confirm.body':
    '{amount}. Once posted, this invoice cannot be edited — a correction becomes a credit or debit note.',
  'billing.invoice.post.confirm.typedPhrase': 'POST {orderNumber}',
  'billing.invoice.post.posted': 'Posted as {invoiceNumber}.',
  'billing.invoice.post.offlineAction': 'Posting this invoice',
  'billing.invoice.discard.action': 'Discard',
  'billing.invoice.discard.discarding': 'Discarding…',
  'billing.invoice.discard.confirm.title': 'Discard this draft?',
  'billing.invoice.discard.confirm.body':
    'This draft is abandoned, not deleted, and its garment jobs become free to invoice again.',
  'billing.invoice.discard.discarded': 'This draft is discarded.',
  'billing.invoice.discard.offlineAction': 'Discarding this draft',
  'billing.invoice.conflict.title': 'This invoice changed',
  'billing.invoice.conflict.body': 'Somebody changed this invoice. Here it is again.',
  'billing.problem.invoiceNotPosted': 'Only a posted invoice is cancelled or corrected by a note.',
  'billing.problem.invoiceAlreadyCancelled': 'This invoice has already been cancelled.',
  'billing.problem.cancellationWindowClosed':
    'This invoice was posted too long ago to cancel. Post a credit note against it instead.',
  'billing.problem.noteLineNotOnInvoice': 'That line does not belong to this invoice.',
  'billing.problem.noteExceedsLine': 'That is more than this line still carries.',
  'billing.problem.noteValueNotWellFormed': 'Enter a positive amount.',
  'billing.invoice.cancel.action': 'Cancel invoice',
  'billing.invoice.cancel.cancelling': 'Cancelling…',
  'billing.invoice.cancel.confirm.title': 'Cancel this invoice?',
  'billing.invoice.cancel.confirm.body':
    'The invoice keeps its number and totals. A credit note for {amount} is posted with it, and its garment jobs become free to invoice again.',
  'billing.invoice.cancel.cancelled': 'This invoice is cancelled.',
  'billing.invoice.cancel.offlineAction': 'Cancelling this invoice',
  'billing.invoice.notes.new': 'Issue a credit or debit note',
  'billing.invoice.notes.new.label': 'Issue a credit or debit note against {invoiceNumber}',
  'billing.note.title': 'Issue a credit or debit note',
  'billing.note.loading': 'the invoice',
  'billing.note.kind.label': 'Kind',
  'billing.note.kind.credit': 'Credit note',
  'billing.note.kind.debit': 'Debit note',
  'billing.note.line.remaining': 'Still carries {amount}',
  'billing.note.line.amount.label': '{description} — taxable value to move',
  'billing.note.line.amount.hint': 'Positive, to the paisa.',
  'billing.note.total': 'Total: {amount}',
  'billing.note.empty.title': 'Every line is already relieved',
  'billing.note.empty': 'There is nothing left to credit on this invoice.',
  'billing.note.incomplete': 'Enter a taxable value against at least one line before posting.',
  'billing.note.submit.credit': 'Post credit note',
  'billing.note.submit.debit': 'Post debit note',
  'billing.note.confirm.title.credit': 'Post this credit note?',
  'billing.note.confirm.title.debit': 'Post this debit note?',
  'billing.note.confirm.body': '{amount} against invoice {invoiceNumber}.',
  'billing.note.posted.credit': 'Credit note {number} posted against {invoiceNumber} for {amount}.',
  'billing.note.posted.debit': 'Debit note {number} posted against {invoiceNumber} for {amount}.',
  'billing.note.posted.download': 'Download PDF',
  'billing.note.posted.viewInvoice': 'Open the invoice',
  'billing.note.offlineAction': 'Posting a note',
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

  /* The shared billing findings list (E09-F01-5b) — a publish validation's errors and warnings. --- */
  'billing.findings.empty': 'Every check passed. There is nothing to fix or note.',
  'billing.findings.errors.title':
    '{count, plural, one {# problem must be fixed before this can publish} other {# problems must be fixed before this can publish}}',
  'billing.findings.warnings.title':
    '{count, plural, one {# warning} other {# warnings}} — none of these refuse the publication.',
  'billing.findings.severity.error': 'Error',
  'billing.findings.severity.warning': 'Warning',
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
  'billing.outstanding.count':
    '{count, plural, one {# invoice outstanding} other {# invoices outstanding}}',
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
  'billing.problem.barcodeNotFound': 'No invoice matches that barcode.',
  // not translated — awaiting native-speaker review
  'billing.invoices.status.draft': 'Draft',
  // not translated — awaiting native-speaker review
  'billing.invoices.status.posted': 'Posted',
  // not translated — awaiting native-speaker review
  'billing.invoices.status.discarded': 'Discarded',
  // not translated — awaiting native-speaker review
  'billing.invoices.title': 'Invoices',
  // not translated — awaiting native-speaker review
  'billing.invoices.loading': 'invoices',
  // not translated — awaiting native-speaker review
  'billing.invoices.empty.title': 'No invoices at this branch yet',
  // not translated — awaiting native-speaker review
  'billing.invoices.empty': 'Draft one from a confirmed order to see it here.',
  // not translated — awaiting native-speaker review
  'billing.invoices.empty.filtered.title': 'No invoices match this filter',
  // not translated — awaiting native-speaker review
  'billing.invoices.empty.filtered': 'Clear the status filter to see every invoice.',
  // not translated — awaiting native-speaker review
  'billing.invoices.caption': "The branch's invoices, newest first",
  // not translated — awaiting native-speaker review
  'billing.invoices.column.order': 'Order',
  // not translated — awaiting native-speaker review
  'billing.invoices.column.invoice': 'Invoice',
  // not translated — awaiting native-speaker review
  'billing.invoices.column.customer': 'Customer',
  // not translated — awaiting native-speaker review
  'billing.invoices.column.total': 'Total',
  // not translated — awaiting native-speaker review
  'billing.invoices.column.status': 'Status',
  // not translated — awaiting native-speaker review
  'billing.invoices.column.created': 'Created',
  // not translated — awaiting native-speaker review
  'billing.invoices.filter.status.label': 'Status',
  // not translated — awaiting native-speaker review
  'billing.invoices.filter.status.all': 'All statuses',
  // not translated — awaiting native-speaker review
  'billing.invoices.filter.applied': 'Status: {status}',
  // not translated — awaiting native-speaker review
  'billing.invoices.loadMore': 'Show more',
  // not translated — awaiting native-speaker review
  'billing.invoices.loadMore.offlineAction': 'Loading more invoices',
  // not translated — awaiting native-speaker review
  'billing.invoices.open': 'Open',
  // not translated — awaiting native-speaker review
  'billing.invoices.open.label': 'Open invoice {reference}',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.title': 'Find by barcode',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.label': 'Barcode',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.hint': 'Scan or type the I- payload printed on the invoice.',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.action': 'Find',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.finding': 'Finding…',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.required': 'Enter a barcode before searching.',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.offlineAction': 'Looking up a barcode',
  // not translated — awaiting native-speaker review
  'billing.invoices.lookup.branchRequired':
    'Your session needs a branch before a barcode can be looked up.',
  // not translated — awaiting native-speaker review
  'billing.invoice.title': 'Invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.loading': 'the invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.draft': 'Draft',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.order': 'Order {orderNumber}',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.financialYear': 'Financial year {year}',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.posted': 'Posted {date}',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.notPosted': 'Not yet posted',
  // not translated — awaiting native-speaker review
  'billing.invoice.header.discarded': 'Discarded {date}',
  // not translated — awaiting native-speaker review
  'billing.invoice.customer.title': 'Billed to',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.caption': 'Invoice lines',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.line': '#',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.description': 'Description',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.classification': 'HSN/SAC',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.quantity': 'Qty',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.rate': 'Rate',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.discount': 'Discount',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.taxableValue': 'Taxable value',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.tax': 'Tax',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.column.total': 'Line total',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.discount.value': '{ruleCode} −{amount}',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.discount.none': '—',
  // not translated — awaiting native-speaker review
  'billing.invoice.lines.tax.component': '{kind} {rate}: {amount}',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.title': 'Totals',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.subtotal': 'Subtotal',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.discountTotal': 'Discount',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.taxableValue': 'Taxable value',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.centralTax': 'CGST',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.stateTax': 'SGST',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.integratedTax': 'IGST',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.cess': 'Cess',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.roundOff': 'Round-off',
  // not translated — awaiting native-speaker review
  'billing.invoice.totals.grandTotal': 'Grand total',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancelled.title': 'This invoice is cancelled',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancelled.body': 'Cancelled {date}. {reason}',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.title': 'Credit and debit notes',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.credit': 'Credit note {number}',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.debit': 'Debit note {number}',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.posted': 'Posted {date}',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.reason': 'Reason: {reason}',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.download': 'Download PDF',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.download.label': 'Download {number} as a PDF',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.title.invoice': 'Tax invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.title.credit': 'Credit note',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.title.debit': 'Debit note',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.issued': 'Date {date} · FY {year}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.cancelledMark': 'CANCELLED',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.supplier.title': 'Supplier',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.supplier.gstin': 'GSTIN {gstin} · State {stateCode}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.artefactNote':
    'The downloadable PDF is the artefact of record and carries the letterhead and the terms.',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.customer.number': 'Customer {number}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.reference.order': 'Order',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.reference.note': 'Against invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.reference.placeOfSupply': 'Place of supply {state} · {scheme}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.line.itemAndClassification': '{itemCode} · HSN/SAC {classification}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.line.surcharge': '+ {description} {amount}',
  // not translated — awaiting native-speaker review
  'billing.invoice.document.balanceDue': 'Balance due',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.title': 'Print and download',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.page': 'Print this page',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station': 'Send to print station',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station.sending': 'Sending…',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station.copies.label': 'Copies',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station.copies.hint': 'One to five.',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station.copies.outOfRange': 'Choose between 1 and 5 copies.',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.station.sent':
    'Sent to the branch’s print queue as job {jobId}. Nothing prints yet — the print bridge is a later change.',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.download': 'Download PDF',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.download.downloading': 'Downloading…',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.offlineAction.station': 'Sending to the print station',
  // not translated — awaiting native-speaker review
  'billing.invoice.print.offlineAction.download': 'Downloading the invoice',
  // not translated — awaiting native-speaker review
  'billing.problem.documentNotAvailable':
    'This document has not finished rendering yet. You can still print this page — try downloading again shortly.',
  // not translated — awaiting native-speaker review
  'billing.problem.copiesOutOfRange': 'Choose between 1 and 5 copies.',
  // not translated — awaiting native-speaker review
  'billing.problem.calculationNotFound': 'No calculation is stored under that reference.',
  // not translated — awaiting native-speaker review
  'billing.problem.customerNotFound': 'The order’s customer could not be read.',
  // not translated — awaiting native-speaker review
  'billing.problem.orderRevisedSinceDraft':
    'The order changed since this draft was made. Re-price it against the order as it now stands before posting.',
  // not translated — awaiting native-speaker review
  'billing.problem.jobAlreadyInvoiced': 'One of these garment jobs is already on another invoice.',
  // not translated — awaiting native-speaker review
  'billing.problem.snapshotMismatch':
    'The stored price no longer adds up. Re-price the order rather than posting this draft.',
  // not translated — awaiting native-speaker review
  'billing.problem.totalsMismatch':
    'This draft’s totals no longer match its calculation. Re-price it before posting.',
  // not translated — awaiting native-speaker review
  'billing.problem.calculationForAnotherBranch': 'That calculation was priced for another branch.',
  // not translated — awaiting native-speaker review
  'billing.problem.branchNotKnown': 'This branch is not set up to draw invoice numbers yet.',
  // not translated — awaiting native-speaker review
  'billing.problem.invoiceNotEditable':
    'Only a draft invoice changes. A posted invoice is corrected by a credit or debit note.',
  // not translated — awaiting native-speaker review
  'billing.problem.invoiceChanged': 'Somebody changed this invoice. Here it is again.',
  // not translated — awaiting native-speaker review
  'billing.problem.jobCancelled':
    'One of these garment jobs was cancelled, so it cannot be invoiced.',
  // not translated — awaiting native-speaker review
  'billing.problem.jobRepeated': 'Two lines name the same garment job.',
  // not translated — awaiting native-speaker review
  'billing.problem.lineNotAGarmentJob': 'That line does not name one of the order’s garment jobs.',
  // not translated — awaiting native-speaker review
  'billing.problem.configurationMissing':
    'Something needed to price this order is not configured yet.',
  // not translated — awaiting native-speaker review
  'billing.problem.reasonRequired': 'Say why.',
  // not translated — awaiting native-speaker review
  'billing.problem.reasonNotWellFormed': 'A reason is plain text.',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.title': 'Raise an invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.creating': 'the invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.missingParams.title': 'This screen is opened from an order',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.missingParams':
    'There is nothing to draft without an order and a stored calculation. Reach this screen from the order it is for.',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.review': 'Review this draft',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.open': 'Open this invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.draft.offlineAction': 'Drafting this invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.action': 'Post',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.posting': 'Posting…',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.confirm.title': 'Post this invoice?',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.confirm.body':
    '{amount}. Once posted, this invoice cannot be edited — a correction becomes a credit or debit note.',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.confirm.typedPhrase': 'POST {orderNumber}',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.posted': 'Posted as {invoiceNumber}.',
  // not translated — awaiting native-speaker review
  'billing.invoice.post.offlineAction': 'Posting this invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.action': 'Discard',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.discarding': 'Discarding…',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.confirm.title': 'Discard this draft?',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.confirm.body':
    'This draft is abandoned, not deleted, and its garment jobs become free to invoice again.',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.discarded': 'This draft is discarded.',
  // not translated — awaiting native-speaker review
  'billing.invoice.discard.offlineAction': 'Discarding this draft',
  // not translated — awaiting native-speaker review
  'billing.invoice.conflict.title': 'This invoice changed',
  // not translated — awaiting native-speaker review
  'billing.invoice.conflict.body': 'Somebody changed this invoice. Here it is again.',
  // not translated — awaiting native-speaker review
  'billing.problem.invoiceNotPosted': 'Only a posted invoice is cancelled or corrected by a note.',
  // not translated — awaiting native-speaker review
  'billing.problem.invoiceAlreadyCancelled': 'This invoice has already been cancelled.',
  // not translated — awaiting native-speaker review
  'billing.problem.cancellationWindowClosed':
    'This invoice was posted too long ago to cancel. Post a credit note against it instead.',
  // not translated — awaiting native-speaker review
  'billing.problem.noteLineNotOnInvoice': 'That line does not belong to this invoice.',
  // not translated — awaiting native-speaker review
  'billing.problem.noteExceedsLine': 'That is more than this line still carries.',
  // not translated — awaiting native-speaker review
  'billing.problem.noteValueNotWellFormed': 'Enter a positive amount.',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.action': 'Cancel invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.cancelling': 'Cancelling…',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.confirm.title': 'Cancel this invoice?',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.confirm.body':
    'The invoice keeps its number and totals. A credit note for {amount} is posted with it, and its garment jobs become free to invoice again.',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.cancelled': 'This invoice is cancelled.',
  // not translated — awaiting native-speaker review
  'billing.invoice.cancel.offlineAction': 'Cancelling this invoice',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.new': 'Issue a credit or debit note',
  // not translated — awaiting native-speaker review
  'billing.invoice.notes.new.label': 'Issue a credit or debit note against {invoiceNumber}',
  // not translated — awaiting native-speaker review
  'billing.note.title': 'Issue a credit or debit note',
  // not translated — awaiting native-speaker review
  'billing.note.loading': 'the invoice',
  // not translated — awaiting native-speaker review
  'billing.note.kind.label': 'Kind',
  // not translated — awaiting native-speaker review
  'billing.note.kind.credit': 'Credit note',
  // not translated — awaiting native-speaker review
  'billing.note.kind.debit': 'Debit note',
  // not translated — awaiting native-speaker review
  'billing.note.line.remaining': 'Still carries {amount}',
  // not translated — awaiting native-speaker review
  'billing.note.line.amount.label': '{description} — taxable value to move',
  // not translated — awaiting native-speaker review
  'billing.note.line.amount.hint': 'Positive, to the paisa.',
  // not translated — awaiting native-speaker review
  'billing.note.total': 'Total: {amount}',
  // not translated — awaiting native-speaker review
  'billing.note.empty.title': 'Every line is already relieved',
  // not translated — awaiting native-speaker review
  'billing.note.empty': 'There is nothing left to credit on this invoice.',
  // not translated — awaiting native-speaker review
  'billing.note.incomplete': 'Enter a taxable value against at least one line before posting.',
  // not translated — awaiting native-speaker review
  'billing.note.submit.credit': 'Post credit note',
  // not translated — awaiting native-speaker review
  'billing.note.submit.debit': 'Post debit note',
  // not translated — awaiting native-speaker review
  'billing.note.confirm.title.credit': 'Post this credit note?',
  // not translated — awaiting native-speaker review
  'billing.note.confirm.title.debit': 'Post this debit note?',
  // not translated — awaiting native-speaker review
  'billing.note.confirm.body': '{amount} against invoice {invoiceNumber}.',
  // not translated — awaiting native-speaker review
  'billing.note.posted.credit': 'Credit note {number} posted against {invoiceNumber} for {amount}.',
  // not translated — awaiting native-speaker review
  'billing.note.posted.debit': 'Debit note {number} posted against {invoiceNumber} for {amount}.',
  // not translated — awaiting native-speaker review
  'billing.note.posted.download': 'Download PDF',
  // not translated — awaiting native-speaker review
  'billing.note.posted.viewInvoice': 'Open the invoice',
  // not translated — awaiting native-speaker review
  'billing.note.offlineAction': 'Posting a note',
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

  /* The shared billing findings list (E09-F01-5b) — a publish validation's errors and warnings. --- */
  // not translated — awaiting native-speaker review
  'billing.findings.empty': 'Every check passed. There is nothing to fix or note.',
  // not translated — awaiting native-speaker review
  'billing.findings.errors.title':
    '{count, plural, one {# problem must be fixed before this can publish} other {# problems must be fixed before this can publish}}',
  // not translated — awaiting native-speaker review
  'billing.findings.warnings.title':
    '{count, plural, one {# warning} other {# warnings}} — none of these refuse the publication.',
  // not translated — awaiting native-speaker review
  'billing.findings.severity.error': 'Error',
  // not translated — awaiting native-speaker review
  'billing.findings.severity.warning': 'Warning',
}
