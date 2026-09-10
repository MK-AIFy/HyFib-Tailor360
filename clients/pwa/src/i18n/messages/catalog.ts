/**
 * Catalogue messages — versions, the hierarchy, service types and publication.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. Two of them decide
 * most of the wording here:
 *
 *  - **Say what will happen to the shop, not to the row.** "Every counter will offer this from the
 *    moment you publish" is what an administrator needs to weigh; "the status will change to
 *    Published" is what the database is about to do.
 *  - **A refusal explains the rule, not the code.** The server answers
 *    `catalog.category-branches-not-subset`; the person reads that a sub-category cannot be offered
 *    where its parent is not, and which branch that is.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * The same reason recorded for the authentication and administration families. These sentences
 * decide what a shop can sell and are read while somebody is changing it; they are held back for
 * native-speaker review with the rest of the operational vocabulary.
 */
export const catalogEn = {
  /* The versions. --------------------------------------------------------------------------- */
  'catalog.title': 'Catalogue',
  'catalog.body':
    'What this shop offers, and where. A version is drafted, checked and published as a whole, so a counter never sees half a change.',
  'catalog.versions.caption': 'Every version of the catalogue, newest first',
  'catalog.versions.loading': 'the catalogue versions',
  'catalog.versions.empty':
    'No catalogue has been drafted yet. Until one is published, no counter can take an order.',
  'catalog.versions.column.version': 'Version',
  'catalog.versions.column.name': 'What changed',
  'catalog.versions.column.status': 'State',
  'catalog.versions.column.categories': 'Categories',
  'catalog.versions.column.published': 'Published',
  'catalog.version': 'Version {number}',
  'catalog.status.Draft': 'Draft',
  'catalog.status.Published': 'Published',
  'catalog.status.Retired': 'Retired',
  'catalog.status.unknown': 'Unknown',
  'catalog.versions.open': 'Open version {number}',
  'catalog.draft.start': 'Start a draft',
  'catalog.draft.clone': 'Start a draft from this version',
  'catalog.draft.title': 'Start a draft?',
  'catalog.draft.body':
    'A draft is a working copy. Nothing a counter sees changes until it is published.',
  'catalog.draft.name': 'What is this change for?',
  'catalog.draft.name.hint': 'A sentence somebody will read in a year when they ask what changed.',
  'catalog.draft.started': 'Draft started.',
  'catalog.notPublished':
    'Nothing is published, so no counter can take an order yet. Draft a catalogue, check it and publish it.',

  /* One version, and its tree. ----------------------------------------------------------------- */
  'catalog.editor.loading': 'this catalogue version',
  'catalog.editor.notFound': 'No catalogue version matches that address.',
  'catalog.editor.caption': 'The hierarchy of this catalogue version',
  'catalog.editor.empty':
    'This version has nothing in it yet. Add a category, then the things a counter can order under it.',
  'catalog.editor.readOnly':
    'This version is published and its structure cannot be changed. Start a draft from it to change what the shop offers; only a label can be corrected here, and every correction is recorded.',
  'catalog.editor.retired':
    'This version has been retired. It is kept because orders were placed against it, and nothing about it can be changed.',
  'catalog.column.code': 'Code',
  'catalog.column.name': 'Name',
  'catalog.column.kind': 'Kind',
  'catalog.column.branches': 'Offered at',
  'catalog.column.orderable': 'Orderable',
  'catalog.column.findings': 'Checks',
  'catalog.kind.category': 'Category',
  'catalog.kind.grouping': 'Grouping only',
  'catalog.kind.serviceType': 'Orderable',
  'catalog.grouping.hint':
    'A grouping category holds other categories and is not ordered directly.',
  'catalog.branches.none': 'Nowhere',
  'catalog.branches.count': '{count, plural, one {1 branch} other {# branches}}',
  'catalog.branches.outside':
    'Offered at {count, plural, one {a branch} other {# branches}} where its parent is not. A sub-category is reached through its parent, so publication will refuse this.',
  'catalog.orderable.yes': 'Yes',
  'catalog.orderable.no': 'Not yet',
  'catalog.orderable.missing':
    'Not orderable: {count, plural, one {one link} other {# links}} still missing.',
  'catalog.link.measurementTemplateId': 'what is measured',
  'catalog.link.workflowDefinitionId': 'how it is made',
  'catalog.link.designOptionGroupIds': 'what the customer chooses from',
  'catalog.link.priceListItemCode': 'what it costs',
  'catalog.link.qcChecklistTemplateId': 'how it is checked',
  'catalog.allowIncomplete':
    'Publish this even with links missing. It will be visibly not orderable until they are set, rather than holding up the whole catalogue.',

  /* Adding and editing. ------------------------------------------------------------------------ */
  'catalog.category.add': 'Add a category',
  'catalog.category.addUnder': 'Add a category under {name}',
  'catalog.category.edit': 'Edit {name}',
  'catalog.category.remove': 'Remove {name}',
  'catalog.category.addTitle': 'A new category',
  'catalog.category.editTitle': 'Editing {name}',
  'catalog.serviceType.add': 'Add something orderable under {name}',
  'catalog.serviceType.edit': 'Edit {name}',
  'catalog.serviceType.remove': 'Remove {name}',
  'catalog.serviceType.addTitle': 'A new orderable service',
  'catalog.serviceType.editTitle': 'Editing {name}',
  'catalog.form.code': 'Code',
  'catalog.form.code.hint':
    'Set once and never again: it is what every order, invoice and report files this under.',
  'catalog.form.code.fixedHint':
    'A code cannot be changed. Orders already placed are filed under it.',
  'catalog.form.name': 'Name',
  'catalog.form.nameTamil': 'Name in Tamil',
  'catalog.form.description': 'Description',
  'catalog.form.displayOrder': 'Where it appears',
  'catalog.form.parent': 'Inside',
  'catalog.form.parent.none': 'At the top level',
  'catalog.form.branches': 'Offered at',
  'catalog.form.branches.hint':
    'Leave every branch unticked and it is offered nowhere. A sub-category can only be offered where its parent is.',
  'catalog.form.featureFlag': 'Only when this feature setting is on',
  'catalog.form.duration': 'How many days it usually takes',
  'catalog.form.intakeWarning': 'What to tell the customer at the counter',
  'catalog.form.reason': 'Why are you making this change?',
  'catalog.form.save': 'Save',
  'catalog.form.links': 'What ordering this means',
  'catalog.form.links.hint':
    'Five links decide what happens when a counter orders this. There is no picker for them yet — the catalogues they would choose from arrive with their own screens — so each is entered as the identifier it is.',
  'catalog.correct': 'Correct the labels of {name}',
  'catalog.correct.title': 'Correct the labels of {name}',
  'catalog.correct.body':
    'A label, a Tamil label, a description and a position. Nothing here changes what ordering this means: the price, the work and every report all follow the code, which does not change.',
  'catalog.correct.reason.hint':
    'Required. Orders were placed against this version, so every correction to it is recorded with who made it and why.',
  'catalog.correct.save': 'Correct the labels',
  'catalog.corrected': 'Corrected the labels of {name}.',
  'catalog.saved': 'Saved {name}.',
  'catalog.removed': 'Removed {name}.',
  'catalog.remove.title': 'Remove this from the draft?',
  'catalog.remove.body':
    'It is removed from this draft only. Published versions keep it, and every order already placed against it is untouched.',
  'catalog.remove.reason': 'Why are you removing this?',

  /* The label correction a published version admits. ------------------------------------------- */
  'catalog.presentation': 'Correct the label',
  'catalog.presentation.title': 'Correct this label?',
  'catalog.presentation.body':
    'Only what a person reads changes — the name, the Tamil name, the description and where it appears. Nothing about what ordering it means, because an order already placed against it was placed against what it meant then.',
  'catalog.presentation.reason': 'Why are you correcting this?',
  'catalog.presentation.done': 'Corrected {name}.',

  /* Checking and publishing. ------------------------------------------------------------------- */
  'catalog.check': 'Check this version',
  'catalog.check.loading': 'the checks that run before publication',
  'catalog.check.ready': 'Every check passed. This version can be published.',
  'catalog.check.counts':
    '{errors, plural, =0 {No problem} one {1 problem} other {# problems}} and {warnings, plural, =0 {nothing} one {1 thing} other {# things}} worth knowing.',
  'catalog.check.blocked':
    'Publication is refused until the problems below are corrected. Each one is shown beside the thing it is about.',
  'catalog.check.warningsOnly':
    'Nothing here refuses publication. Each is shown beside the thing it is about, so it can be looked at before the catalogue goes out.',
  'catalog.check.unanchored':
    'This was reported about something not on this screen. The version may have changed since the check ran — run it again to see where it stands.',
  'catalog.check.stale':
    'The check ran against an earlier state of this version. Run it again before publishing.',
  'catalog.check.finding.error': 'Problem',
  'catalog.check.finding.warning': 'Worth knowing',
  'catalog.check.validator': 'reported by {validator}',

  'catalog.publish': 'Publish this version',
  'catalog.publish.title': 'Publish this catalogue?',
  'catalog.publish.body':
    'Every counter offers this from the moment you publish, and whatever is published now is superseded. Orders already placed keep the version they were placed against.',
  'catalog.publish.reason': 'Why are you publishing this version?',
  'catalog.publish.blocked':
    'This version cannot be published yet. Run the checks and correct what they report.',
  'catalog.publish.done': 'Published. Every counter offers this now.',
  'catalog.publish.superseded': 'It supersedes the version that was published before it.',
  'catalog.retire': 'Retire this version',
  'catalog.retire.title': 'Retire this version?',
  'catalog.retire.body':
    'A retired version can no longer be published and is kept only because orders were placed against it. If it is the published one, no counter can take an order until another is published.',
  'catalog.retire.reason': 'Why are you retiring this version?',
  'catalog.retire.done': 'Retired.',
  'catalog.needsPublish':
    'You can draft and change a catalogue. Publishing and retiring are the reviewing administrator’s acts and need the catalogue publishing permission — ask the shop owner if you need it.',
  'catalog.needsPublish.correction':
    'Correcting a label on a published version needs the catalogue publishing permission, for the same reason publishing does: an order already placed is filed against this version.',

  /* What the counter sees, which is how publication is shown to have worked. ------------------- */
  'catalog.current': 'What the counter can order now',
  'catalog.current.loading': 'what your branch can order',
  'catalog.current.empty':
    'Your branch has nothing it can order. Either nothing is published, or nothing published is offered here.',
  'catalog.current.caption': 'What this branch can order today',
  'catalog.current.column.service': 'Service',
  'catalog.current.column.category': 'Category',
  'catalog.current.column.reference': 'Reference',
  'catalog.current.column.duration': 'Usually takes',
  'catalog.current.days': '{count, plural, one {1 day} other {# days}}',
} as const

/*
 * Every key repeated rather than mapped from the English catalogue, and that is deliberate. Deriving
 * one from the other would keep them in step for free — and would also make the whole family count as
 * translated to any tool that reads these files, because the `not translated` markers the 95%
 * enablement gate is written against would not exist. The duplication is the record.
 */
export const catalogTa: Record<keyof typeof catalogEn, string> = {
  /* The versions. --------------------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'catalog.title': 'Catalogue',
  // not translated — awaiting native-speaker review
  'catalog.body':
    'What this shop offers, and where. A version is drafted, checked and published as a whole, so a counter never sees half a change.',
  // not translated — awaiting native-speaker review
  'catalog.versions.caption': 'Every version of the catalogue, newest first',
  // not translated — awaiting native-speaker review
  'catalog.versions.loading': 'the catalogue versions',
  // not translated — awaiting native-speaker review
  'catalog.versions.empty':
    'No catalogue has been drafted yet. Until one is published, no counter can take an order.',
  // not translated — awaiting native-speaker review
  'catalog.versions.column.version': 'Version',
  // not translated — awaiting native-speaker review
  'catalog.versions.column.name': 'What changed',
  // not translated — awaiting native-speaker review
  'catalog.versions.column.status': 'State',
  // not translated — awaiting native-speaker review
  'catalog.versions.column.categories': 'Categories',
  // not translated — awaiting native-speaker review
  'catalog.versions.column.published': 'Published',
  // not translated — awaiting native-speaker review
  'catalog.version': 'Version {number}',
  // not translated — awaiting native-speaker review
  'catalog.status.Draft': 'Draft',
  // not translated — awaiting native-speaker review
  'catalog.status.Published': 'Published',
  // not translated — awaiting native-speaker review
  'catalog.status.Retired': 'Retired',
  // not translated — awaiting native-speaker review
  'catalog.status.unknown': 'Unknown',
  // not translated — awaiting native-speaker review
  'catalog.versions.open': 'Open version {number}',
  // not translated — awaiting native-speaker review
  'catalog.draft.start': 'Start a draft',
  // not translated — awaiting native-speaker review
  'catalog.draft.clone': 'Start a draft from this version',
  // not translated — awaiting native-speaker review
  'catalog.draft.title': 'Start a draft?',
  // not translated — awaiting native-speaker review
  'catalog.draft.body':
    'A draft is a working copy. Nothing a counter sees changes until it is published.',
  // not translated — awaiting native-speaker review
  'catalog.draft.name': 'What is this change for?',
  // not translated — awaiting native-speaker review
  'catalog.draft.name.hint': 'A sentence somebody will read in a year when they ask what changed.',
  // not translated — awaiting native-speaker review
  'catalog.draft.started': 'Draft started.',
  // not translated — awaiting native-speaker review
  'catalog.notPublished':
    'Nothing is published, so no counter can take an order yet. Draft a catalogue, check it and publish it.',

  /* One version, and its tree. ----------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'catalog.editor.loading': 'this catalogue version',
  // not translated — awaiting native-speaker review
  'catalog.editor.notFound': 'No catalogue version matches that address.',
  // not translated — awaiting native-speaker review
  'catalog.editor.caption': 'The hierarchy of this catalogue version',
  // not translated — awaiting native-speaker review
  'catalog.editor.empty':
    'This version has nothing in it yet. Add a category, then the things a counter can order under it.',
  // not translated — awaiting native-speaker review
  'catalog.editor.readOnly':
    'This version is published and its structure cannot be changed. Start a draft from it to change what the shop offers; only a label can be corrected here, and every correction is recorded.',
  // not translated — awaiting native-speaker review
  'catalog.editor.retired':
    'This version has been retired. It is kept because orders were placed against it, and nothing about it can be changed.',
  // not translated — awaiting native-speaker review
  'catalog.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'catalog.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'catalog.column.kind': 'Kind',
  // not translated — awaiting native-speaker review
  'catalog.column.branches': 'Offered at',
  // not translated — awaiting native-speaker review
  'catalog.column.orderable': 'Orderable',
  // not translated — awaiting native-speaker review
  'catalog.column.findings': 'Checks',
  // not translated — awaiting native-speaker review
  'catalog.kind.category': 'Category',
  // not translated — awaiting native-speaker review
  'catalog.kind.grouping': 'Grouping only',
  // not translated — awaiting native-speaker review
  'catalog.kind.serviceType': 'Orderable',
  // not translated — awaiting native-speaker review
  'catalog.grouping.hint':
    'A grouping category holds other categories and is not ordered directly.',
  // not translated — awaiting native-speaker review
  'catalog.branches.none': 'Nowhere',
  // not translated — awaiting native-speaker review
  'catalog.branches.count': '{count, plural, one {1 branch} other {# branches}}',
  // not translated — awaiting native-speaker review
  'catalog.branches.outside':
    'Offered at {count, plural, one {a branch} other {# branches}} where its parent is not. A sub-category is reached through its parent, so publication will refuse this.',
  // not translated — awaiting native-speaker review
  'catalog.orderable.yes': 'Yes',
  // not translated — awaiting native-speaker review
  'catalog.orderable.no': 'Not yet',
  // not translated — awaiting native-speaker review
  'catalog.orderable.missing':
    'Not orderable: {count, plural, one {one link} other {# links}} still missing.',
  // not translated — awaiting native-speaker review
  'catalog.link.measurementTemplateId': 'what is measured',
  // not translated — awaiting native-speaker review
  'catalog.link.workflowDefinitionId': 'how it is made',
  // not translated — awaiting native-speaker review
  'catalog.link.designOptionGroupIds': 'what the customer chooses from',
  // not translated — awaiting native-speaker review
  'catalog.link.priceListItemCode': 'what it costs',
  // not translated — awaiting native-speaker review
  'catalog.link.qcChecklistTemplateId': 'how it is checked',
  // not translated — awaiting native-speaker review
  'catalog.allowIncomplete':
    'Publish this even with links missing. It will be visibly not orderable until they are set, rather than holding up the whole catalogue.',

  /* Adding and editing. ------------------------------------------------------------------------ */
  // not translated — awaiting native-speaker review
  'catalog.category.add': 'Add a category',
  // not translated — awaiting native-speaker review
  'catalog.category.addUnder': 'Add a category under {name}',
  // not translated — awaiting native-speaker review
  'catalog.category.edit': 'Edit {name}',
  // not translated — awaiting native-speaker review
  'catalog.category.remove': 'Remove {name}',
  // not translated — awaiting native-speaker review
  'catalog.category.addTitle': 'A new category',
  // not translated — awaiting native-speaker review
  'catalog.category.editTitle': 'Editing {name}',
  // not translated — awaiting native-speaker review
  'catalog.serviceType.add': 'Add something orderable under {name}',
  // not translated — awaiting native-speaker review
  'catalog.serviceType.edit': 'Edit {name}',
  // not translated — awaiting native-speaker review
  'catalog.serviceType.remove': 'Remove {name}',
  // not translated — awaiting native-speaker review
  'catalog.serviceType.addTitle': 'A new orderable service',
  // not translated — awaiting native-speaker review
  'catalog.serviceType.editTitle': 'Editing {name}',
  // not translated — awaiting native-speaker review
  'catalog.form.code': 'Code',
  // not translated — awaiting native-speaker review
  'catalog.form.code.hint':
    'Set once and never again: it is what every order, invoice and report files this under.',
  // not translated — awaiting native-speaker review
  'catalog.form.code.fixedHint':
    'A code cannot be changed. Orders already placed are filed under it.',
  // not translated — awaiting native-speaker review
  'catalog.form.name': 'Name',
  // not translated — awaiting native-speaker review
  'catalog.form.nameTamil': 'Name in Tamil',
  // not translated — awaiting native-speaker review
  'catalog.form.description': 'Description',
  // not translated — awaiting native-speaker review
  'catalog.form.displayOrder': 'Where it appears',
  // not translated — awaiting native-speaker review
  'catalog.form.parent': 'Inside',
  // not translated — awaiting native-speaker review
  'catalog.form.parent.none': 'At the top level',
  // not translated — awaiting native-speaker review
  'catalog.form.branches': 'Offered at',
  // not translated — awaiting native-speaker review
  'catalog.form.branches.hint':
    'Leave every branch unticked and it is offered nowhere. A sub-category can only be offered where its parent is.',
  // not translated — awaiting native-speaker review
  'catalog.form.featureFlag': 'Only when this feature setting is on',
  // not translated — awaiting native-speaker review
  'catalog.form.duration': 'How many days it usually takes',
  // not translated — awaiting native-speaker review
  'catalog.form.intakeWarning': 'What to tell the customer at the counter',
  // not translated — awaiting native-speaker review
  'catalog.form.reason': 'Why are you making this change?',
  // not translated — awaiting native-speaker review
  'catalog.form.save': 'Save',
  // not translated — awaiting native-speaker review
  'catalog.form.links': 'What ordering this means',
  // not translated — awaiting native-speaker review
  'catalog.form.links.hint':
    'Five links decide what happens when a counter orders this. There is no picker for them yet — the catalogues they would choose from arrive with their own screens — so each is entered as the identifier it is.',
  // not translated — awaiting native-speaker review
  'catalog.correct': 'Correct the labels of {name}',
  // not translated — awaiting native-speaker review
  'catalog.correct.title': 'Correct the labels of {name}',
  // not translated — awaiting native-speaker review
  'catalog.correct.body':
    'A label, a Tamil label, a description and a position. Nothing here changes what ordering this means: the price, the work and every report all follow the code, which does not change.',
  // not translated — awaiting native-speaker review
  'catalog.correct.reason.hint':
    'Required. Orders were placed against this version, so every correction to it is recorded with who made it and why.',
  // not translated — awaiting native-speaker review
  'catalog.correct.save': 'Correct the labels',
  // not translated — awaiting native-speaker review
  'catalog.corrected': 'Corrected the labels of {name}.',
  // not translated — awaiting native-speaker review
  'catalog.saved': 'Saved {name}.',
  // not translated — awaiting native-speaker review
  'catalog.removed': 'Removed {name}.',
  // not translated — awaiting native-speaker review
  'catalog.remove.title': 'Remove this from the draft?',
  // not translated — awaiting native-speaker review
  'catalog.remove.body':
    'It is removed from this draft only. Published versions keep it, and every order already placed against it is untouched.',
  // not translated — awaiting native-speaker review
  'catalog.remove.reason': 'Why are you removing this?',

  /* The label correction a published version admits. ------------------------------------------- */
  // not translated — awaiting native-speaker review
  'catalog.presentation': 'Correct the label',
  // not translated — awaiting native-speaker review
  'catalog.presentation.title': 'Correct this label?',
  // not translated — awaiting native-speaker review
  'catalog.presentation.body':
    'Only what a person reads changes — the name, the Tamil name, the description and where it appears. Nothing about what ordering it means, because an order already placed against it was placed against what it meant then.',
  // not translated — awaiting native-speaker review
  'catalog.presentation.reason': 'Why are you correcting this?',
  // not translated — awaiting native-speaker review
  'catalog.presentation.done': 'Corrected {name}.',

  /* Checking and publishing. ------------------------------------------------------------------- */
  // not translated — awaiting native-speaker review
  'catalog.check': 'Check this version',
  // not translated — awaiting native-speaker review
  'catalog.check.loading': 'the checks that run before publication',
  // not translated — awaiting native-speaker review
  'catalog.check.ready': 'Every check passed. This version can be published.',
  // not translated — awaiting native-speaker review
  'catalog.check.counts':
    '{errors, plural, =0 {No problem} one {1 problem} other {# problems}} and {warnings, plural, =0 {nothing} one {1 thing} other {# things}} worth knowing.',
  // not translated — awaiting native-speaker review
  'catalog.check.blocked':
    'Publication is refused until the problems below are corrected. Each one is shown beside the thing it is about.',
  // not translated — awaiting native-speaker review
  'catalog.check.warningsOnly':
    'Nothing here refuses publication. Each is shown beside the thing it is about, so it can be looked at before the catalogue goes out.',
  // not translated — awaiting native-speaker review
  'catalog.check.unanchored':
    'This was reported about something not on this screen. The version may have changed since the check ran — run it again to see where it stands.',
  // not translated — awaiting native-speaker review
  'catalog.check.stale':
    'The check ran against an earlier state of this version. Run it again before publishing.',
  // not translated — awaiting native-speaker review
  'catalog.check.finding.error': 'Problem',
  // not translated — awaiting native-speaker review
  'catalog.check.finding.warning': 'Worth knowing',
  // not translated — awaiting native-speaker review
  'catalog.check.validator': 'reported by {validator}',

  // not translated — awaiting native-speaker review
  'catalog.publish': 'Publish this version',
  // not translated — awaiting native-speaker review
  'catalog.publish.title': 'Publish this catalogue?',
  // not translated — awaiting native-speaker review
  'catalog.publish.body':
    'Every counter offers this from the moment you publish, and whatever is published now is superseded. Orders already placed keep the version they were placed against.',
  // not translated — awaiting native-speaker review
  'catalog.publish.reason': 'Why are you publishing this version?',
  // not translated — awaiting native-speaker review
  'catalog.publish.blocked':
    'This version cannot be published yet. Run the checks and correct what they report.',
  // not translated — awaiting native-speaker review
  'catalog.publish.done': 'Published. Every counter offers this now.',
  // not translated — awaiting native-speaker review
  'catalog.publish.superseded': 'It supersedes the version that was published before it.',
  // not translated — awaiting native-speaker review
  'catalog.retire': 'Retire this version',
  // not translated — awaiting native-speaker review
  'catalog.retire.title': 'Retire this version?',
  // not translated — awaiting native-speaker review
  'catalog.retire.body':
    'A retired version can no longer be published and is kept only because orders were placed against it. If it is the published one, no counter can take an order until another is published.',
  // not translated — awaiting native-speaker review
  'catalog.retire.reason': 'Why are you retiring this version?',
  // not translated — awaiting native-speaker review
  'catalog.retire.done': 'Retired.',
  // not translated — awaiting native-speaker review
  'catalog.needsPublish':
    'You can draft and change a catalogue. Publishing and retiring are the reviewing administrator’s acts and need the catalogue publishing permission — ask the shop owner if you need it.',
  // not translated — awaiting native-speaker review
  'catalog.needsPublish.correction':
    'Correcting a label on a published version needs the catalogue publishing permission, for the same reason publishing does: an order already placed is filed against this version.',

  /* What the counter sees, which is how publication is shown to have worked. ------------------- */
  // not translated — awaiting native-speaker review
  'catalog.current': 'What the counter can order now',
  // not translated — awaiting native-speaker review
  'catalog.current.loading': 'what your branch can order',
  // not translated — awaiting native-speaker review
  'catalog.current.empty':
    'Your branch has nothing it can order. Either nothing is published, or nothing published is offered here.',
  // not translated — awaiting native-speaker review
  'catalog.current.caption': 'What this branch can order today',
  // not translated — awaiting native-speaker review
  'catalog.current.column.service': 'Service',
  // not translated — awaiting native-speaker review
  'catalog.current.column.category': 'Category',
  // not translated — awaiting native-speaker review
  'catalog.current.column.reference': 'Reference',
  // not translated — awaiting native-speaker review
  'catalog.current.column.duration': 'Usually takes',
  // not translated — awaiting native-speaker review
  'catalog.current.days': '{count, plural, one {1 day} other {# days}}',
}
