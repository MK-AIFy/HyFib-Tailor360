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
  'pricing.nav.priceLists': 'Price lists',

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

  /* Validating and publishing a version (E09-F01-5b). ------------------------------------------ */
  'pricing.tax.editor.validate': 'Check this version',
  'pricing.tax.editor.check.stale':
    'This version has changed since the last check. Check it again before publishing.',
  'pricing.tax.editor.publish': 'Publish this version',
  'pricing.tax.editor.publish.title': 'Publish this tax configuration?',
  'pricing.tax.editor.publish.body':
    'Every invoice from the moment you publish is calculated on this version, and whatever is published now is superseded. Invoices already posted keep the version they were calculated on.',
  'pricing.tax.editor.publish.done': 'Published.',
  'pricing.tax.editor.publish.superseded':
    'It supersedes the version that was published before it.',
  'pricing.tax.editor.publish.forbiddenAction': 'publishing a tax configuration version',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  'pricing.problem.versionNotPublishable':
    'Only a draft can be published. Clone this version to a new draft to publish the change there.',
  'pricing.problem.publishValidationFailed':
    'This version cannot be published yet. Correct what the checks below report.',
  'pricing.problem.publishConflict':
    'Someone else published a version at the same moment. Here is where this version now stands.',
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

  /* The price-list register (#252). ------------------------------------------------------------- */
  'pricing.priceList.title': 'Price lists',
  'pricing.priceList.body':
    'Every price list this organisation has, by its code. A list is never deleted — start a version to change what it prices.',
  'pricing.priceList.loading': 'the organisation’s price lists',
  'pricing.priceList.caption': 'Every price list, by code',
  'pricing.priceList.empty.title': 'No price list yet',
  'pricing.priceList.empty': 'Create the first price list below.',
  'pricing.priceList.column.code': 'Code',
  'pricing.priceList.column.name': 'Name',
  'pricing.priceList.open': 'Open',
  'pricing.priceList.open.label': 'Open the price list {code}',
  'pricing.priceList.rename': 'Rename',
  'pricing.priceList.rename.label': 'Rename the price list {code}',
  'pricing.priceList.create.action': 'Create a price list',
  'pricing.priceList.create.offlineAction': 'Creating a price list',
  'pricing.priceList.rename.offlineAction': 'Renaming a price list',
  'pricing.priceList.created': 'Created the price list.',
  'pricing.priceList.renamed': 'Renamed the price list.',
  'pricing.priceList.form.reread': 'Read it again',

  /* The create and rename form. ------------------------------------------------------------------ */
  'pricing.priceList.form.addTitle': 'Create a price list',
  'pricing.priceList.form.editTitle': 'Rename {code}',
  'pricing.priceList.form.code': 'Code',
  'pricing.priceList.form.code.hint':
    'Letters, numbers and underscores. Used on seeds and exports once this list exists, and cannot be changed afterwards.',
  'pricing.priceList.form.code.fixedHint':
    'The code cannot change once a list exists — seeds and exports refer to it.',
  'pricing.priceList.form.name': 'Name',
  'pricing.priceList.form.reason': 'Note (optional)',
  'pricing.priceList.form.save': 'Save',

  /* A list's versions. ----------------------------------------------------------------------- */
  'pricing.priceList.version.title': 'Versions of {code}',
  'pricing.priceList.version.body':
    'Every version of this price list, newest first. A version is never deleted — a change is a clone of it into a new draft.',
  'pricing.priceList.version.loading': 'the price list’s versions',
  'pricing.priceList.version.caption': 'Every version, newest first',
  'pricing.priceList.version.empty.title': 'No version yet',
  'pricing.priceList.version.empty': 'Start the first version below.',
  'pricing.priceList.version.column.version': 'Version',
  'pricing.priceList.version.column.status': 'State',
  'pricing.priceList.version.column.name': 'Name',
  'pricing.priceList.version.column.effectiveFrom': 'First day',
  'pricing.priceList.version.column.tax': 'Tax',
  'pricing.priceList.version.column.roundOff': 'Round-off',
  'pricing.priceList.version.column.threshold': 'Override threshold',
  'pricing.priceList.version.column.branches': 'Branches',
  'pricing.priceList.version.column.clonedFrom': 'Cloned from',
  'pricing.priceList.version.clonedFrom.none': '—',
  'pricing.priceList.version.clonedFrom.value': 'Version {versionNumber}',
  'pricing.priceList.version.branches.none': '—',
  'pricing.priceList.version.tax.inclusive': 'Inclusive',
  'pricing.priceList.version.tax.exclusive': 'Exclusive',
  'pricing.priceList.version.status.Draft': 'Draft',
  'pricing.priceList.version.status.Published': 'Published',
  'pricing.priceList.version.status.Retired': 'Retired',
  'pricing.priceList.version.status.unknown': 'Unknown',
  'pricing.priceList.version.start.action': 'Start a draft',
  'pricing.priceList.version.start.offlineAction': 'Starting a price-list draft',
  'pricing.priceList.version.clone.action': 'Clone into a new draft',
  'pricing.priceList.version.clone.label': 'Clone version {versionNumber} into a new draft',
  'pricing.priceList.version.started': 'Started the draft.',
  'pricing.priceList.roundOff.none': 'No rounding',
  'pricing.priceList.roundOff.nearestRupee': 'Nearest rupee',

  /* The version-conventions form, reused by E09-F01-7 to change one. -------------------------- */
  'pricing.priceList.version.form.startTitle': 'Start a draft',
  'pricing.priceList.version.form.cloneTitle': 'Clone version {versionNumber} into a new draft',
  'pricing.priceList.version.form.clonedFrom':
    'Cloned from version {versionNumber}. Every convention below starts from that version and can be changed before saving.',
  'pricing.priceList.version.form.name': 'Name',
  'pricing.priceList.version.form.notes': 'Notes (optional)',
  'pricing.priceList.version.form.effectiveFrom': 'First day',
  'pricing.priceList.version.form.tax': 'Tax treatment',
  'pricing.priceList.version.form.tax.inclusive': 'Tax inclusive',
  'pricing.priceList.version.form.tax.exclusive': 'Tax exclusive',
  'pricing.priceList.version.form.roundOff': 'Round-off rule',
  'pricing.priceList.version.form.roundOff.none': 'No rounding',
  'pricing.priceList.version.form.roundOff.nearestRupee': 'Nearest rupee',
  'pricing.priceList.version.form.threshold': 'Override threshold',
  'pricing.priceList.version.form.threshold.hint':
    'The variance above which a line needs the override permission, as a percentage.',
  'pricing.priceList.version.form.branches': 'Branches this version prices',
  'pricing.priceList.version.form.branches.hint':
    'Choose every branch this version prices. Publication refuses a version pricing no branch.',
  'pricing.priceList.version.form.branches.forbiddenAction':
    'choosing which branches this version prices',
  'pricing.priceList.version.form.reason': 'Note (optional)',
  'pricing.priceList.version.form.save': 'Start the draft',
  'pricing.priceList.version.form.editTitle': 'Version {versionNumber} conventions',
  'pricing.priceList.version.form.saveChanges': 'Save',
  'pricing.priceList.version.open': 'Open',
  'pricing.priceList.version.open.label': 'Open version {versionNumber}',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  'pricing.problem.branchNotFound': 'No branch matches that identifier.',
  'pricing.problem.priceListNotFound': 'No price list matches that address.',
  'pricing.problem.priceListChanged':
    'Someone else changed this price list while it was open here. Read it again to see what changed.',
  'pricing.problem.draftNumberConflict':
    'Another draft was started for this list at the same moment. Read the versions again and try once more.',
  'pricing.problem.versionNotEditable':
    'Only a draft can be changed. Clone this version to a new draft to make the change there.',
  'pricing.problem.versionChanged':
    'Someone else changed this version while it was open here. Read it again to see what changed.',
  'pricing.problem.versionNotFound': 'No tax configuration version matches that address.',
  // `codeNotUnique`, `codeNotWellFormed` and `valueRequired` are shared between the price-list
  // register and the tax configuration editor: both validate a code against the same
  // `BillingCode.IsWellFormed` grammar, and a required field is a required field either way.
  'pricing.problem.codeNotUnique': 'That code is already used. Choose a different one.',
  'pricing.problem.codeNotWellFormed':
    'A code is upper snake case — capital letters, digits and underscores, beginning with a letter.',
  'pricing.problem.classificationNotWellFormed': 'An HSN or SAC code is four to eight digits.',
  'pricing.problem.rateOutOfRange':
    'A rate is a percentage between 0 and 100, to at most three decimal places.',
  'pricing.problem.rateNotWellFormed':
    'A rate is a non-negative amount with at most four decimal places.',
  'pricing.problem.componentDuplicated':
    'A tax code carries each component — CGST, SGST, IGST, cess — at most once.',
  'pricing.problem.componentNotWellFormed': 'That is not a component this product knows.',
  'pricing.problem.valueRequired': 'This is required.',
  'pricing.problem.valueTooLong': 'That is longer than this field allows.',

  /* The price-list version editor: conventions, items and the tax-code picker (E09-F01-7, #268). --- */
  'pricing.priceList.editor.loading': 'the price-list version',
  'pricing.priceList.editor.heading': 'Price-list version {number}',
  'pricing.priceList.editor.readOnly':
    'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
  'pricing.priceList.editor.retired':
    'This version is retired. It is the record of what an invoice was once calculated on.',
  'pricing.priceList.editor.conventionsTitle': 'Conventions',
  'pricing.priceList.editor.editConventions': 'Change the version’s conventions',
  'pricing.priceList.editor.conventions.offlineAction': 'Changing the version’s conventions',
  'pricing.priceList.editor.saved': 'Saved the version’s conventions.',
  'pricing.priceList.editor.reread': 'Read it again',
  'pricing.priceList.editor.itemsTitle': 'Items',
  'pricing.priceList.editor.items.empty': 'No item yet. Add the first one below.',
  'pricing.priceList.editor.items.caption': 'Every item in this version',
  'pricing.priceList.editor.discountRulesTitle': 'Discount rules',
  'pricing.priceList.editor.discountRulesComingSoon':
    'Adding, editing and removing a discount rule are not in this screen yet.',
  'pricing.priceList.editor.discountRules.empty': 'No discount rule yet.',
  'pricing.priceList.editor.discountRules.caption':
    'Every discount rule in this version, read-only',

  'pricing.priceListItem.column.code': 'Code',
  'pricing.priceListItem.column.description': 'Description',
  'pricing.priceListItem.column.kind': 'Kind',
  'pricing.priceListItem.column.baseRate': 'Base rate',
  'pricing.priceListItem.column.unit': 'Unit',
  'pricing.priceListItem.column.taxCode': 'Tax code',
  'pricing.priceListItem.column.active': 'Status',
  'pricing.priceListItem.active.yes': 'Active',
  'pricing.priceListItem.active.no': 'Inactive',
  'pricing.priceListItem.add': 'Add an item',
  'pricing.priceListItem.add.offlineAction': 'Adding an item',
  'pricing.priceListItem.edit': 'Edit {code}',
  'pricing.priceListItem.edit.offlineAction': 'Editing an item',
  'pricing.priceListItem.remove': 'Remove {code}',
  'pricing.priceListItem.remove.offlineAction': 'Removing an item',
  'pricing.priceListItem.remove.title': 'Remove an item',
  'pricing.priceListItem.remove.body':
    'This removes the item from the draft. It can be added again, as a fresh row.',
  'pricing.priceListItem.saved': 'Saved the item {code}.',
  'pricing.priceListItem.removed': 'Removed the item {code}.',

  'pricing.priceListItem.form.addTitle': 'Add an item',
  'pricing.priceListItem.form.editTitle': 'Edit the item {code}',
  'pricing.priceListItem.form.code': 'Code',
  'pricing.priceListItem.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter. What the catalogue’s price-list item code names.',
  'pricing.priceListItem.form.description': 'Description',
  'pricing.priceListItem.form.kind': 'What this prices',
  'pricing.priceListItem.form.kind.Service': 'A service’s base charge',
  'pricing.priceListItem.form.kind.Surcharge': 'A surcharge',
  'pricing.priceListItem.form.kind.Material': 'A material',
  'pricing.priceListItem.form.baseRate': 'Base rate',
  'pricing.priceListItem.form.unit': 'Unit',
  'pricing.priceListItem.form.unit.hint': 'A short lower-case word: each, metre, hour.',
  'pricing.priceListItem.form.taxCode': 'Tax code',
  'pricing.priceListItem.form.taxCode.hint':
    'Choose one of the published tax configuration’s active codes, or type another.',
  'pricing.priceListItem.form.taxCode.noneHint':
    'No tax configuration is published yet, so no code can be suggested. The code is checked when this version is published, not now — the item still saves with a typed code.',
  'pricing.priceListItem.form.taxCode.noneHint.link': 'Open the tax configuration',
  'pricing.priceListItem.form.active': 'Status',
  'pricing.priceListItem.form.active.true': 'Active',
  'pricing.priceListItem.form.active.false': 'Inactive',
  'pricing.priceListItem.form.reason': 'Note (optional)',
  'pricing.priceListItem.form.save': 'Save',

  'pricing.discountRule.column.code': 'Code',
  'pricing.discountRule.column.description': 'Description',
  'pricing.discountRule.column.kind': 'Kind',
  'pricing.discountRule.column.maximumWithoutApproval': 'Maximum without approval',
  'pricing.discountRule.column.maximum': 'Maximum',
  'pricing.discountRule.column.active': 'Status',
  'pricing.discountRule.kind.Percentage': 'Percentage',
  'pricing.discountRule.kind.Amount': 'Fixed amount',
  'pricing.discountRule.active.yes': 'Active',
  'pricing.discountRule.active.no': 'Inactive',

  'pricing.problem.itemNotFound': 'No price-list item matches that address.',
  'pricing.problem.unitNotWellFormed':
    'A unit is a short lower-case word such as each, metre or hour.',
  'pricing.problem.amountNotWellFormed':
    'An amount is a non-negative value in rupees with at most two decimal places.',
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
  // not translated — awaiting native-speaker review
  'pricing.nav.priceLists': 'Price lists',

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

  /* Validating and publishing a version (E09-F01-5b). ------------------------------------------ */
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.validate': 'Check this version',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.check.stale':
    'This version has changed since the last check. Check it again before publishing.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish': 'Publish this version',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish.title': 'Publish this tax configuration?',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish.body':
    'Every invoice from the moment you publish is calculated on this version, and whatever is published now is superseded. Invoices already posted keep the version they were calculated on.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish.done': 'Published.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish.superseded':
    'It supersedes the version that was published before it.',
  // not translated — awaiting native-speaker review
  'pricing.tax.editor.publish.forbiddenAction': 'publishing a tax configuration version',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  // not translated — awaiting native-speaker review
  'pricing.problem.versionNotPublishable':
    'Only a draft can be published. Clone this version to a new draft to publish the change there.',
  // not translated — awaiting native-speaker review
  'pricing.problem.publishValidationFailed':
    'This version cannot be published yet. Correct what the checks below report.',
  // not translated — awaiting native-speaker review
  'pricing.problem.publishConflict':
    'Someone else published a version at the same moment. Here is where this version now stands.',
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
  /* The price-list register (#252). ------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.priceList.title': 'Price lists',
  // not translated — awaiting native-speaker review
  'pricing.priceList.body':
    'Every price list this organisation has, by its code. A list is never deleted — start a version to change what it prices.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.loading': 'the organisation’s price lists',
  // not translated — awaiting native-speaker review
  'pricing.priceList.caption': 'Every price list, by code',
  // not translated — awaiting native-speaker review
  'pricing.priceList.empty.title': 'No price list yet',
  // not translated — awaiting native-speaker review
  'pricing.priceList.empty': 'Create the first price list below.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.priceList.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.priceList.open': 'Open',
  // not translated — awaiting native-speaker review
  'pricing.priceList.open.label': 'Open the price list {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.rename': 'Rename',
  // not translated — awaiting native-speaker review
  'pricing.priceList.rename.label': 'Rename the price list {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.create.action': 'Create a price list',
  // not translated — awaiting native-speaker review
  'pricing.priceList.create.offlineAction': 'Creating a price list',
  // not translated — awaiting native-speaker review
  'pricing.priceList.rename.offlineAction': 'Renaming a price list',
  // not translated — awaiting native-speaker review
  'pricing.priceList.created': 'Created the price list.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.renamed': 'Renamed the price list.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.reread': 'Read it again',

  /* The create and rename form. ------------------------------------------------------------------ */
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.addTitle': 'Create a price list',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.editTitle': 'Rename {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.code.hint':
    'Letters, numbers and underscores. Used on seeds and exports once this list exists, and cannot be changed afterwards.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.code.fixedHint':
    'The code cannot change once a list exists — seeds and exports refer to it.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.priceList.form.save': 'Save',

  /* A list's versions. ----------------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.title': 'Versions of {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.body':
    'Every version of this price list, newest first. A version is never deleted — a change is a clone of it into a new draft.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.loading': 'the price list’s versions',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.caption': 'Every version, newest first',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.empty.title': 'No version yet',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.empty': 'Start the first version below.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.version': 'Version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.status': 'State',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.tax': 'Tax',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.roundOff': 'Round-off',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.threshold': 'Override threshold',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.branches': 'Branches',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.column.clonedFrom': 'Cloned from',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.clonedFrom.none': '—',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.clonedFrom.value': 'Version {versionNumber}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.branches.none': '—',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.tax.inclusive': 'Inclusive',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.tax.exclusive': 'Exclusive',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.status.Draft': 'Draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.status.Published': 'Published',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.status.Retired': 'Retired',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.status.unknown': 'Unknown',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.start.action': 'Start a draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.start.offlineAction': 'Starting a price-list draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.clone.action': 'Clone into a new draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.clone.label': 'Clone version {versionNumber} into a new draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.started': 'Started the draft.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.roundOff.none': 'No rounding',
  // not translated — awaiting native-speaker review
  'pricing.priceList.roundOff.nearestRupee': 'Nearest rupee',

  /* The version-conventions form, reused by E09-F01-7 to change one. -------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.startTitle': 'Start a draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.cloneTitle': 'Clone version {versionNumber} into a new draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.clonedFrom':
    'Cloned from version {versionNumber}. Every convention below starts from that version and can be changed before saving.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.name': 'Name',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.notes': 'Notes (optional)',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.effectiveFrom': 'First day',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.tax': 'Tax treatment',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.tax.inclusive': 'Tax inclusive',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.tax.exclusive': 'Tax exclusive',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.roundOff': 'Round-off rule',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.roundOff.none': 'No rounding',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.roundOff.nearestRupee': 'Nearest rupee',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.threshold': 'Override threshold',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.threshold.hint':
    'The variance above which a line needs the override permission, as a percentage.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.branches': 'Branches this version prices',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.branches.hint':
    'Choose every branch this version prices. Publication refuses a version pricing no branch.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.branches.forbiddenAction':
    'choosing which branches this version prices',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.save': 'Start the draft',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.editTitle': 'Version {versionNumber} conventions',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.form.saveChanges': 'Save',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.open': 'Open',
  // not translated — awaiting native-speaker review
  'pricing.priceList.version.open.label': 'Open version {versionNumber}',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  // not translated — awaiting native-speaker review
  'pricing.problem.branchNotFound': 'No branch matches that identifier.',
  // not translated — awaiting native-speaker review
  'pricing.problem.priceListNotFound': 'No price list matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.priceListChanged':
    'Someone else changed this price list while it was open here. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'pricing.problem.draftNumberConflict':
    'Another draft was started for this list at the same moment. Read the versions again and try once more.',
  // not translated — awaiting native-speaker review
  'pricing.problem.versionNotEditable':
    'Only a draft can be changed. Clone this version to a new draft to make the change there.',
  // not translated — awaiting native-speaker review
  'pricing.problem.versionChanged':
    'Someone else changed this version while it was open here. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'pricing.problem.versionNotFound': 'No tax configuration version matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.codeNotUnique': 'That code is already used. Choose a different one.',
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
    'A rate is a non-negative amount with at most four decimal places.',
  // not translated — awaiting native-speaker review
  'pricing.problem.componentDuplicated':
    'A tax code carries each component — CGST, SGST, IGST, cess — at most once.',
  // not translated — awaiting native-speaker review
  'pricing.problem.componentNotWellFormed': 'That is not a component this product knows.',
  // not translated — awaiting native-speaker review
  'pricing.problem.valueRequired': 'This is required.',
  // not translated — awaiting native-speaker review
  'pricing.problem.valueTooLong': 'That is longer than this field allows.',

  /* The price-list version editor: conventions, items and the tax-code picker (E09-F01-7, #268). --- */
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.loading': 'the price-list version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.heading': 'Price-list version {number}',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.readOnly':
    'This version is published. Every invoice since was calculated on it — clone it to a new draft to change what it says.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.retired':
    'This version is retired. It is the record of what an invoice was once calculated on.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.conventionsTitle': 'Conventions',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.editConventions': 'Change the version’s conventions',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.conventions.offlineAction': 'Changing the version’s conventions',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.saved': 'Saved the version’s conventions.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.reread': 'Read it again',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.itemsTitle': 'Items',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.items.empty': 'No item yet. Add the first one below.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.items.caption': 'Every item in this version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.discountRulesTitle': 'Discount rules',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.discountRulesComingSoon':
    'Adding, editing and removing a discount rule are not in this screen yet.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.discountRules.empty': 'No discount rule yet.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.discountRules.caption':
    'Every discount rule in this version, read-only',

  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.kind': 'Kind',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.baseRate': 'Base rate',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.unit': 'Unit',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.taxCode': 'Tax code',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.column.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.active.yes': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.active.no': 'Inactive',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.add': 'Add an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.add.offlineAction': 'Adding an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.edit': 'Edit {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.edit.offlineAction': 'Editing an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.remove': 'Remove {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.remove.offlineAction': 'Removing an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.remove.title': 'Remove an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.remove.body':
    'This removes the item from the draft. It can be added again, as a fresh row.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.saved': 'Saved the item {code}.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.removed': 'Removed the item {code}.',

  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.addTitle': 'Add an item',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.editTitle': 'Edit the item {code}',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter. What the catalogue’s price-list item code names.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.kind': 'What this prices',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.kind.Service': 'A service’s base charge',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.kind.Surcharge': 'A surcharge',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.kind.Material': 'A material',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.baseRate': 'Base rate',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.unit': 'Unit',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.unit.hint': 'A short lower-case word: each, metre, hour.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.taxCode': 'Tax code',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.taxCode.hint':
    'Choose one of the published tax configuration’s active codes, or type another.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.taxCode.noneHint':
    'No tax configuration is published yet, so no code can be suggested. The code is checked when this version is published, not now — the item still saves with a typed code.',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.taxCode.noneHint.link': 'Open the tax configuration',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.active.true': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.active.false': 'Inactive',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.priceListItem.form.save': 'Save',

  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.kind': 'Kind',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.maximumWithoutApproval': 'Maximum without approval',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.maximum': 'Maximum',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.column.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.kind.Percentage': 'Percentage',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.kind.Amount': 'Fixed amount',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.active.yes': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.active.no': 'Inactive',

  // not translated — awaiting native-speaker review
  'pricing.problem.itemNotFound': 'No price-list item matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.unitNotWellFormed':
    'A unit is a short lower-case word such as each, metre or hour.',
  // not translated — awaiting native-speaker review
  'pricing.problem.amountNotWellFormed':
    'An amount is a non-negative value in rupees with at most two decimal places.',
}
