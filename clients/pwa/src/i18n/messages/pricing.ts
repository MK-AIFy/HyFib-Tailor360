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
    'Every tax configuration version, newest first. Checking and publishing a version are not in this screen yet.',
  'pricing.tax.loading': 'the tax configuration versions',
  'pricing.tax.caption': 'Every tax configuration version, newest first',
  'pricing.tax.empty.title': 'No tax configuration version yet',
  'pricing.tax.empty': 'Start the first draft below.',
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
  'pricing.tax.open': 'Open version {number}',
  'pricing.tax.clone': 'Clone',
  'pricing.tax.clone.label': 'Clone version {number}',

  /* Starting a draft (E09-F01-5). ------------------------------------------------------------------ */
  'pricing.tax.start.action': 'Start a draft',
  'pricing.tax.start.offlineAction': 'Starting a tax configuration draft',
  'pricing.tax.start.title': 'Start a draft',
  'pricing.tax.start.body':
    'Empty, or a copy of an existing version carrying its codes — the ordinary way to change what is in force. Change its details and codes after it is started.',
  'pricing.tax.start.name': 'Name',
  'pricing.tax.start.notes': 'Notes (optional)',
  'pricing.tax.start.effectiveFrom': 'First day',
  'pricing.tax.start.cloneFrom': 'Clone from',
  'pricing.tax.start.cloneFrom.empty': 'Start empty',
  'pricing.tax.start.save': 'Start draft',
  'pricing.tax.started': 'Started the draft.',

  /* The tax configuration editor (E09-F01-5). ------------------------------------------------------ */
  'pricing.tax.editor.loading': 'the tax configuration version',
  'pricing.tax.editor.readOnly':
    'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
  'pricing.tax.editor.retired':
    'This version is retired. It is the record of what an invoice was once calculated on.',
  'pricing.tax.editor.publishComingSoon':
    'Checking this version and publishing it are not in this screen yet.',
  'pricing.tax.editor.heading': 'Tax configuration version {number}',
  'pricing.tax.editor.detailsTitle': 'Version details',
  'pricing.tax.editor.edit': 'Change the version’s details',
  'pricing.tax.editor.form.name': 'Name',
  'pricing.tax.editor.form.notes': 'Notes (optional)',
  'pricing.tax.editor.form.effectiveFrom': 'First day',
  'pricing.tax.editor.form.reason': 'Note (optional)',
  'pricing.tax.editor.form.save': 'Save',
  'pricing.tax.editor.form.offlineAction': 'Changing the version’s details',
  'pricing.tax.editor.saved': 'Saved the version’s details.',
  'pricing.tax.editor.reread': 'Read it again',
  'pricing.tax.editor.caption': 'Every tax code in this version',
  'pricing.tax.editor.empty': 'No tax code yet. Add the first one below.',
  'pricing.tax.editor.column.code': 'Code',
  'pricing.tax.editor.column.description': 'Description',
  'pricing.tax.editor.column.classification': 'HSN/SAC',
  'pricing.tax.editor.column.kind': 'Kind',
  'pricing.tax.editor.column.active': 'Status',
  'pricing.tax.editor.column.rates': 'Rates',
  'pricing.tax.editor.kind.Goods': 'Goods',
  'pricing.tax.editor.kind.Services': 'Services',
  'pricing.tax.editor.active.yes': 'Active',
  'pricing.tax.editor.active.no': 'Inactive',
  'pricing.tax.editor.rates.none': 'Nil-rated',

  /* Adding, editing and removing a tax code. -------------------------------------------------------- */
  'pricing.tax.code.add': 'Add a tax code',
  'pricing.tax.code.add.offlineAction': 'Adding a tax code',
  'pricing.tax.code.edit': 'Edit {code}',
  'pricing.tax.code.edit.offlineAction': 'Editing a tax code',
  'pricing.tax.code.remove': 'Remove {code}',
  'pricing.tax.code.remove.offlineAction': 'Removing a tax code',
  'pricing.tax.code.remove.title': 'Remove a tax code',
  'pricing.tax.code.remove.body':
    'This removes the tax code from the draft. It can be added again, as a fresh row.',
  'pricing.tax.code.saved': 'Saved the tax code {code}.',
  'pricing.tax.code.removed': 'Removed the tax code {code}.',
  'pricing.taxCode.form.addTitle': 'Add a tax code',
  'pricing.taxCode.form.editTitle': 'Edit the tax code {code}',
  'pricing.taxCode.form.code': 'Code',
  'pricing.taxCode.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter.',
  'pricing.taxCode.form.description': 'Description',
  'pricing.taxCode.form.classification': 'HSN or SAC classification',
  'pricing.taxCode.form.classification.hint': 'Digits only, four to eight of them.',
  'pricing.taxCode.form.kind': 'Kind',
  'pricing.taxCode.form.kind.Goods': 'Goods',
  'pricing.taxCode.form.kind.Services': 'Services',
  'pricing.taxCode.form.active': 'Status',
  'pricing.taxCode.form.active.true': 'Active',
  'pricing.taxCode.form.active.false': 'Inactive',
  'pricing.taxCode.form.rates.title': 'Component rates',
  'pricing.taxCode.form.rates.hint':
    'Leave a component blank when this code does not carry it. Enter a rate as a percentage — no rate is suggested; it is the accountant’s to enter.',
  'pricing.taxCode.form.rate.Cgst': 'CGST',
  'pricing.taxCode.form.rate.Sgst': 'SGST',
  'pricing.taxCode.form.rate.Igst': 'IGST',
  'pricing.taxCode.form.rate.Cess': 'Cess',
  'pricing.taxCode.form.reason': 'Note (optional)',
  'pricing.taxCode.form.save': 'Save',

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
  'pricing.problem.versionNotEditable':
    'Only a draft can be changed. Clone this version to a new draft to make the change there.',
  'pricing.problem.versionChanged':
    'Someone else changed this version while it was open here. Read it again to see what changed.',
  'pricing.problem.versionNotFound': 'No tax configuration version matches that address.',
  'pricing.problem.codeNotUnique':
    'That code is already used in this version. Choose a different one.',
  'pricing.problem.codeNotWellFormed':
    'A code is upper snake case — capital letters, digits and underscores, beginning with a letter.',
  'pricing.problem.classificationNotWellFormed': 'An HSN or SAC code is four to eight digits.',
  'pricing.problem.rateOutOfRange':
    'A rate is a percentage between 0 and 100, to at most three decimal places.',
  'pricing.problem.rateNotWellFormed':
    'That does not look like a rate. Enter a percentage, such as 2.5.',
  'pricing.problem.componentDuplicated':
    'A tax code carries each component — CGST, SGST, IGST, cess — at most once.',
  'pricing.problem.componentNotWellFormed': 'That is not a component this product knows.',
  'pricing.problem.valueRequired': 'This is required.',
  'pricing.problem.valueTooLong': 'That is longer than this field allows.',
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
    'Every tax configuration version, newest first. Checking and publishing a version are not in this screen yet.',
  // not translated — awaiting native-speaker review
  'pricing.tax.loading': 'the tax configuration versions',
  // not translated — awaiting native-speaker review
  'pricing.tax.caption': 'Every tax configuration version, newest first',
  // not translated — awaiting native-speaker review
  'pricing.tax.empty.title': 'No tax configuration version yet',
  // not translated — awaiting native-speaker review
  'pricing.tax.empty': 'Start the first draft below.',
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
  // not translated — awaiting native-speaker review
  'pricing.tax.open': 'Open version {number}',
  // not translated — awaiting native-speaker review
  'pricing.tax.clone': 'Clone',
  // not translated — awaiting native-speaker review
  'pricing.tax.clone.label': 'Clone version {number}',

  /* Starting a draft (E09-F01-5). ------------------------------------------------------------------ */
  // not translated — awaiting native-speaker review
  'pricing.tax.start.action': 'Start a draft',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.offlineAction': 'Starting a tax configuration draft',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.title': 'Start a draft',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.body':
    'Empty, or a copy of an existing version carrying its codes — the ordinary way to change what is in force. Change its details and codes after it is started.',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.notes': 'Notes (optional)',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.cloneFrom': 'Clone from',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.cloneFrom.empty': 'Start empty',
  // not translated — awaiting native-speaker review
  'pricing.tax.start.save': 'Start draft',
  // not translated — awaiting native-speaker review
  'pricing.tax.started': 'Started the draft.',

  /* The tax configuration editor (E09-F01-5). ------------------------------------------------------ */
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.loading': 'the tax configuration version',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.readOnly':
    'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.retired':
    'This version is retired. It is the record of what an invoice was once calculated on.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publishComingSoon':
    'Checking this version and publishing it are not in this screen yet.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.heading': 'Tax configuration version {number}',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.detailsTitle': 'Version details',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.edit': 'Change the version’s details',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.notes': 'Notes (optional)',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.save': 'Save',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.form.offlineAction': 'Changing the version’s details',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.saved': 'Saved the version’s details.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.reread': 'Read it again',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.caption': 'Every tax code in this version',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.empty': 'No tax code yet. Add the first one below.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.classification': 'HSN/SAC',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.kind': 'Kind',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.column.rates': 'Rates',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.kind.Goods': 'Goods',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.kind.Services': 'Services',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.active.yes': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.active.no': 'Inactive',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.rates.none': 'Nil-rated',

  /* Adding, editing and removing a tax code. -------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.tax.code.add': 'Add a tax code',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.add.offlineAction': 'Adding a tax code',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.edit': 'Edit {code}',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.edit.offlineAction': 'Editing a tax code',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.remove': 'Remove {code}',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.remove.offlineAction': 'Removing a tax code',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.remove.title': 'Remove a tax code',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.remove.body':
    'This removes the tax code from the draft. It can be added again, as a fresh row.',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.saved': 'Saved the tax code {code}.',
  // not translated — awaiting native-speaker review
  'pricing.tax.code.removed': 'Removed the tax code {code}.',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.addTitle': 'Add a tax code',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.editTitle': 'Edit the tax code {code}',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter.',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.classification': 'HSN or SAC classification',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.classification.hint': 'Digits only, four to eight of them.',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.kind': 'Kind',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.kind.Goods': 'Goods',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.kind.Services': 'Services',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.active.true': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.active.false': 'Inactive',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rates.title': 'Component rates',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rates.hint':
    'Leave a component blank when this code does not carry it. Enter a rate as a percentage — no rate is suggested; it is the accountant’s to enter.',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rate.Cgst': 'CGST',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rate.Sgst': 'SGST',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rate.Igst': 'IGST',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.rate.Cess': 'Cess',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.taxCode.form.save': 'Save',

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
  // not translated — awaiting native-speaker review
  'pricing.problem.versionNotEditable':
    'Only a draft can be changed. Clone this version to a new draft to make the change there.',
  // not translated — awaiting native-speaker review
  'pricing.problem.versionChanged':
    'Someone else changed this version while it was open here. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'pricing.problem.versionNotFound': 'No tax configuration version matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.codeNotUnique':
    'That code is already used in this version. Choose a different one.',
  // not translated — awaiting native-speaker review
  'pricing.problem.codeNotWellFormed':
    'A code is upper snake case — capital letters, digits and underscores, beginning with a letter.',
  // not translated — awaiting native-speaker review
  'pricing.problem.classificationNotWellFormed': 'An HSN or SAC code is four to eight digits.',
  // not translated — awaiting native-speaker review
  'pricing.problem.rateOutOfRange':
    'A rate is a percentage between 0 and 100, to at most three decimal places.',
  // not translated — awaiting native-speaker review
  'pricing.problem.rateNotWellFormed':
    'That does not look like a rate. Enter a percentage, such as 2.5.',
  // not translated — awaiting native-speaker review
  'pricing.problem.componentDuplicated':
    'A tax code carries each component — CGST, SGST, IGST, cess — at most once.',
  // not translated — awaiting native-speaker review
  'pricing.problem.componentNotWellFormed': 'That is not a component this product knows.',
  // not translated — awaiting native-speaker review
  'pricing.problem.valueRequired': 'This is required.',
  // not translated — awaiting native-speaker review
  'pricing.problem.valueTooLong': 'That is longer than this field allows.',
}
