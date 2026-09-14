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
  'pricing.priceList.editor.discountRules.empty': 'No discount rule yet.',
  'pricing.priceList.editor.discountRules.caption': 'Every discount rule in this version',

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
  'pricing.discountRule.add': 'Add a discount rule',
  'pricing.discountRule.add.offlineAction': 'Adding a discount rule',
  'pricing.discountRule.edit': 'Edit {code}',
  'pricing.discountRule.edit.offlineAction': 'Editing a discount rule',
  'pricing.discountRule.remove': 'Remove {code}',
  'pricing.discountRule.remove.offlineAction': 'Removing a discount rule',
  'pricing.discountRule.remove.title': 'Remove a discount rule',
  'pricing.discountRule.remove.body':
    'This removes the rule from the draft. It can be added again, as a fresh row.',
  'pricing.discountRule.saved': 'Saved the discount rule {code}.',
  'pricing.discountRule.removed': 'Removed the discount rule {code}.',

  'pricing.discountRule.form.addTitle': 'Add a discount rule',
  'pricing.discountRule.form.editTitle': 'Edit the discount rule {code}',
  'pricing.discountRule.form.code': 'Code',
  'pricing.discountRule.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter. What a line’s discount cites.',
  'pricing.discountRule.form.description': 'Description',
  'pricing.discountRule.form.kind': 'How the discount is given',
  'pricing.discountRule.form.kind.Percentage':
    'A percentage of the line’s gross amount: its base and its surcharges together',
  'pricing.discountRule.form.kind.Amount': 'A fixed amount off the line',
  'pricing.discountRule.form.maximumWithoutApproval': 'Maximum without approval',
  'pricing.discountRule.form.maximumWithoutApproval.hint':
    'The most a counter may give on their own authority.',
  'pricing.discountRule.form.maximum': 'Maximum',
  'pricing.discountRule.form.maximum.hint': 'The most anybody may give, with approval.',
  'pricing.discountRule.form.active': 'Status',
  'pricing.discountRule.form.active.true': 'Active',
  'pricing.discountRule.form.active.false': 'Inactive',
  'pricing.discountRule.form.reason': 'Note (optional)',
  'pricing.discountRule.form.save': 'Save',

  'pricing.problem.itemNotFound': 'No price-list item matches that address.',
  'pricing.problem.unitNotWellFormed':
    'A unit is a short lower-case word such as each, metre or hour.',
  'pricing.problem.amountNotWellFormed':
    'An amount is a non-negative value in rupees with at most two decimal places.',
  'pricing.problem.discountBoundsNotOrdered':
    'The maximum without approval cannot be more than the maximum anybody may give.',
  'pricing.problem.discountRuleNotFound': 'No discount rule matches that address.',

  /* Validating and publishing a price-list version (E09-F01-7b). -------------------------------- */
  'pricing.priceList.editor.checksTitle': 'Checks and publishing',
  'pricing.priceList.editor.validate': 'Check this version',
  'pricing.priceList.editor.validate.offlineAction': 'Checking a price-list version',
  'pricing.priceList.editor.check.stale':
    'This version has changed since the last check. Check it again before publishing.',
  'pricing.priceList.editor.publish': 'Publish this version',
  'pricing.priceList.editor.publish.offlineAction': 'Publishing a price-list version',
  'pricing.priceList.editor.publish.title': 'Publish this price-list version?',
  'pricing.priceList.editor.publish.body':
    'Every invoice from the moment you publish is calculated on this version for the branches it prices, and whatever previously priced those branches is superseded. Invoices already posted keep the version they were calculated on.',
  'pricing.priceList.editor.publish.done': 'Published.',
  'pricing.priceList.editor.publish.superseded':
    'It supersedes the version that was published before it.',
  'pricing.priceList.editor.publish.forbiddenAction': 'publishing a price-list version',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  'pricing.problem.branchPublishConflict':
    'Another price list’s version already prices one of this version’s branches, published at the same moment. A branch is priced by one published version at a time; read what is published now before deciding whether this draft is still wanted.',

  /* The pricing preview and the accountant's test-case shapes before publish (E09-F01-8b). ------ */
  'pricing.nav.preview': 'Pricing preview',
  'pricing.priceList.editor.preview': 'See the effect before publishing',

  'pricing.preview.heading': 'Pricing preview',
  'pricing.preview.loading': 'the pricing preview',
  'pricing.preview.retry': 'Try again',

  'pricing.preview.form.title': 'What to price',
  'pricing.preview.form.priceListId': 'Price list',
  'pricing.preview.form.priceListVersionId': 'Version',
  'pricing.preview.form.priceListVersionId.option': 'v{number} — {name} ({status})',
  'pricing.preview.form.taxConfigurationVersionId': 'Tax configuration version',
  'pricing.preview.form.taxConfigurationVersionId.hint':
    'Leave this as the published one unless you are trying a draft tax configuration.',
  'pricing.preview.form.taxConfigurationVersionId.publishedOption': 'The published one',
  'pricing.preview.form.taxConfigurationVersionId.option': 'v{number} — {name} ({status})',
  'pricing.preview.form.branchId': 'Branch',
  'pricing.preview.form.on': 'Date',
  'pricing.preview.form.placeOfSupplyStateCode': 'Place of supply state code',
  'pricing.preview.form.placeOfSupplyStateCode.hint':
    'The two-digit GST state code of where the work is supplied. The same code as the branch’s own registration prices intra-state; a different one prices inter-state.',

  'pricing.preview.lines.title': 'Lines',
  'pricing.preview.lines.loading': 'this version’s items and discount rules',
  'pricing.preview.lines.empty.title': 'No lines yet',
  'pricing.preview.lines.empty':
    'Add a line, or press one of the accountant’s cases below — pressing a case adds one for you, built from this version’s own items.',
  'pricing.preview.lines.caption': 'The lines to price',
  'pricing.preview.line.column.itemCode': 'Item',
  'pricing.preview.line.column.quantity': 'Quantity',
  'pricing.preview.line.column.surcharges': 'Surcharges',
  'pricing.preview.line.column.discount': 'Discount',
  'pricing.preview.line.column.override': 'Override',
  'pricing.preview.line.edit': 'Edit {itemCode}',
  'pricing.preview.line.remove': 'Remove {itemCode}',
  'pricing.preview.line.add': 'Add a line',
  'pricing.preview.line.add.offlineAction': 'Adding a line',
  'pricing.preview.line.edit.offlineAction': 'Editing a line',

  'pricing.preview.line.form.addTitle': 'Add a line',
  'pricing.preview.line.form.editTitle': 'Edit a line',
  'pricing.preview.line.form.itemCode': 'Item',
  'pricing.preview.line.form.quantity': 'Quantity',
  'pricing.preview.line.form.surcharges': 'Surcharges',
  'pricing.preview.line.form.discountRuleCode': 'Discount',
  'pricing.preview.line.form.discountRuleCode.hint': 'One of this version’s own discount rules.',
  'pricing.preview.line.form.discountRuleCode.none': 'No discount',
  'pricing.preview.line.form.discountValue': 'Discount value',
  'pricing.preview.line.form.discountReason': 'Reason (optional)',
  'pricing.preview.line.form.override': 'Price this line at a different rate',
  'pricing.preview.line.form.overrideRate': 'Override rate',
  'pricing.preview.line.form.overrideRate.hint':
    'In place of the item’s catalogue rate. Within the version’s own threshold, this needs a reason; beyond it, it needs the “Override a price” permission on a recently verified session.',
  'pricing.preview.line.form.overrideReason': 'Reason',
  'pricing.preview.line.form.override.note':
    'An override beyond the version’s threshold, or a discount beyond its rule’s counter maximum, is refused unless the session holds “Override a price” and was recently re-verified.',
  'pricing.preview.line.form.save': 'Save this line',

  'pricing.preview.cases.title': 'Try one of the accountant’s cases',
  'pricing.preview.cases.hint':
    'Each one fills the form with a shape the accountant already knows — it names no expected figure of its own; the figures come from this version’s own rates once the preview runs.',
  'pricing.preview.case.intraStateExclusive': 'Intra-state, exclusive of tax',
  'pricing.preview.case.intraStateExclusive.hint':
    'One line, priced at the catalogue rate, supplied within the same state as the branch’s registration.',
  'pricing.preview.case.interState': 'Inter-state',
  'pricing.preview.case.interState.hint':
    'The same line, supplied to a different state — a single integrated-tax component in place of the pair.',
  'pricing.preview.case.inclusive': 'Tax-inclusive version',
  'pricing.preview.case.inclusive.hint':
    'The same line, to run against a version whose price is quoted inclusive of tax — the taxable value is backed out of the quoted amount.',
  'pricing.preview.case.percentageDiscount': 'A percentage discount',
  'pricing.preview.case.percentageDiscount.hint':
    'Names one of this version’s percentage discount rules, at the most a counter may give on its own authority.',
  'pricing.preview.case.amountDiscount': 'An amount discount',
  'pricing.preview.case.amountDiscount.hint':
    'Names one of this version’s fixed-amount discount rules, at the most a counter may give on its own authority.',
  'pricing.preview.case.surcharge': 'A surcharge',
  'pricing.preview.case.surcharge.hint':
    'Adds one of this version’s own surcharge items to the line.',
  'pricing.preview.case.quantityAboveOne': 'A quantity above one',
  'pricing.preview.case.quantityAboveOne.hint': 'The same line, priced twice over.',
  'pricing.preview.case.overrideWithinThreshold': 'An override within the threshold',
  'pricing.preview.case.overrideWithinThreshold.hint':
    'A rate below this version’s override threshold — priced with a reason, needing no permission.',
  'pricing.preview.case.overrideBeyondThreshold': 'An override beyond the threshold',
  'pricing.preview.case.overrideBeyondThreshold.hint':
    'A rate beyond this version’s override threshold — refused without “Override a price” on a recently verified session.',
  'pricing.preview.case.nilRated': 'A nil-rated item',
  'pricing.preview.case.nilRated.hint':
    'Choose an item whose tax code carries no rate to see a line with no tax component at all.',
  'pricing.preview.case.roundOffTwoLines': 'Round-off, over two lines',
  'pricing.preview.case.roundOffTwoLines.hint':
    'Two lines, each rounded on its own before the document totals them — the reconciliation the round-off line makes visible.',
  'pricing.preview.case.override.reason':
    'Filled by a case shape — review before running the preview.',

  'pricing.preview.run': 'Run the preview',
  'pricing.preview.run.offlineAction': 'Running a pricing preview',
  'pricing.preview.configurationMissing.gstLink': 'Check the branch’s GST registrations',
  'pricing.preview.configurationMissing.versionLink': 'Check this version’s branches',

  'pricing.preview.result.heading': 'What the engine priced',
  'pricing.preview.result.case': 'Case shown: {case}.',
  'pricing.preview.result.none': 'None',
  'pricing.preview.result.scheme': 'Scheme',
  'pricing.preview.result.scheme.IntraState': 'Intra-state',
  'pricing.preview.result.scheme.InterState': 'Inter-state',
  'pricing.preview.result.taxInclusive': 'Tax treatment',
  'pricing.preview.result.priceListVersionId': 'Price-list version',
  'pricing.preview.result.taxConfigurationVersionId': 'Tax configuration version',
  'pricing.preview.result.gstRegistrationId': 'GST registration',
  'pricing.preview.result.calculatedAt': 'Calculated at',
  'pricing.preview.result.lines.caption': 'What was priced',
  'pricing.preview.result.column.itemCode': 'Item',
  'pricing.preview.result.column.quantity': 'Quantity',
  'pricing.preview.result.column.catalogueRate': 'Catalogue rate',
  'pricing.preview.result.column.appliedRate': 'Applied rate',
  'pricing.preview.result.column.base': 'Base',
  'pricing.preview.result.column.surcharges': 'Surcharges',
  'pricing.preview.result.column.discount': 'Discount',
  'pricing.preview.result.column.gross': 'Gross',
  'pricing.preview.result.column.taxableValue': 'Taxable value',
  'pricing.preview.result.column.taxCode': 'Tax code',
  'pricing.preview.result.column.taxes': 'Tax components',
  'pricing.preview.result.column.taxTotal': 'Tax total',
  'pricing.preview.result.column.lineTotal': 'Line total',
  'pricing.preview.result.column.variance': 'Variance',
  'pricing.preview.result.column.approvalExercised': 'Approval',
  'pricing.preview.result.approvalExercised.yes': 'Approval used',
  'pricing.preview.result.approvalExercised.no': 'Within authority',
  'pricing.preview.result.totalsTitle': 'Document totals',
  'pricing.preview.result.totals.subtotal': 'Subtotal',
  'pricing.preview.result.totals.discountTotal': 'Discount',
  'pricing.preview.result.totals.taxableValue': 'Taxable value',
  'pricing.preview.result.totals.centralTax': 'Central tax',
  'pricing.preview.result.totals.stateTax': 'State tax',
  'pricing.preview.result.totals.integratedTax': 'Integrated tax',
  'pricing.preview.result.totals.cess': 'Cess',
  'pricing.preview.result.totals.roundOff': 'Round-off',
  'pricing.preview.result.totals.grandTotal': 'Grand total',

  'pricing.problem.approvalRequired':
    'This is beyond what a counter may give on its own authority. Ask the Owner or a Branch Manager — the two roles who may override a price — to run this from a recently verified session, or to grant the permission.',
  'pricing.problem.discountAboveMaximum':
    'This discount is beyond the rule’s own maximum. No approval can raise it — choose a smaller value or a different rule.',
  'pricing.problem.discountExceedsLine': 'This discount is more than the line itself is worth.',
  'pricing.problem.discountRuleNotInForce':
    'This discount rule has been retired from this version. Choose one still active on it.',
  'pricing.problem.itemNotASurcharge':
    'This item is not a surcharge, so it cannot be added as one.',
  'pricing.problem.surchargeTaxedDifferently':
    'This surcharge is taxed differently from the line it was added to, so it cannot be combined with it.',
  'pricing.problem.itemNotPriced': 'This item has no price in the chosen version.',
  'pricing.problem.quantityNotPositive': 'A quantity is a positive number.',
  'pricing.problem.lineKeyDuplicated':
    'Two lines shared the same key. Remove one and add it again.',
  'pricing.problem.linesRequired': 'At least one line is needed to price anything.',
  'pricing.problem.overrideRateNotWellFormed':
    'An override rate is a non-negative value in rupees with at most two decimal places.',
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
  'pricing.priceList.editor.discountRules.empty': 'No discount rule yet.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.discountRules.caption': 'Every discount rule in this version',

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
  'pricing.discountRule.add': 'Add a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.add.offlineAction': 'Adding a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.edit': 'Edit {code}',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.edit.offlineAction': 'Editing a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.remove': 'Remove {code}',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.remove.offlineAction': 'Removing a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.remove.title': 'Remove a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.remove.body':
    'This removes the rule from the draft. It can be added again, as a fresh row.',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.saved': 'Saved the discount rule {code}.',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.removed': 'Removed the discount rule {code}.',

  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.addTitle': 'Add a discount rule',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.editTitle': 'Edit the discount rule {code}',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.code': 'Code',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.code.hint':
    'Upper snake case — capital letters, digits and underscores, beginning with a letter. What a line’s discount cites.',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.description': 'Description',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.kind': 'How the discount is given',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.kind.Percentage':
    'A percentage of the line’s gross amount: its base and its surcharges together',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.kind.Amount': 'A fixed amount off the line',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.maximumWithoutApproval': 'Maximum without approval',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.maximumWithoutApproval.hint':
    'The most a counter may give on their own authority.',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.maximum': 'Maximum',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.maximum.hint': 'The most anybody may give, with approval.',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.active': 'Status',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.active.true': 'Active',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.active.false': 'Inactive',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.reason': 'Note (optional)',
  // not translated — awaiting native-speaker review
  'pricing.discountRule.form.save': 'Save',

  // not translated — awaiting native-speaker review
  'pricing.problem.itemNotFound': 'No price-list item matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.unitNotWellFormed':
    'A unit is a short lower-case word such as each, metre or hour.',
  // not translated — awaiting native-speaker review
  'pricing.problem.amountNotWellFormed':
    'An amount is a non-negative value in rupees with at most two decimal places.',
  // not translated — awaiting native-speaker review
  'pricing.problem.discountBoundsNotOrdered':
    'The maximum without approval cannot be more than the maximum anybody may give.',
  // not translated — awaiting native-speaker review
  'pricing.problem.discountRuleNotFound': 'No discount rule matches that address.',

  /* Validating and publishing a price-list version (E09-F01-7b). -------------------------------- */
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.checksTitle': 'Checks and publishing',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.validate': 'Check this version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.validate.offlineAction': 'Checking a price-list version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.check.stale':
    'This version has changed since the last check. Check it again before publishing.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish': 'Publish this version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.offlineAction': 'Publishing a price-list version',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.title': 'Publish this price-list version?',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.body':
    'Every invoice from the moment you publish is calculated on this version for the branches it prices, and whatever previously priced those branches is superseded. Invoices already posted keep the version they were calculated on.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.done': 'Published.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.superseded':
    'It supersedes the version that was published before it.',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.publish.forbiddenAction': 'publishing a price-list version',

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  // not translated — awaiting native-speaker review
  'pricing.problem.branchPublishConflict':
    'Another price list’s version already prices one of this version’s branches, published at the same moment. A branch is priced by one published version at a time; read what is published now before deciding whether this draft is still wanted.',

  /* The pricing preview and the accountant's test-case shapes before publish (E09-F01-8b). ------ */
  // not translated — awaiting native-speaker review
  'pricing.nav.preview': 'Pricing preview',
  // not translated — awaiting native-speaker review
  'pricing.priceList.editor.preview': 'See the effect before publishing',

  // not translated — awaiting native-speaker review
  'pricing.preview.heading': 'Pricing preview',
  // not translated — awaiting native-speaker review
  'pricing.preview.loading': 'the pricing preview',
  // not translated — awaiting native-speaker review
  'pricing.preview.retry': 'Try again',

  // not translated — awaiting native-speaker review
  'pricing.preview.form.title': 'What to price',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.priceListId': 'Price list',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.priceListVersionId': 'Version',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.priceListVersionId.option': 'v{number} — {name} ({status})',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.taxConfigurationVersionId': 'Tax configuration version',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.taxConfigurationVersionId.hint':
    'Leave this as the published one unless you are trying a draft tax configuration.',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.taxConfigurationVersionId.publishedOption': 'The published one',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.taxConfigurationVersionId.option': 'v{number} — {name} ({status})',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.branchId': 'Branch',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.on': 'Date',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.placeOfSupplyStateCode': 'Place of supply state code',
  // not translated — awaiting native-speaker review
  'pricing.preview.form.placeOfSupplyStateCode.hint':
    'The two-digit GST state code of where the work is supplied. The same code as the branch’s own registration prices intra-state; a different one prices inter-state.',

  // not translated — awaiting native-speaker review
  'pricing.preview.lines.title': 'Lines',
  // not translated — awaiting native-speaker review
  'pricing.preview.lines.loading': 'this version’s items and discount rules',
  // not translated — awaiting native-speaker review
  'pricing.preview.lines.empty.title': 'No lines yet',
  // not translated — awaiting native-speaker review
  'pricing.preview.lines.empty':
    'Add a line, or press one of the accountant’s cases below — pressing a case adds one for you, built from this version’s own items.',
  // not translated — awaiting native-speaker review
  'pricing.preview.lines.caption': 'The lines to price',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.column.itemCode': 'Item',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.column.quantity': 'Quantity',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.column.surcharges': 'Surcharges',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.column.discount': 'Discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.column.override': 'Override',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.edit': 'Edit {itemCode}',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.remove': 'Remove {itemCode}',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.add': 'Add a line',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.add.offlineAction': 'Adding a line',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.edit.offlineAction': 'Editing a line',

  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.addTitle': 'Add a line',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.editTitle': 'Edit a line',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.itemCode': 'Item',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.quantity': 'Quantity',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.surcharges': 'Surcharges',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.discountRuleCode': 'Discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.discountRuleCode.hint': 'One of this version’s own discount rules.',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.discountRuleCode.none': 'No discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.discountValue': 'Discount value',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.discountReason': 'Reason (optional)',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.override': 'Price this line at a different rate',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.overrideRate': 'Override rate',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.overrideRate.hint':
    'In place of the item’s catalogue rate. Within the version’s own threshold, this needs a reason; beyond it, it needs the “Override a price” permission on a recently verified session.',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.overrideReason': 'Reason',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.override.note':
    'An override beyond the version’s threshold, or a discount beyond its rule’s counter maximum, is refused unless the session holds “Override a price” and was recently re-verified.',
  // not translated — awaiting native-speaker review
  'pricing.preview.line.form.save': 'Save this line',

  // not translated — awaiting native-speaker review
  'pricing.preview.cases.title': 'Try one of the accountant’s cases',
  // not translated — awaiting native-speaker review
  'pricing.preview.cases.hint':
    'Each one fills the form with a shape the accountant already knows — it names no expected figure of its own; the figures come from this version’s own rates once the preview runs.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.intraStateExclusive': 'Intra-state, exclusive of tax',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.intraStateExclusive.hint':
    'One line, priced at the catalogue rate, supplied within the same state as the branch’s registration.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.interState': 'Inter-state',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.interState.hint':
    'The same line, supplied to a different state — a single integrated-tax component in place of the pair.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.inclusive': 'Tax-inclusive version',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.inclusive.hint':
    'The same line, to run against a version whose price is quoted inclusive of tax — the taxable value is backed out of the quoted amount.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.percentageDiscount': 'A percentage discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.percentageDiscount.hint':
    'Names one of this version’s percentage discount rules, at the most a counter may give on its own authority.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.amountDiscount': 'An amount discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.amountDiscount.hint':
    'Names one of this version’s fixed-amount discount rules, at the most a counter may give on its own authority.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.surcharge': 'A surcharge',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.surcharge.hint':
    'Adds one of this version’s own surcharge items to the line.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.quantityAboveOne': 'A quantity above one',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.quantityAboveOne.hint': 'The same line, priced twice over.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.overrideWithinThreshold': 'An override within the threshold',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.overrideWithinThreshold.hint':
    'A rate below this version’s override threshold — priced with a reason, needing no permission.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.overrideBeyondThreshold': 'An override beyond the threshold',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.overrideBeyondThreshold.hint':
    'A rate beyond this version’s override threshold — refused without “Override a price” on a recently verified session.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.nilRated': 'A nil-rated item',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.nilRated.hint':
    'Choose an item whose tax code carries no rate to see a line with no tax component at all.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.roundOffTwoLines': 'Round-off, over two lines',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.roundOffTwoLines.hint':
    'Two lines, each rounded on its own before the document totals them — the reconciliation the round-off line makes visible.',
  // not translated — awaiting native-speaker review
  'pricing.preview.case.override.reason':
    'Filled by a case shape — review before running the preview.',

  // not translated — awaiting native-speaker review
  'pricing.preview.run': 'Run the preview',
  // not translated — awaiting native-speaker review
  'pricing.preview.run.offlineAction': 'Running a pricing preview',
  // not translated — awaiting native-speaker review
  'pricing.preview.configurationMissing.gstLink': 'Check the branch’s GST registrations',
  // not translated — awaiting native-speaker review
  'pricing.preview.configurationMissing.versionLink': 'Check this version’s branches',

  // not translated — awaiting native-speaker review
  'pricing.preview.result.heading': 'What the engine priced',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.case': 'Case shown: {case}.',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.none': 'None',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.scheme': 'Scheme',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.scheme.IntraState': 'Intra-state',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.scheme.InterState': 'Inter-state',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.taxInclusive': 'Tax treatment',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.priceListVersionId': 'Price-list version',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.taxConfigurationVersionId': 'Tax configuration version',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.gstRegistrationId': 'GST registration',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.calculatedAt': 'Calculated at',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.lines.caption': 'What was priced',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.itemCode': 'Item',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.quantity': 'Quantity',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.catalogueRate': 'Catalogue rate',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.appliedRate': 'Applied rate',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.base': 'Base',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.surcharges': 'Surcharges',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.discount': 'Discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.gross': 'Gross',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.taxableValue': 'Taxable value',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.taxCode': 'Tax code',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.taxes': 'Tax components',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.taxTotal': 'Tax total',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.lineTotal': 'Line total',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.variance': 'Variance',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.column.approvalExercised': 'Approval',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.approvalExercised.yes': 'Approval used',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.approvalExercised.no': 'Within authority',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totalsTitle': 'Document totals',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.subtotal': 'Subtotal',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.discountTotal': 'Discount',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.taxableValue': 'Taxable value',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.centralTax': 'Central tax',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.stateTax': 'State tax',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.integratedTax': 'Integrated tax',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.cess': 'Cess',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.roundOff': 'Round-off',
  // not translated — awaiting native-speaker review
  'pricing.preview.result.totals.grandTotal': 'Grand total',

  // not translated — awaiting native-speaker review
  'pricing.problem.approvalRequired':
    'This is beyond what a counter may give on its own authority. Ask the Owner or a Branch Manager — the two roles who may override a price — to run this from a recently verified session, or to grant the permission.',
  // not translated — awaiting native-speaker review
  'pricing.problem.discountAboveMaximum':
    'This discount is beyond the rule’s own maximum. No approval can raise it — choose a smaller value or a different rule.',
  // not translated — awaiting native-speaker review
  'pricing.problem.discountExceedsLine': 'This discount is more than the line itself is worth.',
  // not translated — awaiting native-speaker review
  'pricing.problem.discountRuleNotInForce':
    'This discount rule has been retired from this version. Choose one still active on it.',
  // not translated — awaiting native-speaker review
  'pricing.problem.itemNotASurcharge':
    'This item is not a surcharge, so it cannot be added as one.',
  // not translated — awaiting native-speaker review
  'pricing.problem.surchargeTaxedDifferently':
    'This surcharge is taxed differently from the line it was added to, so it cannot be combined with it.',
  // not translated — awaiting native-speaker review
  'pricing.problem.itemNotPriced': 'This item has no price in the chosen version.',
  // not translated — awaiting native-speaker review
  'pricing.problem.quantityNotPositive': 'A quantity is a positive number.',
  // not translated — awaiting native-speaker review
  'pricing.problem.lineKeyDuplicated':
    'Two lines shared the same key. Remove one and add it again.',
  // not translated — awaiting native-speaker review
  'pricing.problem.linesRequired': 'At least one line is needed to price anything.',
  // not translated — awaiting native-speaker review
  'pricing.problem.overrideRateNotWellFormed':
    'An override rate is a non-negative value in rupees with at most two decimal places.',
}
