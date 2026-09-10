/**
 * Administration messages — staff accounts, branches, roles, feature settings, the trail, the outbox.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. Three of them bind
 * particularly hard here:
 *
 *  - **Say what will happen to the person, not to the row.** "They will be signed out of every
 *    device" is what an administrator needs to weigh; "the status will change to Suspended" is what
 *    the database is about to do, and nobody has ever hesitated over it.
 *  - **A reason prompt asks a question.** "Why are you suspending this account?" collects a sentence
 *    somebody will read in a year's time. A box labelled "Reason" collects the word "reason".
 *  - **A refusal explains the rule, not the code.** The server answers
 *    `identity.permission-not-held-by-granter`; the person reads that they cannot give away something
 *    they do not have themselves, and who to ask.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * The same reason recorded for the authentication family. These sentences decide who can do what and
 * are read at the moment somebody is taking access away from a colleague; a mistranslation here is a
 * security incident rather than an awkward phrase, and they are held back for native-speaker review
 * with the rest of the security vocabulary.
 */
export const adminEn = {
  /* The section and its destinations. ------------------------------------------------------- */
  'admin.title': 'Administration',
  'admin.body':
    'Staff accounts, branches, roles and the settings that govern how this shop works. Every change here is recorded with your name and the reason you give.',
  'admin.nav.label': 'Administration sections',
  'admin.nav.users': 'Staff accounts',
  'admin.nav.branches': 'Branches',
  'admin.nav.roles': 'Roles and permissions',
  'admin.nav.features': 'Feature settings',
  'admin.nav.audit': 'Audit trail',
  'admin.nav.outbox': 'Failed messages',
  'admin.forbidden.title': 'You do not have access to this',
  'admin.forbidden.body':
    'This screen needs a permission your account does not hold. If you need it, ask the shop owner.',

  /* Things every command on this surface shares. --------------------------------------------- */
  'admin.reason.label': 'Reason',
  'admin.reason.hint': 'Recorded in the audit trail with your name. A sentence is enough.',
  'admin.reason.required': 'Give a reason before continuing.',
  'admin.reason.tooLong': 'Shorten this to 500 characters or fewer.',
  'admin.conflict.title': 'Somebody else changed this first',
  'admin.conflict.body':
    'This was edited while you had it open, so your change was not applied. Reload it, check the change is still the one you want, and apply it again.',
  'admin.stepUp.title': 'Confirm it is you',
  'admin.stepUp.body':
    'This change needs a fresh check that it is you. Answer your second factor and it will continue.',
  'admin.saved': 'Saved.',
  'admin.reload': 'Reload',
  'admin.cancel': 'Cancel',
  'admin.back': 'Back',

  /* Staff accounts. -------------------------------------------------------------------------- */
  'admin.users.title': 'Staff accounts',
  'admin.users.caption': 'Staff accounts, with their status and roles',
  'admin.users.loading': 'staff accounts',
  'admin.users.empty': 'No account matches what you are looking for.',
  'admin.users.search.label': 'Search by name',
  'admin.users.search.hint':
    'Matches names only. Searching by phone number or email address is deliberately not offered.',
  'admin.users.filter.status': 'Status',
  'admin.users.filter.anyStatus': 'Any status',
  'admin.users.column.name': 'Name',
  'admin.users.column.signIn': 'Sign-in name',
  'admin.users.column.status': 'Status',
  'admin.users.column.roles': 'Roles',
  'admin.users.column.lastSignIn': 'Last signed in',
  'admin.users.neverSignedIn': 'Never',
  'admin.users.noRoles': 'No role',
  'admin.users.more': 'Show more accounts',
  'admin.users.open': 'Open {name}',
  'admin.users.invite': 'Invite somebody',

  'admin.user.loading': 'this account',
  'admin.user.notFound': 'No account matches that address.',
  'admin.user.signInName': 'Sign-in name',
  'admin.user.email': 'Email address',
  'admin.user.status': 'Status',
  'admin.user.secondFactor': 'Second factor',
  'admin.user.lastSignIn': 'Last signed in',
  'admin.user.created': 'Account created',
  'admin.user.self':
    'This is your own account. Administering it here is refused, so that ending your own access is never one mis-click away.',

  /* The six commands, each with the sentence that says what it does to a person. -------------- */
  'admin.command.suspend': 'Suspend',
  'admin.command.suspend.title': 'Suspend this account?',
  'admin.command.suspend.body':
    '{name} will be signed out of every device immediately and will not be able to sign in again until somebody lifts the suspension. Nothing they have done is removed.',
  'admin.command.suspend.reason': 'Why are you suspending this account?',
  'admin.command.reinstate': 'Lift suspension',
  'admin.command.reinstate.title': 'Lift the suspension?',
  'admin.command.reinstate.body':
    '{name} will be able to sign in again with the password and second factor they already had.',
  'admin.command.reinstate.reason': 'Why are you lifting the suspension?',
  'admin.command.deactivate': 'Close account',
  'admin.command.deactivate.title': 'Close this account for good?',
  'admin.command.deactivate.body':
    '{name} will be signed out everywhere, every remembered device will be forgotten, and they will not be able to sign in again. Their orders, invoices and audit entries stay exactly as they are — nothing is deleted. Reopening the account later starts their password and second factor from scratch.',
  'admin.command.deactivate.reason': 'Why are you closing this account?',
  'admin.command.reactivate': 'Reopen account',
  'admin.command.reactivate.title': 'Reopen this account?',
  'admin.command.reactivate.body':
    'The account reopens as an invitation: the password and every second factor are cleared, and {name} sets them up again from the beginning. Do this only when you have confirmed in person who they are.',
  'admin.command.reactivate.reason': 'Why are you reopening this account?',
  'admin.command.reset-mfa': 'Reset second factor',
  'admin.command.reset-mfa.title': 'Reset their second factor?',
  'admin.command.reset-mfa.body':
    "{name}'s authenticator and recovery codes are cleared, and they enrol again the next time they sign in. Until they do, their account is protected by a password alone — so confirm in person who you are talking to before you do this.",
  'admin.command.reset-mfa.reason': 'Why are you resetting the second factor?',
  'admin.command.revoke-sessions': 'Sign out everywhere',
  'admin.command.revoke-sessions.title': 'Sign this account out everywhere?',
  'admin.command.revoke-sessions.body':
    '{name} is signed out of every device at once, including the one they may be standing at. They can sign straight back in — this ends the sessions, it does not stop the account.',
  'admin.command.revoke-sessions.reason': 'Why are you ending these sessions?',
  'admin.command.done': '{name} — done.',

  /* Branches. --------------------------------------------------------------------------------- */
  'admin.branches.title': 'Branches',
  'admin.branches.caption': 'The branches this business trades from',
  'admin.branches.loading': 'branches',
  'admin.branches.empty': 'No branch has been opened yet.',
  'admin.branches.column.code': 'Code',
  'admin.branches.column.name': 'Name',
  'admin.branches.column.timeZone': 'Timezone',
  'admin.branches.column.status': 'Status',
  'admin.branches.open': 'Open a branch',
  'admin.branches.codeHint':
    'Set once and never again: this code is printed in every order, estimate and invoice number the branch produces.',
  'admin.branches.code': 'Branch code',
  'admin.branches.name': 'Branch name',
  'admin.branches.timeZone': 'Timezone',
  'admin.branches.timeZoneHint':
    'Every due date, SLA clock and report cut-off for this branch is worked out in this timezone.',
  'admin.branches.reason.open': 'Why are you opening this branch?',
  'admin.branches.status.open': 'Trading',
  'admin.branches.status.closed': 'Closed',
  'admin.branches.close': 'Close branch',
  'admin.branches.closeTitle': 'Close this branch?',
  'admin.branches.closeBody':
    'The branch stops trading. Nothing is deleted — every order, estimate and invoice it produced keeps its number, and the number keeps its code. Closing is refused while anybody is still assigned to work here.',
  'admin.branches.reason.close': 'Why are you closing this branch?',
  'admin.branches.reopen': 'Reopen branch',
  'admin.branches.reopenTitle': 'Reopen this branch?',
  'admin.branches.reopenBody': 'The branch trades again, under the code it always had.',
  'admin.branches.reason.reopen': 'Why are you reopening this branch?',

  /* Roles and the permission catalogue. -------------------------------------------------------- */
  'admin.roles.title': 'Roles and permissions',
  'admin.roles.caption': 'The roles this business uses, and what each one allows',
  'admin.roles.loading': 'roles',
  'admin.roles.empty': 'No role has been defined yet.',
  'admin.roles.column.name': 'Role',
  'admin.roles.column.reach': 'Reach',
  'admin.roles.column.grants': 'Permissions',
  'admin.roles.column.holders': 'People',
  'admin.roles.system': 'Shipped with the application',
  'admin.roles.reach.Branch': 'One branch',
  'admin.roles.reach.Organisation': 'The whole business',
  'admin.roles.holders': '{count, plural, =0 {Nobody} one {1 person} other {# people}}',
  'admin.roles.grantCount':
    '{count, plural, =0 {Nothing yet} one {1 permission} other {# permissions}}',
  'admin.role.loading': 'this role',
  'admin.role.notFound': 'No role matches that address.',
  'admin.role.permissions': 'What this role allows',
  'admin.role.permissionsHint':
    'Tick every permission this role should grant when you are finished, not just the ones you are adding. You can only grant something you hold yourself.',
  'admin.role.save': 'Save permissions',
  'admin.role.reason': 'Why are you changing what this role allows?',
  'admin.role.flag.mfa': 'Needs a second factor',
  'admin.role.flag.stepUp': 'Needs a fresh check of identity each time',
  'admin.role.flag.reason': 'Needs a written reason each time',

  /* Feature settings. -------------------------------------------------------------------------- */
  'admin.features.title': 'Feature settings',
  'admin.features.caption': 'The settings that switch parts of this application on and off',
  'admin.features.loading': 'feature settings',
  'admin.features.empty': 'No setting has been configured yet.',
  'admin.features.column.key': 'Setting',
  'admin.features.column.state': 'State',
  'admin.features.column.changed': 'Last changed',
  'admin.features.column.reason': 'Why',
  'admin.features.on': 'On',
  'admin.features.off': 'Off',
  'admin.features.turnOn': 'Turn on',
  'admin.features.turnOff': 'Turn off',
  'admin.features.confirmOn': 'Turn this on?',
  'admin.features.confirmOff': 'Turn this off?',
  'admin.features.body':
    'Every till and tablet picks this up within about {seconds, plural, one {# second} other {# seconds}}. Until they do, some devices will still be working the old way.',
  'admin.features.reason': 'Why are you changing this setting?',
  'admin.features.neverChanged': 'Never changed',

  /* The audit trail. --------------------------------------------------------------------------- */
  'admin.audit.title': 'Audit trail',
  'admin.audit.caption': 'What was changed, by whom, and why',
  'admin.audit.loading': 'the audit trail',
  'admin.audit.empty': 'Nothing matches what you are looking for.',
  'admin.audit.filter.action': 'Action starts with',
  'admin.audit.filter.apply': 'Search the trail',
  'admin.audit.column.when': 'When',
  'admin.audit.column.what': 'What happened',
  'admin.audit.column.who': 'Who',
  'admin.audit.column.why': 'Why',
  'admin.audit.noReason': 'No reason recorded',
  'admin.audit.before': 'Before',
  'admin.audit.after': 'After',
  'admin.audit.details': 'Show what changed',
  'admin.audit.more': 'Show older entries',
  'admin.audit.correlation': 'Request {correlationId}',

  /* The outbox dead letter. -------------------------------------------------------------------- */
  'admin.outbox.title': 'Failed messages',
  'admin.outbox.caption': 'Messages that could not be delivered and were given up on',
  'admin.outbox.loading': 'failed messages',
  'admin.outbox.empty': 'Nothing has failed. Everything the shop has published has been delivered.',
  'admin.outbox.column.event': 'Message',
  'admin.outbox.column.when': 'Failed',
  'admin.outbox.column.attempts': 'Attempts',
  'admin.outbox.column.error': 'What went wrong',
  'admin.outbox.replay': 'Send again',
  'admin.outbox.replayTitle': 'Put this message back on the queue?',
  'admin.outbox.replayBody':
    'It will be delivered again. If it had in fact reached its destination before it was given up on, whoever receives it gets it twice — a second message to a customer, or a second entry in an accounting system. Check that the problem behind it is fixed before sending it again.',
  'admin.outbox.reason': 'Why are you sending this again?',
  'admin.outbox.attempts': '{count, plural, one {1 attempt} other {# attempts}}',
  'admin.outbox.gone':
    'That message is no longer waiting — somebody else has already sent it again, or it went through on its own.',

  /* Roles and branches held by one account. --------------------------------------------------- */
  'admin.access.title': 'Roles and branches',
  'admin.access.loading': 'this account’s access',
  'admin.access.roles': 'Roles',
  'admin.access.rolesHint':
    'What this person may do. Tick every role they should hold when you are finished, not just the ones you are adding.',
  'admin.access.branches': 'Branches',
  'admin.access.branchesHint':
    'Where they may work. The primary branch is the one their screens open on.',
  'admin.access.primary': 'Primary',
  'admin.access.save': 'Save roles',
  'admin.access.saveBranches': 'Save branches',
  'admin.access.reason.roles': 'Why are you changing what this person may do?',
  'admin.access.reason.branches': 'Why are you changing where this person works?',
  'admin.access.lastAdministrator':
    'This would leave nobody able to administer accounts. Give somebody else an administrative role first, then take this one away.',

  /* Measurement templates (#93). ------------------------------------------------------------- */
  'admin.nav.templates': 'Measurement templates',
  'admin.templates.title': 'Measurement templates',
  'admin.templates.loading': 'measurement templates',
  'admin.templates.empty':
    'No measurement template has been set up yet. Until one is published, nothing can be measured.',
  'admin.templates.caption': 'Measurement templates, with the version each is capturing against',
  'admin.templates.column.code': 'Code',
  'admin.templates.column.name': 'Name',
  'admin.templates.column.published': 'Capturing against',
  'admin.templates.column.inProgress': 'In progress',
  'admin.templates.none': 'Nothing published',
  'admin.templates.open': 'Open {name}',
  'admin.templates.version': 'Version {number}',
  'admin.templates.inProgress':
    '{count, plural, =0 {Nothing in progress} one {1 version} other {# versions}}',
  'admin.templates.notPublishedHint':
    'A template captures nothing until one of its versions is published. Drafting, review and publication are separate acts, and the person who submits a version is not the person who approves it.',

  /* One template, and its versions. */
  'admin.template.loading': 'this template',
  'admin.template.versions': 'Versions',
  'admin.template.versionsCaption': 'Every version of this template, newest first',
  'admin.template.column.version': 'Version',
  'admin.template.column.status': 'State',
  'admin.template.column.name': 'What changed',
  'admin.template.column.fields': 'Fields',
  'admin.template.fields': '{count, plural, =0 {No field} one {1 field} other {# fields}}',
  'admin.template.readOnly':
    'This version is published and cannot be edited. Change it by starting a draft from it, which copies its fields and leaves what has already been captured alone.',
  'admin.template.clone': 'Start a draft from this version',
  'admin.template.fieldsCaption': 'The fields of this version, in the order they are measured',
  'admin.template.column.key': 'Key',
  'admin.template.column.label': 'Label',
  'admin.template.column.group': 'Step',
  'admin.template.column.unit': 'Stored as',
  'admin.template.column.range': 'Range',
  'admin.template.column.required': 'Required',
  'admin.template.required': 'Required',
  'admin.template.optional': 'Optional',
  'admin.template.millimetres': '{from}–{to} mm',
  'admin.template.noFields':
    'This version has no fields yet. A version with no fields cannot be published.',
  'admin.template.noVersions':
    'This template has no versions yet. Start a draft to describe what is measured.',
  'admin.template.approved': 'Approved, ready to publish',
  'admin.template.awaitingApproval': 'Waiting for a second administrator',
  'admin.template.needsPublish':
    'You can draft a version and submit it for review. Sending one back, approving it, publishing it and retiring it are the reviewing administrator’s acts and need the template publishing permission — ask the shop owner if you need it.',

  /* The states a version can be in. The API's own words are the last segment. */
  'admin.template.status.Draft': 'Draft',
  'admin.template.status.InReview': 'In review',
  'admin.template.status.Published': 'Published',
  'admin.template.status.Retired': 'Retired',
  'admin.template.status.unknown': 'A state this version of the application does not know',

  /* The five lifecycle acts. */
  'admin.template.action.submit': 'Submit for review',
  'admin.template.action.submit.title': 'Submit this version for review?',
  'admin.template.action.submit.body':
    'It stops being editable straight away, so that whoever reviews it reads a version that cannot change under them.',
  'admin.template.action.return': 'Send back',
  'admin.template.action.return.title': 'Send this version back to its author?',
  'admin.template.action.return.body':
    'It becomes editable again and its approval goes back with it: a version that comes back for changes has not been reviewed in the state it will be in.',
  'admin.template.action.return.reason': 'What needs changing?',
  'admin.template.action.approve': 'Approve',
  'admin.template.action.approve.title': 'Approve this version?',
  'admin.template.action.approve.body':
    'You are saying this is what should be measured. Somebody still has to publish it before anything is captured against it.',
  'admin.template.action.publish': 'Publish',
  'admin.template.action.publish.title': 'Publish this version?',
  'admin.template.action.publish.body':
    'Everything measured from now on is captured against it, and the version it replaces is retired in the same act. Measurements already taken still read through the version they were captured under.',
  'admin.template.action.publish.reason': 'Why are you publishing this version?',
  'admin.template.action.retire': 'Retire',
  'admin.template.action.retire.title': 'Retire this version?',
  'admin.template.action.retire.body':
    'Nothing new is captured against it. Everything already captured still reads through it. This is refused while the published catalogue still points at this template.',
  'admin.template.action.retire.reason': 'Why are you retiring this version?',
  'admin.template.done': 'Version {number} — done.',
  'admin.template.validationRefused':
    'This version cannot be published yet: the checks that run before publication found something to fix. Open the version and correct what it reports, then publish again.',
  'admin.template.selfApproval':
    'The administrator who submitted a version does not also approve it, unless they are the only one who could. Ask a second administrator to review it.',

  /* The draft field editor (#102). ------------------------------------------------------------ */
  'admin.field.editor.title': 'Fields of version {number}',
  'admin.field.editor.loading': 'this version',
  'admin.field.editor.notDraft':
    'Only a draft can be edited. Start a draft from this version to change what is measured; what has already been captured is left alone.',
  'admin.field.editor.notFound': 'No version of this template matches that address.',
  'admin.field.editor.open': 'Edit the fields',
  'admin.field.add': 'Add a field',
  'admin.field.edit': 'Edit {label}',
  'admin.field.remove': 'Remove {label}',
  'admin.field.addTitle': 'A new field',
  'admin.field.editTitle': 'Editing {label}',
  'admin.field.save': 'Save this field',
  'admin.field.added': 'Added {label}.',
  'admin.field.changed': 'Saved {label}.',
  'admin.field.removed': 'Removed {label}.',
  'admin.field.remove.title': 'Remove this field?',
  'admin.field.remove.body':
    'The field is removed from this draft. Versions that are already published keep it, and every measurement already captured under it is untouched — a draft is not measuring anything yet.',
  'admin.field.remove.reason': 'Why are you removing this field?',
  'admin.field.key': 'Key',
  'admin.field.key.hint':
    'What captured values are filed under. Two to sixty characters, lower case, starting with a letter; letters, digits and underscores.',
  'admin.field.key.fixedHint':
    'A key cannot be renamed. Values already captured are filed under it, so the server ignores a key sent with an edit. To change one, remove this field and add it again — two acts, both recorded, and the field starts a new history.',
  'admin.field.label': 'Label',
  'admin.field.label.hint': 'What a tailor reads on the capture screen.',
  'admin.field.labelTamil': 'Label in Tamil',
  'admin.field.labelTamil.hint': 'Optional. Left empty, the English label is shown in both.',
  'admin.field.group': 'Step',
  'admin.field.group.hint':
    'Fields sharing a step are measured together. Arranging the steps is a separate screen.',
  'admin.field.help': 'How to measure it',
  'admin.field.help.hint':
    'Required. Say whether this is measured on the body or on a finished garment — confusing the two is the commonest cause of a re-make.',
  'admin.field.unit': 'Stored as',
  'admin.field.unit.Millimetre': 'A length',
  'admin.field.unit.Count': 'A count',
  'admin.field.unit.None': 'A choice',
  'admin.field.unit.hint':
    'A length is entered in inches or centimetres and stored in millimetres. A count is a whole number. A choice offers a fixed list and is not a measurement at all.',
  'admin.field.inchFraction': 'Inch step',
  'admin.field.inchFraction.hint': 'How finely the tape is read. Halving, as a tape is divided.',
  'admin.field.inchFraction.none': 'Not shown in inches',
  'admin.field.inchFraction.value': 'To the nearest 1/{denominator}',
  'admin.field.centimetreDecimals': 'Centimetre places',
  'admin.field.centimetreDecimals.none': 'Not shown in centimetres',
  'admin.field.centimetreDecimals.value':
    '{places, plural, one {# decimal place} other {# decimal places}}',
  'admin.field.required': 'A tailor must fill this in',
  'admin.field.diagramKey': 'Diagram',
  'admin.field.diagramKey.hint':
    'The name of a bundled sheet. There is no picker and nothing yet renders it: a field can reference a diagram, and choosing one arrives with the media library.',
  'admin.field.diagramAlt': 'Describe the diagram',
  'admin.field.diagramAlt.hint':
    'Required once a diagram is named. It is what somebody who cannot see the picture is told instead.',
  'admin.field.wholeNumber':
    'A count is a whole number, so it has no inch step and no decimal places.',
  'admin.field.choiceShape':
    'A choice has no range and no units. It has a list, and a tailor picks from it.',
  'admin.field.bandsElsewhere':
    'The range this field accepts is set on its own screen, and a save here leaves it as it is.',
  'admin.field.options': 'The choices',
  'admin.field.options.add': 'Add a choice',
  'admin.field.options.remove': 'Remove choice {position}',
  'admin.field.option.code': 'Stored value',
  'admin.field.option.code.hint': 'Upper case, starting with a letter or a digit.',
  'admin.field.option.label': 'What a tailor reads',
  'admin.field.option.labelTamil': 'In Tamil',
  'admin.field.options.empty': 'A choice field needs at least one choice before it can be saved.',
  'admin.field.error.summary': 'This field cannot be saved yet',
  'admin.field.error.keyRequired': 'Give this field a key.',
  'admin.field.error.keyMalformed':
    'A key is 2 to 60 characters, lower case, starting with a letter, and uses only letters, digits and underscores.',
  'admin.field.error.keyTaken': 'Another field in this version already uses that key.',
  'admin.field.error.labelRequired': 'Give this field a label.',
  'admin.field.error.groupRequired': 'Say which step this field is measured in.',
  'admin.field.error.helpRequired': 'Say how this is measured.',
  'admin.field.error.altRequired': 'Describe the diagram, or remove it.',
  'admin.field.error.tooLong': 'Shorten this to {limit} characters or fewer.',
  'admin.field.error.precisionMissing':
    'A length needs an inch step, decimal centimetre places, or both — otherwise there is no unit to enter it in.',
  'admin.field.error.fractionNotPermitted': 'Choose a step a tape is divided into.',
  'admin.field.error.optionsRequired': 'Add at least one choice.',
  'admin.field.error.optionCode':
    'A stored value is up to 40 characters, upper case, starting with a letter or a digit.',
  'admin.field.error.optionDuplicate': 'Two choices cannot share a stored value.',
  'admin.field.error.optionLabel': 'Give this choice something a tailor can read.',
} as const

/*
 * Every key repeated rather than mapped from the English catalogue, and that is deliberate. Deriving
 * one from the other would keep them in step for free — and would also make the whole family count as
 * translated to any tool that reads these files, because the `not translated` markers the 95%
 * enablement gate is written against would not exist. The duplication is the record.
 */
export const adminTa: Record<keyof typeof adminEn, string> = {
  // not translated — awaiting native-speaker review
  'admin.title': 'Administration',
  // not translated — awaiting native-speaker review
  'admin.body':
    'Staff accounts, branches, roles and the settings that govern how this shop works. Every change here is recorded with your name and the reason you give.',
  // not translated — awaiting native-speaker review
  'admin.nav.label': 'Administration sections',
  // not translated — awaiting native-speaker review
  'admin.nav.users': 'Staff accounts',
  // not translated — awaiting native-speaker review
  'admin.nav.branches': 'Branches',
  // not translated — awaiting native-speaker review
  'admin.nav.roles': 'Roles and permissions',
  // not translated — awaiting native-speaker review
  'admin.nav.features': 'Feature settings',
  // not translated — awaiting native-speaker review
  'admin.nav.audit': 'Audit trail',
  // not translated — awaiting native-speaker review
  'admin.nav.outbox': 'Failed messages',
  // not translated — awaiting native-speaker review
  'admin.forbidden.title': 'You do not have access to this',
  // not translated — awaiting native-speaker review
  'admin.forbidden.body':
    'This screen needs a permission your account does not hold. If you need it, ask the shop owner.',
  // not translated — awaiting native-speaker review
  'admin.reason.label': 'Reason',
  // not translated — awaiting native-speaker review
  'admin.reason.hint': 'Recorded in the audit trail with your name. A sentence is enough.',
  // not translated — awaiting native-speaker review
  'admin.reason.required': 'Give a reason before continuing.',
  // not translated — awaiting native-speaker review
  'admin.reason.tooLong': 'Shorten this to 500 characters or fewer.',
  // not translated — awaiting native-speaker review
  'admin.conflict.title': 'Somebody else changed this first',
  // not translated — awaiting native-speaker review
  'admin.conflict.body':
    'This was edited while you had it open, so your change was not applied. Reload it, check the change is still the one you want, and apply it again.',
  // not translated — awaiting native-speaker review
  'admin.stepUp.title': 'Confirm it is you',
  // not translated — awaiting native-speaker review
  'admin.stepUp.body':
    'This change needs a fresh check that it is you. Answer your second factor and it will continue.',
  // not translated — awaiting native-speaker review
  'admin.saved': 'Saved.',
  // not translated — awaiting native-speaker review
  'admin.reload': 'Reload',
  // not translated — awaiting native-speaker review
  'admin.cancel': 'Cancel',
  // not translated — awaiting native-speaker review
  'admin.back': 'Back',
  // not translated — awaiting native-speaker review
  'admin.users.title': 'Staff accounts',
  // not translated — awaiting native-speaker review
  'admin.users.caption': 'Staff accounts, with their status and roles',
  // not translated — awaiting native-speaker review
  'admin.users.loading': 'staff accounts',
  // not translated — awaiting native-speaker review
  'admin.users.empty': 'No account matches what you are looking for.',
  // not translated — awaiting native-speaker review
  'admin.users.search.label': 'Search by name',
  // not translated — awaiting native-speaker review
  'admin.users.search.hint':
    'Matches names only. Searching by phone number or email address is deliberately not offered.',
  // not translated — awaiting native-speaker review
  'admin.users.filter.status': 'Status',
  // not translated — awaiting native-speaker review
  'admin.users.filter.anyStatus': 'Any status',
  // not translated — awaiting native-speaker review
  'admin.users.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'admin.users.column.signIn': 'Sign-in name',
  // not translated — awaiting native-speaker review
  'admin.users.column.status': 'Status',
  // not translated — awaiting native-speaker review
  'admin.users.column.roles': 'Roles',
  // not translated — awaiting native-speaker review
  'admin.users.column.lastSignIn': 'Last signed in',
  // not translated — awaiting native-speaker review
  'admin.users.neverSignedIn': 'Never',
  // not translated — awaiting native-speaker review
  'admin.users.noRoles': 'No role',
  // not translated — awaiting native-speaker review
  'admin.users.more': 'Show more accounts',
  // not translated — awaiting native-speaker review
  'admin.users.open': 'Open {name}',
  // not translated — awaiting native-speaker review
  'admin.users.invite': 'Invite somebody',
  // not translated — awaiting native-speaker review
  'admin.user.loading': 'this account',
  // not translated — awaiting native-speaker review
  'admin.user.notFound': 'No account matches that address.',
  // not translated — awaiting native-speaker review
  'admin.user.signInName': 'Sign-in name',
  // not translated — awaiting native-speaker review
  'admin.user.email': 'Email address',
  // not translated — awaiting native-speaker review
  'admin.user.status': 'Status',
  // not translated — awaiting native-speaker review
  'admin.user.secondFactor': 'Second factor',
  // not translated — awaiting native-speaker review
  'admin.user.lastSignIn': 'Last signed in',
  // not translated — awaiting native-speaker review
  'admin.user.created': 'Account created',
  // not translated — awaiting native-speaker review
  'admin.user.self':
    'This is your own account. Administering it here is refused, so that ending your own access is never one mis-click away.',
  // not translated — awaiting native-speaker review
  'admin.command.suspend': 'Suspend',
  // not translated — awaiting native-speaker review
  'admin.command.suspend.title': 'Suspend this account?',
  // not translated — awaiting native-speaker review
  'admin.command.suspend.body':
    '{name} will be signed out of every device immediately and will not be able to sign in again until somebody lifts the suspension. Nothing they have done is removed.',
  // not translated — awaiting native-speaker review
  'admin.command.suspend.reason': 'Why are you suspending this account?',
  // not translated — awaiting native-speaker review
  'admin.command.reinstate': 'Lift suspension',
  // not translated — awaiting native-speaker review
  'admin.command.reinstate.title': 'Lift the suspension?',
  // not translated — awaiting native-speaker review
  'admin.command.reinstate.body':
    '{name} will be able to sign in again with the password and second factor they already had.',
  // not translated — awaiting native-speaker review
  'admin.command.reinstate.reason': 'Why are you lifting the suspension?',
  // not translated — awaiting native-speaker review
  'admin.command.deactivate': 'Close account',
  // not translated — awaiting native-speaker review
  'admin.command.deactivate.title': 'Close this account for good?',
  // not translated — awaiting native-speaker review
  'admin.command.deactivate.body':
    '{name} will be signed out everywhere, every remembered device will be forgotten, and they will not be able to sign in again. Their orders, invoices and audit entries stay exactly as they are — nothing is deleted. Reopening the account later starts their password and second factor from scratch.',
  // not translated — awaiting native-speaker review
  'admin.command.deactivate.reason': 'Why are you closing this account?',
  // not translated — awaiting native-speaker review
  'admin.command.reactivate': 'Reopen account',
  // not translated — awaiting native-speaker review
  'admin.command.reactivate.title': 'Reopen this account?',
  // not translated — awaiting native-speaker review
  'admin.command.reactivate.body':
    'The account reopens as an invitation: the password and every second factor are cleared, and {name} sets them up again from the beginning. Do this only when you have confirmed in person who they are.',
  // not translated — awaiting native-speaker review
  'admin.command.reactivate.reason': 'Why are you reopening this account?',
  // not translated — awaiting native-speaker review
  'admin.command.reset-mfa': 'Reset second factor',
  // not translated — awaiting native-speaker review
  'admin.command.reset-mfa.title': 'Reset their second factor?',
  // not translated — awaiting native-speaker review
  'admin.command.reset-mfa.body':
    "{name}'s authenticator and recovery codes are cleared, and they enrol again the next time they sign in. Until they do, their account is protected by a password alone — so confirm in person who you are talking to before you do this.",
  // not translated — awaiting native-speaker review
  'admin.command.reset-mfa.reason': 'Why are you resetting the second factor?',
  // not translated — awaiting native-speaker review
  'admin.command.revoke-sessions': 'Sign out everywhere',
  // not translated — awaiting native-speaker review
  'admin.command.revoke-sessions.title': 'Sign this account out everywhere?',
  // not translated — awaiting native-speaker review
  'admin.command.revoke-sessions.body':
    '{name} is signed out of every device at once, including the one they may be standing at. They can sign straight back in — this ends the sessions, it does not stop the account.',
  // not translated — awaiting native-speaker review
  'admin.command.revoke-sessions.reason': 'Why are you ending these sessions?',
  // not translated — awaiting native-speaker review
  'admin.command.done': '{name} — done.',
  // not translated — awaiting native-speaker review
  'admin.branches.title': 'Branches',
  // not translated — awaiting native-speaker review
  'admin.branches.caption': 'The branches this business trades from',
  // not translated — awaiting native-speaker review
  'admin.branches.loading': 'branches',
  // not translated — awaiting native-speaker review
  'admin.branches.empty': 'No branch has been opened yet.',
  // not translated — awaiting native-speaker review
  'admin.branches.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'admin.branches.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'admin.branches.column.timeZone': 'Timezone',
  // not translated — awaiting native-speaker review
  'admin.branches.column.status': 'Status',
  // not translated — awaiting native-speaker review
  'admin.branches.open': 'Open a branch',
  // not translated — awaiting native-speaker review
  'admin.branches.codeHint':
    'Set once and never again: this code is printed in every order, estimate and invoice number the branch produces.',
  // not translated — awaiting native-speaker review
  'admin.branches.code': 'Branch code',
  // not translated — awaiting native-speaker review
  'admin.branches.name': 'Branch name',
  // not translated — awaiting native-speaker review
  'admin.branches.timeZone': 'Timezone',
  // not translated — awaiting native-speaker review
  'admin.branches.timeZoneHint':
    'Every due date, SLA clock and report cut-off for this branch is worked out in this timezone.',
  // not translated — awaiting native-speaker review
  'admin.branches.reason.open': 'Why are you opening this branch?',
  // not translated — awaiting native-speaker review
  'admin.branches.status.open': 'Trading',
  // not translated — awaiting native-speaker review
  'admin.branches.status.closed': 'Closed',
  // not translated — awaiting native-speaker review
  'admin.branches.close': 'Close branch',
  // not translated — awaiting native-speaker review
  'admin.branches.closeTitle': 'Close this branch?',
  // not translated — awaiting native-speaker review
  'admin.branches.closeBody':
    'The branch stops trading. Nothing is deleted — every order, estimate and invoice it produced keeps its number, and the number keeps its code. Closing is refused while anybody is still assigned to work here.',
  // not translated — awaiting native-speaker review
  'admin.branches.reason.close': 'Why are you closing this branch?',
  // not translated — awaiting native-speaker review
  'admin.branches.reopen': 'Reopen branch',
  // not translated — awaiting native-speaker review
  'admin.branches.reopenTitle': 'Reopen this branch?',
  // not translated — awaiting native-speaker review
  'admin.branches.reopenBody': 'The branch trades again, under the code it always had.',
  // not translated — awaiting native-speaker review
  'admin.branches.reason.reopen': 'Why are you reopening this branch?',
  // not translated — awaiting native-speaker review
  'admin.roles.title': 'Roles and permissions',
  // not translated — awaiting native-speaker review
  'admin.roles.caption': 'The roles this business uses, and what each one allows',
  // not translated — awaiting native-speaker review
  'admin.roles.loading': 'roles',
  // not translated — awaiting native-speaker review
  'admin.roles.empty': 'No role has been defined yet.',
  // not translated — awaiting native-speaker review
  'admin.roles.column.name': 'Role',
  // not translated — awaiting native-speaker review
  'admin.roles.column.reach': 'Reach',
  // not translated — awaiting native-speaker review
  'admin.roles.column.grants': 'Permissions',
  // not translated — awaiting native-speaker review
  'admin.roles.column.holders': 'People',
  // not translated — awaiting native-speaker review
  'admin.roles.system': 'Shipped with the application',
  // not translated — awaiting native-speaker review
  'admin.roles.reach.Branch': 'One branch',
  // not translated — awaiting native-speaker review
  'admin.roles.reach.Organisation': 'The whole business',
  // not translated — awaiting native-speaker review
  'admin.roles.holders': '{count, plural, =0 {Nobody} one {1 person} other {# people}}',
  // not translated — awaiting native-speaker review
  'admin.roles.grantCount':
    '{count, plural, =0 {Nothing yet} one {1 permission} other {# permissions}}',
  // not translated — awaiting native-speaker review
  'admin.role.loading': 'this role',
  // not translated — awaiting native-speaker review
  'admin.role.notFound': 'No role matches that address.',
  // not translated — awaiting native-speaker review
  'admin.role.permissions': 'What this role allows',
  // not translated — awaiting native-speaker review
  'admin.role.permissionsHint':
    'Tick every permission this role should grant when you are finished, not just the ones you are adding. You can only grant something you hold yourself.',
  // not translated — awaiting native-speaker review
  'admin.role.save': 'Save permissions',
  // not translated — awaiting native-speaker review
  'admin.role.reason': 'Why are you changing what this role allows?',
  // not translated — awaiting native-speaker review
  'admin.role.flag.mfa': 'Needs a second factor',
  // not translated — awaiting native-speaker review
  'admin.role.flag.stepUp': 'Needs a fresh check of identity each time',
  // not translated — awaiting native-speaker review
  'admin.role.flag.reason': 'Needs a written reason each time',
  // not translated — awaiting native-speaker review
  'admin.features.title': 'Feature settings',
  // not translated — awaiting native-speaker review
  'admin.features.caption': 'The settings that switch parts of this application on and off',
  // not translated — awaiting native-speaker review
  'admin.features.loading': 'feature settings',
  // not translated — awaiting native-speaker review
  'admin.features.empty': 'No setting has been configured yet.',
  // not translated — awaiting native-speaker review
  'admin.features.column.key': 'Setting',
  // not translated — awaiting native-speaker review
  'admin.features.column.state': 'State',
  // not translated — awaiting native-speaker review
  'admin.features.column.changed': 'Last changed',
  // not translated — awaiting native-speaker review
  'admin.features.column.reason': 'Why',
  // not translated — awaiting native-speaker review
  'admin.features.on': 'On',
  // not translated — awaiting native-speaker review
  'admin.features.off': 'Off',
  // not translated — awaiting native-speaker review
  'admin.features.turnOn': 'Turn on',
  // not translated — awaiting native-speaker review
  'admin.features.turnOff': 'Turn off',
  // not translated — awaiting native-speaker review
  'admin.features.confirmOn': 'Turn this on?',
  // not translated — awaiting native-speaker review
  'admin.features.confirmOff': 'Turn this off?',
  // not translated — awaiting native-speaker review
  'admin.features.body':
    'Every till and tablet picks this up within about {seconds, plural, one {# second} other {# seconds}}. Until they do, some devices will still be working the old way.',
  // not translated — awaiting native-speaker review
  'admin.features.reason': 'Why are you changing this setting?',
  // not translated — awaiting native-speaker review
  'admin.features.neverChanged': 'Never changed',
  // not translated — awaiting native-speaker review
  'admin.audit.title': 'Audit trail',
  // not translated — awaiting native-speaker review
  'admin.audit.caption': 'What was changed, by whom, and why',
  // not translated — awaiting native-speaker review
  'admin.audit.loading': 'the audit trail',
  // not translated — awaiting native-speaker review
  'admin.audit.empty': 'Nothing matches what you are looking for.',
  // not translated — awaiting native-speaker review
  'admin.audit.filter.action': 'Action starts with',
  // not translated — awaiting native-speaker review
  'admin.audit.filter.apply': 'Search the trail',
  // not translated — awaiting native-speaker review
  'admin.audit.column.when': 'When',
  // not translated — awaiting native-speaker review
  'admin.audit.column.what': 'What happened',
  // not translated — awaiting native-speaker review
  'admin.audit.column.who': 'Who',
  // not translated — awaiting native-speaker review
  'admin.audit.column.why': 'Why',
  // not translated — awaiting native-speaker review
  'admin.audit.noReason': 'No reason recorded',
  // not translated — awaiting native-speaker review
  'admin.audit.before': 'Before',
  // not translated — awaiting native-speaker review
  'admin.audit.after': 'After',
  // not translated — awaiting native-speaker review
  'admin.audit.details': 'Show what changed',
  // not translated — awaiting native-speaker review
  'admin.audit.more': 'Show older entries',
  // not translated — awaiting native-speaker review
  'admin.audit.correlation': 'Request {correlationId}',
  // not translated — awaiting native-speaker review
  'admin.outbox.title': 'Failed messages',
  // not translated — awaiting native-speaker review
  'admin.outbox.caption': 'Messages that could not be delivered and were given up on',
  // not translated — awaiting native-speaker review
  'admin.outbox.loading': 'failed messages',
  // not translated — awaiting native-speaker review
  'admin.outbox.empty': 'Nothing has failed. Everything the shop has published has been delivered.',
  // not translated — awaiting native-speaker review
  'admin.outbox.column.event': 'Message',
  // not translated — awaiting native-speaker review
  'admin.outbox.column.when': 'Failed',
  // not translated — awaiting native-speaker review
  'admin.outbox.column.attempts': 'Attempts',
  // not translated — awaiting native-speaker review
  'admin.outbox.column.error': 'What went wrong',
  // not translated — awaiting native-speaker review
  'admin.outbox.replay': 'Send again',
  // not translated — awaiting native-speaker review
  'admin.outbox.replayTitle': 'Put this message back on the queue?',
  // not translated — awaiting native-speaker review
  'admin.outbox.replayBody':
    'It will be delivered again. If it had in fact reached its destination before it was given up on, whoever receives it gets it twice — a second message to a customer, or a second entry in an accounting system. Check that the problem behind it is fixed before sending it again.',
  // not translated — awaiting native-speaker review
  'admin.outbox.reason': 'Why are you sending this again?',
  // not translated — awaiting native-speaker review
  'admin.outbox.attempts': '{count, plural, one {1 attempt} other {# attempts}}',
  // not translated — awaiting native-speaker review
  'admin.outbox.gone':
    'That message is no longer waiting — somebody else has already sent it again, or it went through on its own.',
  // not translated — awaiting native-speaker review
  'admin.access.title': 'Roles and branches',
  // not translated — awaiting native-speaker review
  'admin.access.loading': 'this account’s access',
  // not translated — awaiting native-speaker review
  'admin.access.roles': 'Roles',
  // not translated — awaiting native-speaker review
  'admin.access.rolesHint':
    'What this person may do. Tick every role they should hold when you are finished, not just the ones you are adding.',
  // not translated — awaiting native-speaker review
  'admin.access.branches': 'Branches',
  // not translated — awaiting native-speaker review
  'admin.access.branchesHint':
    'Where they may work. The primary branch is the one their screens open on.',
  // not translated — awaiting native-speaker review
  'admin.access.primary': 'Primary',
  // not translated — awaiting native-speaker review
  'admin.access.save': 'Save roles',
  // not translated — awaiting native-speaker review
  'admin.access.saveBranches': 'Save branches',
  // not translated — awaiting native-speaker review
  'admin.access.reason.roles': 'Why are you changing what this person may do?',
  // not translated — awaiting native-speaker review
  'admin.access.reason.branches': 'Why are you changing where this person works?',
  // not translated — awaiting native-speaker review
  'admin.access.lastAdministrator':
    'This would leave nobody able to administer accounts. Give somebody else an administrative role first, then take this one away.',
  // not translated — awaiting native-speaker review
  'admin.nav.templates': 'Measurement templates',
  // not translated — awaiting native-speaker review
  'admin.templates.title': 'Measurement templates',
  // not translated — awaiting native-speaker review
  'admin.templates.loading': 'measurement templates',
  // not translated — awaiting native-speaker review
  'admin.templates.empty':
    'No measurement template has been set up yet. Until one is published, nothing can be measured.',
  // not translated — awaiting native-speaker review
  'admin.templates.caption': 'Measurement templates, with the version each is capturing against',
  // not translated — awaiting native-speaker review
  'admin.templates.column.code': 'Code',
  // not translated — awaiting native-speaker review
  'admin.templates.column.name': 'Name',
  // not translated — awaiting native-speaker review
  'admin.templates.column.published': 'Capturing against',
  // not translated — awaiting native-speaker review
  'admin.templates.column.inProgress': 'In progress',
  // not translated — awaiting native-speaker review
  'admin.templates.none': 'Nothing published',
  // not translated — awaiting native-speaker review
  'admin.templates.open': 'Open {name}',
  // not translated — awaiting native-speaker review
  'admin.templates.version': 'Version {number}',
  // not translated — awaiting native-speaker review
  'admin.templates.inProgress':
    '{count, plural, =0 {Nothing in progress} one {1 version} other {# versions}}',
  // not translated — awaiting native-speaker review
  'admin.templates.notPublishedHint':
    'A template captures nothing until one of its versions is published. Drafting, review and publication are separate acts, and the person who submits a version is not the person who approves it.',
  // not translated — awaiting native-speaker review
  'admin.template.loading': 'this template',
  // not translated — awaiting native-speaker review
  'admin.template.versions': 'Versions',
  // not translated — awaiting native-speaker review
  'admin.template.versionsCaption': 'Every version of this template, newest first',
  // not translated — awaiting native-speaker review
  'admin.template.column.version': 'Version',
  // not translated — awaiting native-speaker review
  'admin.template.column.status': 'State',
  // not translated — awaiting native-speaker review
  'admin.template.column.name': 'What changed',
  // not translated — awaiting native-speaker review
  'admin.template.column.fields': 'Fields',
  // not translated — awaiting native-speaker review
  'admin.template.fields': '{count, plural, =0 {No field} one {1 field} other {# fields}}',
  // not translated — awaiting native-speaker review
  'admin.template.readOnly':
    'This version is published and cannot be edited. Change it by starting a draft from it, which copies its fields and leaves what has already been captured alone.',
  // not translated — awaiting native-speaker review
  'admin.template.clone': 'Start a draft from this version',
  // not translated — awaiting native-speaker review
  'admin.template.fieldsCaption': 'The fields of this version, in the order they are measured',
  // not translated — awaiting native-speaker review
  'admin.template.column.key': 'Key',
  // not translated — awaiting native-speaker review
  'admin.template.column.label': 'Label',
  // not translated — awaiting native-speaker review
  'admin.template.column.group': 'Step',
  // not translated — awaiting native-speaker review
  'admin.template.column.unit': 'Stored as',
  // not translated — awaiting native-speaker review
  'admin.template.column.range': 'Range',
  // not translated — awaiting native-speaker review
  'admin.template.column.required': 'Required',
  // not translated — awaiting native-speaker review
  'admin.template.required': 'Required',
  // not translated — awaiting native-speaker review
  'admin.template.optional': 'Optional',
  // not translated — awaiting native-speaker review
  'admin.template.millimetres': '{from}–{to} mm',
  // not translated — awaiting native-speaker review
  'admin.template.noFields':
    'This version has no fields yet. A version with no fields cannot be published.',
  // not translated — awaiting native-speaker review
  'admin.template.noVersions':
    'This template has no versions yet. Start a draft to describe what is measured.',
  // not translated — awaiting native-speaker review
  'admin.template.approved': 'Approved, ready to publish',
  // not translated — awaiting native-speaker review
  'admin.template.awaitingApproval': 'Waiting for a second administrator',
  // not translated — awaiting native-speaker review
  'admin.template.needsPublish':
    'You can draft a version and submit it for review. Sending one back, approving it, publishing it and retiring it are the reviewing administrator’s acts and need the template publishing permission — ask the shop owner if you need it.',
  // not translated — awaiting native-speaker review
  'admin.template.status.Draft': 'Draft',
  // not translated — awaiting native-speaker review
  'admin.template.status.InReview': 'In review',
  // not translated — awaiting native-speaker review
  'admin.template.status.Published': 'Published',
  // not translated — awaiting native-speaker review
  'admin.template.status.Retired': 'Retired',
  // not translated — awaiting native-speaker review
  'admin.template.status.unknown': 'A state this version of the application does not know',
  // not translated — awaiting native-speaker review
  'admin.template.action.submit': 'Submit for review',
  // not translated — awaiting native-speaker review
  'admin.template.action.submit.title': 'Submit this version for review?',
  // not translated — awaiting native-speaker review
  'admin.template.action.submit.body':
    'It stops being editable straight away, so that whoever reviews it reads a version that cannot change under them.',
  // not translated — awaiting native-speaker review
  'admin.template.action.return': 'Send back',
  // not translated — awaiting native-speaker review
  'admin.template.action.return.title': 'Send this version back to its author?',
  // not translated — awaiting native-speaker review
  'admin.template.action.return.body':
    'It becomes editable again and its approval goes back with it: a version that comes back for changes has not been reviewed in the state it will be in.',
  // not translated — awaiting native-speaker review
  'admin.template.action.return.reason': 'What needs changing?',
  // not translated — awaiting native-speaker review
  'admin.template.action.approve': 'Approve',
  // not translated — awaiting native-speaker review
  'admin.template.action.approve.title': 'Approve this version?',
  // not translated — awaiting native-speaker review
  'admin.template.action.approve.body':
    'You are saying this is what should be measured. Somebody still has to publish it before anything is captured against it.',
  // not translated — awaiting native-speaker review
  'admin.template.action.publish': 'Publish',
  // not translated — awaiting native-speaker review
  'admin.template.action.publish.title': 'Publish this version?',
  // not translated — awaiting native-speaker review
  'admin.template.action.publish.body':
    'Everything measured from now on is captured against it, and the version it replaces is retired in the same act. Measurements already taken still read through the version they were captured under.',
  // not translated — awaiting native-speaker review
  'admin.template.action.publish.reason': 'Why are you publishing this version?',
  // not translated — awaiting native-speaker review
  'admin.template.action.retire': 'Retire',
  // not translated — awaiting native-speaker review
  'admin.template.action.retire.title': 'Retire this version?',
  // not translated — awaiting native-speaker review
  'admin.template.action.retire.body':
    'Nothing new is captured against it. Everything already captured still reads through it. This is refused while the published catalogue still points at this template.',
  // not translated — awaiting native-speaker review
  'admin.template.action.retire.reason': 'Why are you retiring this version?',
  // not translated — awaiting native-speaker review
  'admin.template.done': 'Version {number} — done.',
  // not translated — awaiting native-speaker review
  'admin.template.validationRefused':
    'This version cannot be published yet: the checks that run before publication found something to fix. Open the version and correct what it reports, then publish again.',
  // not translated — awaiting native-speaker review
  'admin.template.selfApproval':
    'The administrator who submitted a version does not also approve it, unless they are the only one who could. Ask a second administrator to review it.',

  /* The draft field editor (#102). ------------------------------------------------------------ */
  // not translated — awaiting native-speaker review
  'admin.field.editor.title': 'Fields of version {number}',
  // not translated — awaiting native-speaker review
  'admin.field.editor.loading': 'this version',
  // not translated — awaiting native-speaker review
  'admin.field.editor.notDraft':
    'Only a draft can be edited. Start a draft from this version to change what is measured; what has already been captured is left alone.',
  // not translated — awaiting native-speaker review
  'admin.field.editor.notFound': 'No version of this template matches that address.',
  // not translated — awaiting native-speaker review
  'admin.field.editor.open': 'Edit the fields',
  // not translated — awaiting native-speaker review
  'admin.field.add': 'Add a field',
  // not translated — awaiting native-speaker review
  'admin.field.edit': 'Edit {label}',
  // not translated — awaiting native-speaker review
  'admin.field.remove': 'Remove {label}',
  // not translated — awaiting native-speaker review
  'admin.field.addTitle': 'A new field',
  // not translated — awaiting native-speaker review
  'admin.field.editTitle': 'Editing {label}',
  // not translated — awaiting native-speaker review
  'admin.field.save': 'Save this field',
  // not translated — awaiting native-speaker review
  'admin.field.added': 'Added {label}.',
  // not translated — awaiting native-speaker review
  'admin.field.changed': 'Saved {label}.',
  // not translated — awaiting native-speaker review
  'admin.field.removed': 'Removed {label}.',
  // not translated — awaiting native-speaker review
  'admin.field.remove.title': 'Remove this field?',
  // not translated — awaiting native-speaker review
  'admin.field.remove.body':
    'The field is removed from this draft. Versions that are already published keep it, and every measurement already captured under it is untouched — a draft is not measuring anything yet.',
  // not translated — awaiting native-speaker review
  'admin.field.remove.reason': 'Why are you removing this field?',
  // not translated — awaiting native-speaker review
  'admin.field.key': 'Key',
  // not translated — awaiting native-speaker review
  'admin.field.key.hint':
    'What captured values are filed under. Two to sixty characters, lower case, starting with a letter; letters, digits and underscores.',
  // not translated — awaiting native-speaker review
  'admin.field.key.fixedHint':
    'A key cannot be renamed. Values already captured are filed under it, so the server ignores a key sent with an edit. To change one, remove this field and add it again — two acts, both recorded, and the field starts a new history.',
  // not translated — awaiting native-speaker review
  'admin.field.label': 'Label',
  // not translated — awaiting native-speaker review
  'admin.field.label.hint': 'What a tailor reads on the capture screen.',
  // not translated — awaiting native-speaker review
  'admin.field.labelTamil': 'Label in Tamil',
  // not translated — awaiting native-speaker review
  'admin.field.labelTamil.hint': 'Optional. Left empty, the English label is shown in both.',
  // not translated — awaiting native-speaker review
  'admin.field.group': 'Step',
  // not translated — awaiting native-speaker review
  'admin.field.group.hint':
    'Fields sharing a step are measured together. Arranging the steps is a separate screen.',
  // not translated — awaiting native-speaker review
  'admin.field.help': 'How to measure it',
  // not translated — awaiting native-speaker review
  'admin.field.help.hint':
    'Required. Say whether this is measured on the body or on a finished garment — confusing the two is the commonest cause of a re-make.',
  // not translated — awaiting native-speaker review
  'admin.field.unit': 'Stored as',
  // not translated — awaiting native-speaker review
  'admin.field.unit.Millimetre': 'A length',
  // not translated — awaiting native-speaker review
  'admin.field.unit.Count': 'A count',
  // not translated — awaiting native-speaker review
  'admin.field.unit.None': 'A choice',
  // not translated — awaiting native-speaker review
  'admin.field.unit.hint':
    'A length is entered in inches or centimetres and stored in millimetres. A count is a whole number. A choice offers a fixed list and is not a measurement at all.',
  // not translated — awaiting native-speaker review
  'admin.field.inchFraction': 'Inch step',
  // not translated — awaiting native-speaker review
  'admin.field.inchFraction.hint': 'How finely the tape is read. Halving, as a tape is divided.',
  // not translated — awaiting native-speaker review
  'admin.field.inchFraction.none': 'Not shown in inches',
  // not translated — awaiting native-speaker review
  'admin.field.inchFraction.value': 'To the nearest 1/{denominator}',
  // not translated — awaiting native-speaker review
  'admin.field.centimetreDecimals': 'Centimetre places',
  // not translated — awaiting native-speaker review
  'admin.field.centimetreDecimals.none': 'Not shown in centimetres',
  // not translated — awaiting native-speaker review
  'admin.field.centimetreDecimals.value':
    '{places, plural, one {# decimal place} other {# decimal places}}',
  // not translated — awaiting native-speaker review
  'admin.field.required': 'A tailor must fill this in',
  // not translated — awaiting native-speaker review
  'admin.field.diagramKey': 'Diagram',
  // not translated — awaiting native-speaker review
  'admin.field.diagramKey.hint':
    'The name of a bundled sheet. There is no picker and nothing yet renders it: a field can reference a diagram, and choosing one arrives with the media library.',
  // not translated — awaiting native-speaker review
  'admin.field.diagramAlt': 'Describe the diagram',
  // not translated — awaiting native-speaker review
  'admin.field.diagramAlt.hint':
    'Required once a diagram is named. It is what somebody who cannot see the picture is told instead.',
  // not translated — awaiting native-speaker review
  'admin.field.wholeNumber':
    'A count is a whole number, so it has no inch step and no decimal places.',
  // not translated — awaiting native-speaker review
  'admin.field.choiceShape':
    'A choice has no range and no units. It has a list, and a tailor picks from it.',
  // not translated — awaiting native-speaker review
  'admin.field.bandsElsewhere':
    'The range this field accepts is set on its own screen, and a save here leaves it as it is.',
  // not translated — awaiting native-speaker review
  'admin.field.options': 'The choices',
  // not translated — awaiting native-speaker review
  'admin.field.options.add': 'Add a choice',
  // not translated — awaiting native-speaker review
  'admin.field.options.remove': 'Remove choice {position}',
  // not translated — awaiting native-speaker review
  'admin.field.option.code': 'Stored value',
  // not translated — awaiting native-speaker review
  'admin.field.option.code.hint': 'Upper case, starting with a letter or a digit.',
  // not translated — awaiting native-speaker review
  'admin.field.option.label': 'What a tailor reads',
  // not translated — awaiting native-speaker review
  'admin.field.option.labelTamil': 'In Tamil',
  // not translated — awaiting native-speaker review
  'admin.field.options.empty': 'A choice field needs at least one choice before it can be saved.',
  // not translated — awaiting native-speaker review
  'admin.field.error.summary': 'This field cannot be saved yet',
  // not translated — awaiting native-speaker review
  'admin.field.error.keyRequired': 'Give this field a key.',
  // not translated — awaiting native-speaker review
  'admin.field.error.keyMalformed':
    'A key is 2 to 60 characters, lower case, starting with a letter, and uses only letters, digits and underscores.',
  // not translated — awaiting native-speaker review
  'admin.field.error.keyTaken': 'Another field in this version already uses that key.',
  // not translated — awaiting native-speaker review
  'admin.field.error.labelRequired': 'Give this field a label.',
  // not translated — awaiting native-speaker review
  'admin.field.error.groupRequired': 'Say which step this field is measured in.',
  // not translated — awaiting native-speaker review
  'admin.field.error.helpRequired': 'Say how this is measured.',
  // not translated — awaiting native-speaker review
  'admin.field.error.altRequired': 'Describe the diagram, or remove it.',
  // not translated — awaiting native-speaker review
  'admin.field.error.tooLong': 'Shorten this to {limit} characters or fewer.',
  // not translated — awaiting native-speaker review
  'admin.field.error.precisionMissing':
    'A length needs an inch step, decimal centimetre places, or both — otherwise there is no unit to enter it in.',
  // not translated — awaiting native-speaker review
  'admin.field.error.fractionNotPermitted': 'Choose a step a tape is divided into.',
  // not translated — awaiting native-speaker review
  'admin.field.error.optionsRequired': 'Add at least one choice.',
  // not translated — awaiting native-speaker review
  'admin.field.error.optionCode':
    'A stored value is up to 40 characters, upper case, starting with a letter or a digit.',
  // not translated — awaiting native-speaker review
  'admin.field.error.optionDuplicate': 'Two choices cannot share a stored value.',
  // not translated — awaiting native-speaker review
  'admin.field.error.optionLabel': 'Give this choice something a tailor can read.',
}
