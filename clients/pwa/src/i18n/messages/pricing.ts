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

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  'pricing.problem.codeNotUnique':
    'This code is already used by another price list. Choose a different one.',
  'pricing.problem.codeNotWellFormed':
    'Use letters, numbers and underscores only, starting with a letter.',
  'pricing.problem.branchNotFound': 'No branch matches that identifier.',
  'pricing.problem.valueRequired': 'This has to be answered before the draft can be saved.',
  'pricing.problem.priceListNotFound': 'No price list matches that address.',
  'pricing.problem.priceListChanged':
    'Someone else changed this price list while it was open here. Read it again to see what changed.',
  'pricing.problem.draftNumberConflict':
    'Another draft was started for this list at the same moment. Read the versions again and try once more.',
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

  /* Refusals, in the shop's words — consumed through `billingProblems.ts`. ---------------------- */
  // not translated — awaiting native-speaker review
  'pricing.problem.codeNotUnique':
    'This code is already used by another price list. Choose a different one.',
  // not translated — awaiting native-speaker review
  'pricing.problem.codeNotWellFormed':
    'Use letters, numbers and underscores only, starting with a letter.',
  // not translated — awaiting native-speaker review
  'pricing.problem.branchNotFound': 'No branch matches that identifier.',
  // not translated — awaiting native-speaker review
  'pricing.problem.valueRequired': 'This has to be answered before the draft can be saved.',
  // not translated — awaiting native-speaker review
  'pricing.problem.priceListNotFound': 'No price list matches that address.',
  // not translated — awaiting native-speaker review
  'pricing.problem.priceListChanged':
    'Someone else changed this price list while it was open here. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'pricing.problem.draftNumberConflict':
    'Another draft was started for this list at the same moment. Read the versions again and try once more.',
}
