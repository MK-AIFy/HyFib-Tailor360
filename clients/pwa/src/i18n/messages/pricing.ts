/**
 * Pricing administration messages — the GST registration register and the tax configuration version
 * register (#237), the first of eight client slices against #41's pricing surface.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. This file is the one
 * later siblings append to (E09-F01-5 through E09-F01-8b) — append at the end of this file's own
 * block, do not re-order it.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * The same reason recorded for the authentication, administration and catalogue families. These
 * sentences decide what is registered against the organisation's tax position and are read while
 * somebody is changing it; they are held back for native-speaker review with the rest of the
 * operational vocabulary.
 */
export const pricingEn = {
  /* Navigation. ----------------------------------------------------------------------------- */
  'pricing.nav.gstRegistrations': 'GST registrations',
  'pricing.nav.taxConfiguration': 'Tax configuration',

  /* The GST registration register. ----------------------------------------------------------- */
  'pricing.gst.title': 'GST registrations',
  'pricing.gst.body':
    'Every GST registration this organisation has recorded, by branch and first day. A registration is never deleted — amend it, or give it a last day.',
  'pricing.gst.loading': 'the organisation’s GST registrations',
  'pricing.gst.caption': 'Every GST registration, by branch and first day',
  'pricing.gst.empty.title': 'No registration recorded yet',
  'pricing.gst.empty': 'Record the first branch’s GST registration below.',
  'pricing.gst.column.branch': 'Branch',
  'pricing.gst.column.gstin': 'GSTIN',
  'pricing.gst.column.stateCode': 'State code',
  'pricing.gst.column.legalName': 'Legal name',
  'pricing.gst.column.tradeName': 'Trade name',
  'pricing.gst.column.effectiveFrom': 'First day',
  'pricing.gst.column.effectiveTo': 'Last day',
  'pricing.gst.stillInForce': 'Still in force',
  'pricing.gst.amend': 'Amend',
  'pricing.gst.amend.label': 'Amend the registration {gstin}',
  'pricing.gst.record.action': 'Record a registration',
  'pricing.gst.record.offlineAction': 'Recording a GST registration',
  'pricing.gst.amend.offlineAction': 'Amending a GST registration',

  /* The record and amend form. ------------------------------------------------------------------- */
  'pricing.gst.form.addTitle': 'Record a GST registration',
  'pricing.gst.form.editTitle': 'Amend the registration {gstin}',
  'pricing.gst.form.branch': 'Branch',
  'pricing.gst.form.branch.readOnlyHint':
    'Set when the registration is recorded, and never changed by an amendment.',
  'pricing.gst.form.branch.id': 'Branch identifier',
  'pricing.gst.form.branch.namesUnavailable':
    'Branch names could not be loaded — enter the identifier exactly as it appears in the branch register. Ask an Owner or Admin for access to see names here.',
  'pricing.gst.form.gstin': 'GSTIN',
  'pricing.gst.form.gstin.hint':
    'Fifteen characters: the state code, the PAN, an entity code and a check character.',
  'pricing.gst.form.stateCode': 'State code',
  'pricing.gst.form.stateCode.hint':
    'The two-digit GST state code this registration is issued under.',
  'pricing.gst.form.legalName': 'Legal name',
  'pricing.gst.form.tradeName': 'Trade name',
  'pricing.gst.form.effectiveFrom': 'First day',
  'pricing.gst.form.effectiveTo': 'Last day',
  'pricing.gst.form.effectiveTo.hint':
    'Leave blank while the registration is in force. A registration is never deleted: documents already posted under it keep it exactly as it stood when they were posted.',
  'pricing.gst.form.reason': 'Note (optional)',
  'pricing.gst.form.save': 'Save',
  'pricing.gst.form.reread': 'Read it again',
  'pricing.gst.recorded': 'Recorded the registration.',
  'pricing.gst.amended': 'Amended the registration.',

  /* The tax configuration version register. ---------------------------------------------------- */
  'pricing.tax.title': 'Tax configuration',
  'pricing.tax.body':
    'Every tax configuration version, newest first. Drafting, checking and publishing a version are not in this screen yet.',
  'pricing.tax.loading': 'the tax configuration versions',
  'pricing.tax.caption': 'Every tax configuration version, newest first',
  'pricing.tax.empty.title': 'No tax configuration version yet',
  'pricing.tax.empty': 'No version has been drafted yet. Drafting one is not in this screen yet.',
  'pricing.tax.column.version': 'Version',
  'pricing.tax.column.name': 'Name',
  'pricing.tax.column.status': 'State',
  'pricing.tax.column.effectiveFrom': 'First day',
  'pricing.tax.column.publishedAt': 'Published',
  'pricing.tax.status.Draft': 'Draft',
  'pricing.tax.status.Published': 'Published',
  'pricing.tax.status.Retired': 'Retired',
  'pricing.tax.status.unknown': 'Unknown',
  'pricing.tax.notPublishedYet': 'Not yet published',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  'pricing.problem.gstinNotWellFormed':
    'This does not look like a GSTIN. It is fifteen characters, ending with a check character the server verifies.',
  'pricing.problem.gstinStateMismatch': 'This GSTIN’s state does not match the state code entered.',
  'pricing.problem.stateCodeNotWellFormed':
    'Enter the two-digit GST state code this registration is issued under.',
  'pricing.problem.datesNotOrdered': 'The last day must be on or after the first day.',
  'pricing.problem.registrationOverlaps':
    'Another registration is already in force for this branch on this day. End it first, or choose a later first day.',
  'pricing.problem.registrationChanged':
    'Someone else changed this registration while it was open here. Read it again to see what changed.',
  'pricing.problem.registrationNotFound': 'No registration matches that address.',
} as const

/*
 * Every key repeated rather than mapped from the English catalogue, and that is deliberate — see
 * `catalog.ts`'s own note on why. The duplication is the record.
 */
export const pricingTa: Record<keyof typeof pricingEn, string> = {
  /* Navigation. ----------------------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.nav.gstRegistrations': 'GST registrations',
  // not translated — awaiting native-speaker review
  'pricing.nav.taxConfiguration': 'Tax configuration',

  /* The GST registration register. ----------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.gst.title': 'GST registrations',
  // not translated — awaiting native-speaker review
  'pricing.gst.body':
    'Every GST registration this organisation has recorded, by branch and first day. A registration is never deleted — amend it, or give it a last day.',
  // not translated — awaiting native-speaker review
  'pricing.gst.loading': 'the organisation’s GST registrations',
  // not translated — awaiting native-speaker review
  'pricing.gst.caption': 'Every GST registration, by branch and first day',
  // not translated — awaiting native-speaker review
  'pricing.gst.empty.title': 'No registration recorded yet',
  // not translated — awaiting native-speaker review
  'pricing.gst.empty': 'Record the first branch’s GST registration below.',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.branch': 'Branch',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.gstin': 'GSTIN',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.stateCode': 'State code',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.legalName': 'Legal name',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.tradeName': 'Trade name',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.gst.column.effectiveTo': 'Last day',
  // not translated — awaiting native-speaker review
  'pricing.gst.stillInForce': 'Still in force',
  // not translated — awaiting native-speaker review
  'pricing.gst.amend': 'Amend',
  // not translated — awaiting native-speaker review
  'pricing.gst.amend.label': 'Amend the registration {gstin}',
  // not translated — awaiting native-speaker review
  'pricing.gst.record.action': 'Record a registration',
  // not translated — awaiting native-speaker review
  'pricing.gst.record.offlineAction': 'Recording a GST registration',
  // not translated — awaiting native-speaker review
  'pricing.gst.amend.offlineAction': 'Amending a GST registration',

  /* The record and amend form. ------------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.gst.form.addTitle': 'Record a GST registration',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.editTitle': 'Amend the registration {gstin}',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.branch': 'Branch',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.branch.readOnlyHint':
    'Set when the registration is recorded, and never changed by an amendment.',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.branch.id': 'Branch identifier',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.branch.namesUnavailable':
    'Branch names could not be loaded — enter the identifier exactly as it appears in the branch register. Ask an Owner or Admin for access to see names here.',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.gstin': 'GSTIN',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.gstin.hint':
    'Fifteen characters: the state code, the PAN, an entity code and a check character.',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.stateCode': 'State code',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.stateCode.hint':
    'The two-digit GST state code this registration is issued under.',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.legalName': 'Legal name',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.tradeName': 'Trade name',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.effectiveTo': 'Last day',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.effectiveTo.hint':
    'Leave blank while the registration is in force. A registration is never deleted: documents already posted under it keep it exactly as it stood when they were posted.',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.save': 'Save',
  // not translated — awaiting native-speaker review
  'pricing.gst.form.reread': 'Read it again',
  // not translated — awaiting native-speaker review
  'pricing.gst.recorded': 'Recorded the registration.',
  // not translated — awaiting native-speaker review
  'pricing.gst.amended': 'Amended the registration.',

  /* The tax configuration version register. ---------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.tax.title': 'Tax configuration',
  // not translated — awaiting native-speaker review
  'pricing.tax.body':
    'Every tax configuration version, newest first. Drafting, checking and publishing a version are not in this screen yet.',
  // not translated — awaiting native-speaker review
  'pricing.tax.loading': 'the tax configuration versions',
  // not translated — awaiting native-speaker review
  'pricing.tax.caption': 'Every tax configuration version, newest first',
  // not translated — awaiting native-speaker review
  'pricing.tax.empty.title': 'No tax configuration version yet',
  // not translated — awaiting native-speaker review
  'pricing.tax.empty': 'No version has been drafted yet. Drafting one is not in this screen yet.',
  // not translated — awaiting native-speaker review
  'pricing.tax.column.version': 'Version',
  // not translated — awaiting native-speaker review
  'pricing.tax.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.tax.column.status': 'State',
  // not translated — awaiting native-speaker review
  'pricing.tax.column.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.tax.column.publishedAt': 'Published',
  // not translated — awaiting native-speaker review
  'pricing.tax.status.Draft': 'Draft',
  // not translated — awaiting native-speaker review
  'pricing.tax.status.Published': 'Published',
  // not translated — awaiting native-speaker review
  'pricing.tax.status.Retired': 'Retired',
  // not translated — awaiting native-speaker review
  'pricing.tax.status.unknown': 'Unknown',
  // not translated — awaiting native-speaker review
  'pricing.tax.notPublishedYet': 'Not yet published',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  // not translated — awaiting native-speaker review
  'pricing.problem.gstinNotWellFormed':
    'This does not look like a GSTIN. It is fifteen characters, ending with a check character the server verifies.',
  // not translated — awaiting native-speaker review
  'pricing.problem.gstinStateMismatch': 'This GSTIN’s state does not match the state code entered.',
  // not translated — awaiting native-speaker review
  'pricing.problem.stateCodeNotWellFormed':
    'Enter the two-digit GST state code this registration is issued under.',
  // not translated — awaiting native-speaker review
  'pricing.problem.datesNotOrdered': 'The last day must be on or after the first day.',
  // not translated — awaiting native-speaker review
  'pricing.problem.registrationOverlaps':
    'Another registration is already in force for this branch on this day. End it first, or choose a later first day.',
  // not translated — awaiting native-speaker review
  'pricing.problem.registrationChanged':
    'Someone else changed this registration while it was open here. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'pricing.problem.registrationNotFound': 'No registration matches that address.',
}
