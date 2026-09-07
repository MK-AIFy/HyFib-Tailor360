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
}
