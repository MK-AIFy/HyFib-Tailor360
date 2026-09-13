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

  /* Design groups, options and rules on one category (#141). ---------------------------------- */
  'catalog.design.open': 'Design options for {name}',
  'catalog.design.title': 'Design options for {name}',
  'catalog.design.loading': "this category's design options",
  'catalog.design.categoryNotFound': 'No category matches that address.',
  'catalog.design.caption': 'The groups and options {name} offers',
  'catalog.design.empty':
    'This category has no design groups yet. Add one, then the options a customer chooses from.',
  'catalog.design.column.detail': 'Detail',
  'catalog.design.column.illustration': 'Illustration',
  'catalog.design.group.add': 'Add a group',
  'catalog.design.group.addTitle': 'A new design group',
  'catalog.design.group.editTitle': 'Editing {name}',
  'catalog.design.group.edit': 'Edit {name}',
  'catalog.design.group.remove': 'Remove {name}',
  'catalog.design.group.code.hint':
    'Set once and never again: rules, snapshots and job cards refer to it once the version is published.',
  'catalog.design.group.selectionMode': 'How many the customer may choose',
  'catalog.design.group.selectionMode.SingleChoice': 'One',
  'catalog.design.group.selectionMode.MultipleChoice': 'One or more',
  'catalog.design.group.required': 'Required',
  'catalog.design.group.required.hint': 'The customer must choose something from this group.',
  'catalog.design.group.branches.hint':
    'Leave every branch unticked and this group is offered nowhere, even where the category is offered.',
  'catalog.design.option.add': 'Add an option to {name}',
  'catalog.design.option.addTitle': 'A new option',
  'catalog.design.option.editTitle': 'Editing {name}',
  'catalog.design.option.edit': 'Edit {name}',
  'catalog.design.option.remove': 'Remove {name}',
  'catalog.design.option.code.hint':
    "Set once and never again. NONE is reserved for 'the customer chose not to have this' and is always selectable.",
  'catalog.design.option.helpText': 'Help text',
  'catalog.design.option.illustrationKey': 'Illustration key',
  'catalog.design.option.illustrationKey.hint':
    'There is no picker for this yet — the illustration catalogue arrives with its own screen — so it is entered as the key it is. Leave it blank and the picker falls back to the label and alternative text.',
  'catalog.design.option.illustrationAlt': 'Alternative text',
  'catalog.design.option.illustrationAlt.hint':
    'What a customer is told when there is no drawing to show, or when one cannot be seen.',
  'catalog.design.option.illustration.missing':
    'No illustration yet. The picker shows the label and this alternative text instead.',
  'catalog.design.option.illustration.key': 'Illustration: {key}',
  'catalog.design.option.timeImpactDays': 'Extra days this option usually adds',
  'catalog.design.option.active': 'Offered',
  'catalog.design.option.active.hint':
    'Untick to retire the option: it stays known to the catalogue but is no longer offered.',
  'catalog.design.option.retired': 'Retired',
  'catalog.design.rules.title': 'Rules',
  'catalog.design.rules.empty': 'This category has no rules yet.',
  'catalog.design.rules.caption': 'The rules between {name}’s design options',
  'catalog.design.rule.add': 'Add a rule',
  'catalog.design.rule.formTitle': 'A design rule',
  'catalog.design.rule.edit': 'Edit {identifier}',
  'catalog.design.rule.remove': 'Remove {identifier}',
  'catalog.design.rule.identifier': 'Rule',
  'catalog.design.rule.type': 'What the rule does',
  'catalog.design.rule.type.Requires': 'Requires another choice',
  'catalog.design.rule.type.Excludes': 'Excludes another choice',
  'catalog.design.rule.type.RequiresAttachment': 'Requires a reference photo',
  'catalog.design.rule.type.Note': 'Shows a note at the counter',
  'catalog.design.rule.statement': 'The rule',
  'catalog.design.rule.antecedent': 'When',
  'catalog.design.rule.consequent': 'Then',
  'catalog.design.rule.operand.form': 'Condition',
  'catalog.design.rule.operand.form.Always': 'Always',
  'catalog.design.rule.operand.form.AnySelection': 'Anything is chosen',
  'catalog.design.rule.operand.form.Equals': 'Is exactly',
  'catalog.design.rule.operand.form.NotEquals': 'Is not',
  'catalog.design.rule.operand.form.In': 'Is one of',
  'catalog.design.rule.operand.form.Includes': 'Includes',
  'catalog.design.rule.operand.form.Excludes': 'Excludes',
  'catalog.design.rule.operand.group': 'Group',
  'catalog.design.rule.operand.group.none': 'Choose a group',
  'catalog.design.rule.operand.group.pickFirst': 'Choose a group to see its options.',
  'catalog.design.rule.operand.options': 'Options',
  'catalog.design.rule.note': 'Note shown at the counter',
  'catalog.design.rule.why': 'Why this rule exists',
  'catalog.design.rule.why.hint': 'For the next person reading this rule, not for the customer.',
  'catalog.design.rule.sentence': 'This rule reads:',
  'catalog.design.rule.saved': 'Saved the rule.',
  'catalog.design.rule.removed': 'Removed {identifier}.',
  'catalog.design.remove.action': 'Remove {name}',
  'catalog.design.order.up': 'Move {label} up',
  'catalog.design.order.down': 'Move {label} down',
  'catalog.design.order.atStart': 'Already first.',
  'catalog.design.order.atEnd': 'Already last.',

  /* The job-card design component (#142). ---------------------------------------------------- */
  'catalog.design.card.meta': '{category} · catalogue version {version}',
  'catalog.design.card.notes': 'Standing instructions',
  'catalog.design.card.instructions': 'Instructions:',
  'catalog.design.card.print': 'Print',

  /* The design picker (#142). ----------------------------------------------------------------- */
  'catalog.design.picker.title': 'Choose the design',
  'catalog.design.picker.loading': 'the design options',
  'catalog.design.picker.offline.start': 'Starting a design',
  'catalog.design.picker.offline.save': 'Saving a design choice',
  'catalog.design.picker.offline.disabled': 'Needs a connection to change this.',
  'catalog.design.picker.consumed.title': 'This design is already finished.',
  'catalog.design.picker.consumed.body':
    'It was confirmed onto an order and can no longer be changed here.',
  'catalog.design.picker.empty.title': 'Nothing to choose from yet.',
  'catalog.design.picker.empty.body':
    'This service has no design options configured. Ask an administrator to add some before a customer can choose one.',
  'catalog.design.picker.conflict.title': 'Someone else changed this design.',
  'catalog.design.picker.conflict.body':
    'Another device on this branch saved a change first. Read it again to see what changed.',
  'catalog.design.picker.conflict.reload': 'Read it again',
  'catalog.design.picker.group.count':
    '{count, plural, =0 {Nothing chosen} one {1 chosen} other {# chosen}}',
  'catalog.design.picker.zoom': 'View {option} full-screen',
  'catalog.design.picker.reason.requires': 'Needed because {reason}.',
  'catalog.design.picker.reason.excluded': 'Not available because {reason}.',
  'catalog.design.picker.attachment.reason': 'A reference photo is needed because {reason}.',
  'catalog.design.picker.attachment.confirm': 'A reference photo is attached to this garment.',
  'catalog.design.picker.instructions': 'Anything else the customer asked for',
  'catalog.design.picker.instructions.hint':
    'Free text. Never priced, and never any option choice.',
  'catalog.design.picker.operand.always': 'always',
  'catalog.design.picker.operand.anySelection': 'a choice is made in {group}',
  'catalog.design.picker.operand.equals': '{group} is {options}',
  'catalog.design.picker.operand.notEquals': '{group} is not {options}',
  'catalog.design.picker.operand.in': '{group} is one of {options}',
  'catalog.design.picker.operand.includes': '{group} includes {options}',
  'catalog.design.picker.operand.excludes': '{group} does not include {options}',

  /* The migration prompt (#142). --------------------------------------------------------------- */
  'catalog.design.migration.title': 'The catalogue changed since this was started',
  'catalog.design.migration.description':
    'What moved, and the choice between updating to the current version or finishing on this one.',
  'catalog.design.migration.keep': 'Finish on this version',
  'catalog.design.migration.migrate': 'Update to the current version',
  'catalog.design.migration.finished.title': 'Finishing on the version already chosen.',
  'catalog.design.migration.finished.body':
    'The choices already made are kept as they are. Updating to the current version is still available whenever it is wanted.',

  /* The selection summary (#142). -------------------------------------------------------------- */
  'catalog.design.summary.title': 'Summary',
  'catalog.design.summary.checking': 'Checking with the catalogue…',
  'catalog.design.summary.unchecked': 'Not checked yet.',
  'catalog.design.summary.clean': 'Nothing is standing in the way of confirming this.',
  'catalog.design.summary.blocked': 'This cannot be confirmed yet — see below.',
  'catalog.design.summary.notChosen': 'Not chosen',
  'catalog.design.summary.requiredUnset': 'Needs a choice',
  'catalog.design.summary.autoSelected': 'Added automatically because {reason}.',
  'catalog.design.summary.priceItems': 'Price list items: {items}',
  'catalog.design.summary.priceItems.none': 'none',
  'catalog.design.summary.days':
    '{count, plural, =0 {No extra days} one {1 extra day} other {# extra days}}',
  'catalog.design.summary.notes': 'Standing instructions',
  'catalog.design.summary.violations': 'What is blocking confirmation',
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

  /* Design groups, options and rules on one category (#141). ---------------------------------- */
  // not translated — awaiting native-speaker review
  'catalog.design.open': 'Design options for {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.title': 'Design options for {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.loading': "this category's design options",
  // not translated — awaiting native-speaker review
  'catalog.design.categoryNotFound': 'No category matches that address.',
  // not translated — awaiting native-speaker review
  'catalog.design.caption': 'The groups and options {name} offers',
  // not translated — awaiting native-speaker review
  'catalog.design.empty':
    'This category has no design groups yet. Add one, then the options a customer chooses from.',
  // not translated — awaiting native-speaker review
  'catalog.design.column.detail': 'Detail',
  // not translated — awaiting native-speaker review
  'catalog.design.column.illustration': 'Illustration',
  // not translated — awaiting native-speaker review
  'catalog.design.group.add': 'Add a group',
  // not translated — awaiting native-speaker review
  'catalog.design.group.addTitle': 'A new design group',
  // not translated — awaiting native-speaker review
  'catalog.design.group.editTitle': 'Editing {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.group.edit': 'Edit {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.group.remove': 'Remove {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.group.code.hint':
    'Set once and never again: rules, snapshots and job cards refer to it once the version is published.',
  // not translated — awaiting native-speaker review
  'catalog.design.group.selectionMode': 'How many the customer may choose',
  // not translated — awaiting native-speaker review
  'catalog.design.group.selectionMode.SingleChoice': 'One',
  // not translated — awaiting native-speaker review
  'catalog.design.group.selectionMode.MultipleChoice': 'One or more',
  // not translated — awaiting native-speaker review
  'catalog.design.group.required': 'Required',
  // not translated — awaiting native-speaker review
  'catalog.design.group.required.hint': 'The customer must choose something from this group.',
  // not translated — awaiting native-speaker review
  'catalog.design.group.branches.hint':
    'Leave every branch unticked and this group is offered nowhere, even where the category is offered.',
  // not translated — awaiting native-speaker review
  'catalog.design.option.add': 'Add an option to {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.option.addTitle': 'A new option',
  // not translated — awaiting native-speaker review
  'catalog.design.option.editTitle': 'Editing {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.option.edit': 'Edit {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.option.remove': 'Remove {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.option.code.hint':
    "Set once and never again. NONE is reserved for 'the customer chose not to have this' and is always selectable.",
  // not translated — awaiting native-speaker review
  'catalog.design.option.helpText': 'Help text',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustrationKey': 'Illustration key',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustrationKey.hint':
    'There is no picker for this yet — the illustration catalogue arrives with its own screen — so it is entered as the key it is. Leave it blank and the picker falls back to the label and alternative text.',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustrationAlt': 'Alternative text',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustrationAlt.hint':
    'What a customer is told when there is no drawing to show, or when one cannot be seen.',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustration.missing':
    'No illustration yet. The picker shows the label and this alternative text instead.',
  // not translated — awaiting native-speaker review
  'catalog.design.option.illustration.key': 'Illustration: {key}',
  // not translated — awaiting native-speaker review
  'catalog.design.option.timeImpactDays': 'Extra days this option usually adds',
  // not translated — awaiting native-speaker review
  'catalog.design.option.active': 'Offered',
  // not translated — awaiting native-speaker review
  'catalog.design.option.active.hint':
    'Untick to retire the option: it stays known to the catalogue but is no longer offered.',
  // not translated — awaiting native-speaker review
  'catalog.design.option.retired': 'Retired',
  // not translated — awaiting native-speaker review
  'catalog.design.rules.title': 'Rules',
  // not translated — awaiting native-speaker review
  'catalog.design.rules.empty': 'This category has no rules yet.',
  // not translated — awaiting native-speaker review
  'catalog.design.rules.caption': 'The rules between {name}’s design options',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.add': 'Add a rule',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.formTitle': 'A design rule',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.edit': 'Edit {identifier}',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.remove': 'Remove {identifier}',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.identifier': 'Rule',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.type': 'What the rule does',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.type.Requires': 'Requires another choice',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.type.Excludes': 'Excludes another choice',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.type.RequiresAttachment': 'Requires a reference photo',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.type.Note': 'Shows a note at the counter',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.statement': 'The rule',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.antecedent': 'When',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.consequent': 'Then',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form': 'Condition',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.Always': 'Always',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.AnySelection': 'Anything is chosen',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.Equals': 'Is exactly',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.NotEquals': 'Is not',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.In': 'Is one of',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.Includes': 'Includes',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.form.Excludes': 'Excludes',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.group': 'Group',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.group.none': 'Choose a group',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.group.pickFirst': 'Choose a group to see its options.',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.operand.options': 'Options',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.note': 'Note shown at the counter',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.why': 'Why this rule exists',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.why.hint': 'For the next person reading this rule, not for the customer.',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.sentence': 'This rule reads:',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.saved': 'Saved the rule.',
  // not translated — awaiting native-speaker review
  'catalog.design.rule.removed': 'Removed {identifier}.',
  // not translated — awaiting native-speaker review
  'catalog.design.remove.action': 'Remove {name}',
  // not translated — awaiting native-speaker review
  'catalog.design.order.up': 'Move {label} up',
  // not translated — awaiting native-speaker review
  'catalog.design.order.down': 'Move {label} down',
  // not translated — awaiting native-speaker review
  'catalog.design.order.atStart': 'Already first.',
  // not translated — awaiting native-speaker review
  'catalog.design.order.atEnd': 'Already last.',

  // not translated — awaiting native-speaker review
  'catalog.design.card.meta': '{category} · catalogue version {version}',
  // not translated — awaiting native-speaker review
  'catalog.design.card.notes': 'Standing instructions',
  // not translated — awaiting native-speaker review
  'catalog.design.card.instructions': 'Instructions:',
  // not translated — awaiting native-speaker review
  'catalog.design.card.print': 'Print',

  // not translated — awaiting native-speaker review
  'catalog.design.picker.title': 'Choose the design',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.loading': 'the design options',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.offline.start': 'Starting a design',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.offline.save': 'Saving a design choice',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.offline.disabled': 'Needs a connection to change this.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.consumed.title': 'This design is already finished.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.consumed.body':
    'It was confirmed onto an order and can no longer be changed here.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.empty.title': 'Nothing to choose from yet.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.empty.body':
    'This service has no design options configured. Ask an administrator to add some before a customer can choose one.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.conflict.title': 'Someone else changed this design.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.conflict.body':
    'Another device on this branch saved a change first. Read it again to see what changed.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.conflict.reload': 'Read it again',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.group.count':
    '{count, plural, =0 {Nothing chosen} one {1 chosen} other {# chosen}}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.zoom': 'View {option} full-screen',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.reason.requires': 'Needed because {reason}.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.reason.excluded': 'Not available because {reason}.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.attachment.reason': 'A reference photo is needed because {reason}.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.attachment.confirm': 'A reference photo is attached to this garment.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.instructions': 'Anything else the customer asked for',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.instructions.hint':
    'Free text. Never priced, and never any option choice.',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.always': 'always',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.anySelection': 'a choice is made in {group}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.equals': '{group} is {options}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.notEquals': '{group} is not {options}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.in': '{group} is one of {options}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.includes': '{group} includes {options}',
  // not translated — awaiting native-speaker review
  'catalog.design.picker.operand.excludes': '{group} does not include {options}',

  // not translated — awaiting native-speaker review
  'catalog.design.migration.title': 'The catalogue changed since this was started',
  // not translated — awaiting native-speaker review
  'catalog.design.migration.description':
    'What moved, and the choice between updating to the current version or finishing on this one.',
  // not translated — awaiting native-speaker review
  'catalog.design.migration.keep': 'Finish on this version',
  // not translated — awaiting native-speaker review
  'catalog.design.migration.migrate': 'Update to the current version',
  // not translated — awaiting native-speaker review
  'catalog.design.migration.finished.title': 'Finishing on the version already chosen.',
  // not translated — awaiting native-speaker review
  'catalog.design.migration.finished.body':
    'The choices already made are kept as they are. Updating to the current version is still available whenever it is wanted.',

  // not translated — awaiting native-speaker review
  'catalog.design.summary.title': 'Summary',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.checking': 'Checking with the catalogue…',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.unchecked': 'Not checked yet.',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.clean': 'Nothing is standing in the way of confirming this.',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.blocked': 'This cannot be confirmed yet — see below.',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.notChosen': 'Not chosen',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.requiredUnset': 'Needs a choice',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.autoSelected': 'Added automatically because {reason}.',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.priceItems': 'Price list items: {items}',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.priceItems.none': 'none',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.days':
    '{count, plural, =0 {No extra days} one {1 extra day} other {# extra days}}',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.notes': 'Standing instructions',
  // not translated — awaiting native-speaker review
  'catalog.design.summary.violations': 'What is blocking confirmation',
}
