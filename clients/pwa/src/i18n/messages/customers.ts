/**
 * Customer messages — finding a customer, registering one, and the duplicate check in between.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. Two bind hardest here:
 *
 *  - **A duplicate suggestion explains the match, never just flags it.** "Same phone number as
 *    Priya S." is a sentence a receptionist can act on with a customer standing at the counter;
 *    "possible duplicate" is not, and issue #26 is explicit that a suggestion must say why.
 *  - **The search hint matches the server's own rule exactly.** The minimum length is read from
 *    `CUSTOMER_SEARCH_MINIMUM_LENGTH`, not retyped here, so the two can never disagree.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * The same reason recorded for the authentication and administration families. A customer's name,
 * once mistyped or mistranslated at the counter, is what she is called on every receipt afterwards.
 * These are held back for native-speaker review with the rest of the shop-floor vocabulary, and stay
 * behind the 95% enablement gate until then.
 */
export const customersEn = {
  /* The destination. ------------------------------------------------------------------------ */
  'customers.title': 'Customers',
  'customers.body': 'Find a customer, or register somebody new.',

  /* Search. ----------------------------------------------------------------------------------- */
  'customers.search.title': 'Find a customer',
  'customers.search.body':
    'Search by name, customer number, or the last digits of the telephone number.',
  'customers.search.label': 'Search',
  'customers.search.hint': 'At least {minimum} characters.',
  'customers.search.tooShort': 'Type at least {minimum} characters.',
  'customers.search.action': 'Search',
  'customers.search.searching': 'Searching…',
  'customers.search.loading': 'the search results',
  'customers.search.empty': 'Nobody matched. Check the spelling, or register a new customer.',
  'customers.search.results': 'Matching customers',
  'customers.search.more': 'More matched than are shown. Narrow the search to see the rest.',
  'customers.search.card': '{name}, {number}, {phone}',
  'customers.search.masked': '{name}, {number} — at another branch',
  'customers.search.maskedHint':
    'This record was created at a branch you are not assigned to. Open it to add your branch to its visibility.',
  'customers.search.createNew': 'Register a new customer',
  'customers.search.createNewHint':
    "Only once you're sure this is somebody new — check the results above first.",

  /* Detail. ------------------------------------------------------------------------------------ */
  'customers.detail.loading': 'the customer record',
  'customers.detail.notFound': 'This record could not be found, or is not one you can reach.',
  'customers.detail.number': 'Customer number {number}',
  'customers.detail.notGiven': 'Not given',
  'customers.detail.language': 'Written to in',
  'customers.detail.contactWithheld':
    'Contact details are shown to holders of a permission you do not have.',
  'customers.detail.aliases': 'Also known as',

  /* Create. ------------------------------------------------------------------------------------ */
  'customers.create.title': 'Register a customer',
  'customers.create.body': 'This creates a record at the branch you are working in.',
  'customers.create.field.displayName': 'Name',
  'customers.create.field.nativeName': 'Name (native script)',
  'customers.create.field.phone': 'Telephone number',
  'customers.create.field.alternatePhone': 'Second telephone number',
  'customers.create.field.email': 'Email address',
  'customers.create.field.addressLine': 'Address',
  'customers.create.field.locality': 'Area or town',
  'customers.create.field.postcode': 'PIN code',
  'customers.create.field.language': 'Written to in',
  'customers.create.field.language.en-IN': 'English',
  'customers.create.field.language.ta-IN': 'Tamil',
  'customers.create.action': 'Register',
  'customers.create.saving': 'Registering…',
  'customers.create.offlineAction': 'Registering a customer',

  /* The duplicate check. ------------------------------------------------------------------------ */
  'customers.create.duplicates.title': 'This may be somebody we already know',
  'customers.create.duplicates.body':
    'Read the records below before registering a new one — a second record for the same person is harder to undo than a moment spent checking.',
  'customers.create.duplicates.open': 'Open this record instead',
  'customers.create.duplicates.confidence.High': 'Strong match',
  'customers.create.duplicates.confidence.Medium': 'Possible match',
  'customers.create.duplicates.confidence.Low': 'Weak match',
  'customers.create.duplicates.reviewed': "I've checked — this is somebody new",
  'customers.create.duplicates.reviewedHint':
    'This is kept on record as your decision that the two are different people.',

  /* Correct. ----------------------------------------------------------------------------------- */
  'customers.detail.correct': 'Correct this record',
  'customers.edit.title': 'Correct a customer record',
  'customers.edit.body':
    'Change what the record says about the person. A changed name is kept as an alias, so she is still found under the name on her old receipts.',
  'customers.edit.loading': 'the customer record',
  'customers.edit.back': 'Back to the record',
  'customers.edit.reason': 'Why this correction',
  'customers.edit.reasonHint': 'Recorded in the audit trail with your name. A sentence is enough.',
  'customers.edit.action': 'Save the correction',
  'customers.edit.saving': 'Saving…',
  'customers.edit.saved': 'The correction was saved.',
  'customers.edit.offlineAction': 'Correcting a customer record',
  'customers.edit.nameRequired': 'A customer record must have a name.',
  'customers.edit.reasonRequired': 'Say why this record is being corrected.',
  'customers.edit.conflict.title': 'Somebody else changed this record',
  'customers.edit.conflict.body':
    'It was corrected by somebody else while you were typing. Reload it to see their version — what you typed is kept, so you can check it against theirs before saving again.',
  'customers.edit.conflict.reload': 'Reload the record',
  'customers.edit.contactWithheld':
    'Correcting a record sends every field back, contact details included — and those are shown to holders of a permission you do not have. Ask somebody who holds it to make this correction.',

  /* History. ------------------------------------------------------------------------------- */
  'customers.detail.tabs': 'Sections of this record',
  'customers.detail.tab.record': 'Details',
  'customers.detail.tab.history': 'History',
  'customers.timeline.label': 'What has happened to this customer',
  'customers.timeline.loading': 'this customer’s history',
  'customers.timeline.empty': 'Nothing has been recorded against this customer yet.',
  'customers.timeline.older': 'Show older',
  'customers.timeline.loadingOlder': 'Loading…',
  'customers.timeline.olderAdded':
    '{count, plural, one {# older entry added below} other {# older entries added below}}',
  'customers.timeline.actor.system': 'the system',
  'customers.timeline.reason': 'Reason: {reason}',
  'customers.timeline.reasonWithheld':
    'A reason was given. Reading it needs a permission you do not have.',
  'customers.timeline.partial.title': 'Part of this history could not be loaded',
  'customers.timeline.partial.body':
    '{count, plural, one {# part of the record} other {# parts of the record}} did not answer ({sources}), so this history is incomplete. Do not read a gap below as nothing having happened.',
  'customers.timeline.source.customers': 'customer records',
  'customers.timeline.source.orders': 'orders',
  'customers.timeline.source.billing': 'billing',
  'customers.timeline.source.custody': 'garment tracking',

  /* Duplicates and the merge. ---------------------------------------------------------------- */
  'customers.detail.duplicates': 'Check for duplicate records',
  'customers.merge.title': 'Possible duplicates of {name}',
  'customers.merge.body':
    '{name} ({number}) is the record that will survive. Anything you fold in here is merged into it and cannot be separated again.',
  'customers.merge.loading': 'the possible duplicates',
  'customers.merge.empty':
    'No other record resembles this one closely enough to be worth reviewing.',
  'customers.merge.open': 'Open this record',
  'customers.merge.action': 'Fold {number} into {name}',
  'customers.merge.offlineAction': 'Merging two customer records',
  'customers.merge.confirm.title': 'Fold {merged} into {survivor}?',
  'customers.merge.confirm.body':
    '{merged} ({mergedNumber}) will stop being used. {survivor} ({survivorNumber}) carries on, keeps {mergedNumber} searchable as an alias, and becomes visible to every branch that could see {mergedNumber}. Measurements and orders move across; anything already printed on an invoice or a job card is left exactly as it was.',
  'customers.merge.confirm.action': 'folding {number} into {name}',
  'customers.merge.confirm.label': 'Fold {number} in',
  'customers.merge.conflict.survivor':
    'The record that would survive has been corrected since you opened this screen. Close this, read it again, and decide once more — the pair you approved is not the pair that would be merged.',
  'customers.merge.conflict.merged':
    'The record you are folding in has been corrected since you opened this screen. Close this and read it again before deciding — this is the record that would stop existing.',
  'customers.merge.done.title': 'The records were merged',
  'customers.merge.done.body': '{number} was folded into {survivor}.',
  'customers.merge.done.aliases':
    '{count, plural, one {# alias recorded} other {# aliases recorded}} on the surviving record',
  'customers.merge.done.repointed':
    '{count, plural, one {# record re-pointed} other {# records re-pointed}} to the surviving customer',
  'customers.merge.done.branches':
    '{count, plural, one {# branch added} other {# branches added}} to what the surviving record is visible to',

  /* Consent and communication preferences. --------------------------------------------------- */
  'customers.detail.consent': 'Consent and how to reach her',
  'customers.consent.title': 'Consent and communication',
  'customers.consent.body':
    'What she has agreed to, and how she wants to be reached. An answer is added to the record rather than replacing the last one, so what she said before is still there.',
  'customers.consent.loading': 'her consent record',
  'customers.consent.empty': 'The shop asks about nothing that needs consent.',
  'customers.consent.status.Granted': 'She agreed',
  'customers.consent.status.Declined': 'She said no',
  'customers.consent.status.Withdrawn': 'She withdrew',
  'customers.consent.status.NeverAsked': 'Nobody has asked her',
  'customers.consent.retired': 'This is no longer asked about, so no answer can be recorded.',
  'customers.consent.noWording':
    'No wording has been published for this yet. An answer names the words she was read, and there are none to name.',
  'customers.consent.answers': 'What she has said',
  'customers.consent.answer': '{decision} — {when}, wording version {version} — {source}',
  'customers.consent.source': 'Where she said it',
  'customers.consent.sourceHint':
    'At the counter, over the telephone, on a signed form. Kept on the record exactly as you write it.',
  'customers.consent.sourceRequired': 'Say where she said it.',
  'customers.consent.grant': 'She agreed',
  'customers.consent.decline': 'She said no',
  'customers.consent.withdraw': 'She withdrew it',
  'customers.consent.offlineAction': 'Recording what she said',
  'customers.consent.recorded': 'Recorded: {decision}.',
  'customers.preferences.title': 'How to reach her',
  'customers.preferences.body':
    'This replaces what was there, so it always reads as one answer to “which channels does she accept”.',
  'customers.preferences.loading': 'how she wants to be reached',
  'customers.preferences.channels': 'Channels she accepts',
  'customers.preferences.channel.Sms': 'Text message',
  'customers.preferences.channel.WhatsApp': 'WhatsApp',
  'customers.preferences.channel.Email': 'Email',
  'customers.preferences.noChannels':
    'No channels chosen, which is how she says do not message me. It does not withdraw any consent.',
  'customers.preferences.quietHoursStart': 'Do not message after',
  'customers.preferences.quietHoursEnd': 'Message again from',
  'customers.preferences.quietHoursHint':
    'Branch time. It may run across midnight — 9 pm to 8 am is an ordinary quiet night.',
  'customers.preferences.quietHoursBothEnds': 'Give both ends of the quiet hours, or neither.',
  'customers.preferences.save': 'Save how to reach her',
  'customers.preferences.saved': 'Saved.',
  'customers.preferences.offlineAction': 'Saving how to reach her',
  'customers.layout.title': 'Customers',
  'customers.layout.list': 'Customer search and results',
  'customers.layout.detail': 'The customer record',

  /* The subject-access export. -------------------------------------------------------------- */
  'customers.detail.export': 'Export her data for a subject-access request',
  'customers.export.title': 'Export {name}’s data',
  'customers.export.body':
    'This is the copy that answers a subject-access request. Generating one replaces any earlier copy, so only one exists outside the record at a time.',
  'customers.export.contains': 'What the copy holds',
  'customers.export.contains.profile': 'Her record: name, contact details, branch and status',
  'customers.export.contains.consent': 'Every answer she has given about consent, and when',
  'customers.export.contains.preferences': 'How she has asked to be reached',
  'customers.export.excludes':
    'It does not hold images, duplicate scores or merge reasons, and it holds no measurements — the system does not record any against a customer yet. Say so if you are asked whether the copy is everything.',
  'customers.export.action': 'Generate the copy',
  'customers.export.again': 'Generate a fresh copy',
  'customers.export.offlineAction': 'Generating a subject-access export',
  'customers.export.offlineDownload': 'Downloading the export',
  'customers.export.confirm.title': 'Generate a copy of {name}’s data?',
  'customers.export.confirm.body':
    'Everything the shop holds about her is written to a file that can be downloaded until it expires. Any earlier copy stops working the moment this one is made, so a download you have already given somebody will stop working. The reason you give is kept against her record.',
  'customers.export.confirm.action': 'generating a copy of her data',
  'customers.export.confirm.label': 'Generate the copy',
  'customers.export.ready.title': 'The copy is ready',
  'customers.export.generatedAt': 'Made at',
  'customers.export.expiresAt': 'Stops working at',
  'customers.export.classification': 'Handling class',
  'customers.export.superseded':
    '{count, plural, one {# earlier copy} other {# earlier copies}} stopped working when this one was made.',
  'customers.export.download': 'Download the copy',
  'customers.export.gone.title': 'That copy has gone',
  'customers.export.gone.body':
    'It expired, or a newer copy replaced it. The record that it was made, by whom and why is kept — only the copy of the data is destroyed. Generate a fresh one to answer the request.',
  'customers.search.withdrawn': 'Include deactivated records',
  'customers.search.withdrawnHint':
    'Off for an ordinary search: a deactivated record is not one to start a new order against. Turn it on to find somebody who was deactivated, so you can reactivate them.',

  /* Withdrawing a record, and putting it back. ------------------------------------------------ */
  'customers.status.deactivate': 'Deactivate this record',
  'customers.status.reactivate': 'Reactivate this record',
  'customers.status.offlineDeactivate': 'Deactivating a customer record',
  'customers.status.offlineReactivate': 'Reactivating a customer record',
  'customers.status.withdrawn.title': 'This record has been deactivated',
  'customers.status.withdrawn.body':
    'It is still here and its history still stands — an order placed last year still names her. What changed is that an ordinary search no longer offers her, so nobody starts a new order against this record by accident.',
  'customers.status.merged':
    'This record was merged into another one, so it cannot be reactivated — a merge cannot be undone. Work on the record that survived.',
  'customers.status.confirm.deactivate.title': 'Deactivate {name}’s record?',
  'customers.status.confirm.deactivate.body':
    'She stops appearing in an ordinary search, so nobody starts a new order against this record. Nothing is deleted: the record stays readable, her history stands, and you can reactivate it. To find her afterwards, tick “Include deactivated records” when you search.',
  'customers.status.confirm.deactivate.action': 'deactivating this record',
  'customers.status.confirm.reactivate.title': 'Reactivate {name}’s record?',
  'customers.status.confirm.reactivate.body':
    'She appears in ordinary search results again and can be used for a new order.',
  'customers.status.confirm.reactivate.action': 'reactivating this record',
  'customers.status.conflict':
    'Somebody corrected this record while you were reading it, so this is not the record you looked at. Read it again and decide once more.',
  'customers.status.already':
    'Somebody else already did that. The record is where you wanted it, so there is nothing more to do.',
} as const

export const customersTa: Record<keyof typeof customersEn, string> = {
  // not translated — awaiting native-speaker review
  'customers.title': 'Customers',
  // not translated — awaiting native-speaker review
  'customers.body': 'Find a customer, or register somebody new.',
  // not translated — awaiting native-speaker review
  'customers.search.title': 'Find a customer',
  // not translated — awaiting native-speaker review
  'customers.search.body':
    'Search by name, customer number, or the last digits of the telephone number.',
  // not translated — awaiting native-speaker review
  'customers.search.label': 'Search',
  // not translated — awaiting native-speaker review
  'customers.search.hint': 'At least {minimum} characters.',
  // not translated — awaiting native-speaker review
  'customers.search.tooShort': 'Type at least {minimum} characters.',
  // not translated — awaiting native-speaker review
  'customers.search.action': 'Search',
  // not translated — awaiting native-speaker review
  'customers.search.searching': 'Searching…',
  // not translated — awaiting native-speaker review
  'customers.search.loading': 'the search results',
  // not translated — awaiting native-speaker review
  'customers.search.empty': 'Nobody matched. Check the spelling, or register a new customer.',
  // not translated — awaiting native-speaker review
  'customers.search.results': 'Matching customers',
  // not translated — awaiting native-speaker review
  'customers.search.more': 'More matched than are shown. Narrow the search to see the rest.',
  // not translated — awaiting native-speaker review
  'customers.search.card': '{name}, {number}, {phone}',
  // not translated — awaiting native-speaker review
  'customers.search.masked': '{name}, {number} — at another branch',
  // not translated — awaiting native-speaker review
  'customers.search.maskedHint':
    'This record was created at a branch you are not assigned to. Open it to add your branch to its visibility.',
  // not translated — awaiting native-speaker review
  'customers.search.createNew': 'Register a new customer',
  // not translated — awaiting native-speaker review
  'customers.search.createNewHint':
    "Only once you're sure this is somebody new — check the results above first.",
  // not translated — awaiting native-speaker review
  // not translated — awaiting native-speaker review
  'customers.detail.loading': 'the customer record',
  // not translated — awaiting native-speaker review
  'customers.detail.notFound': 'This record could not be found, or is not one you can reach.',
  // not translated — awaiting native-speaker review
  'customers.detail.number': 'Customer number {number}',
  // not translated — awaiting native-speaker review
  'customers.detail.notGiven': 'Not given',
  // not translated — awaiting native-speaker review
  'customers.detail.language': 'Written to in',
  // not translated — awaiting native-speaker review
  'customers.detail.contactWithheld':
    'Contact details are shown to holders of a permission you do not have.',
  // not translated — awaiting native-speaker review
  'customers.detail.aliases': 'Also known as',
  // not translated — awaiting native-speaker review
  'customers.create.title': 'Register a customer',
  // not translated — awaiting native-speaker review
  'customers.create.body': 'This creates a record at the branch you are working in.',
  // not translated — awaiting native-speaker review
  'customers.create.field.displayName': 'Name',
  // not translated — awaiting native-speaker review
  'customers.create.field.nativeName': 'Name (native script)',
  // not translated — awaiting native-speaker review
  'customers.create.field.phone': 'Telephone number',
  // not translated — awaiting native-speaker review
  'customers.create.field.alternatePhone': 'Second telephone number',
  // not translated — awaiting native-speaker review
  'customers.create.field.email': 'Email address',
  // not translated — awaiting native-speaker review
  'customers.create.field.addressLine': 'Address',
  // not translated — awaiting native-speaker review
  'customers.create.field.locality': 'Area or town',
  // not translated — awaiting native-speaker review
  'customers.create.field.postcode': 'PIN code',
  // not translated — awaiting native-speaker review
  'customers.create.field.language': 'Written to in',
  // not translated — awaiting native-speaker review
  'customers.create.field.language.en-IN': 'English',
  // not translated — awaiting native-speaker review
  'customers.create.field.language.ta-IN': 'Tamil',
  // not translated — awaiting native-speaker review
  'customers.create.action': 'Register',
  // not translated — awaiting native-speaker review
  'customers.create.saving': 'Registering…',
  // not translated — awaiting native-speaker review
  'customers.create.offlineAction': 'Registering a customer',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.title': 'This may be somebody we already know',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.body':
    'Read the records below before registering a new one — a second record for the same person is harder to undo than a moment spent checking.',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.open': 'Open this record instead',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.confidence.High': 'Strong match',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.confidence.Medium': 'Possible match',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.confidence.Low': 'Weak match',
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.reviewed': "I've checked — this is somebody new",
  // not translated — awaiting native-speaker review
  'customers.create.duplicates.reviewedHint':
    'This is kept on record as your decision that the two are different people.',
  // not translated — awaiting native-speaker review
  'customers.detail.correct': 'Correct this record',
  // not translated — awaiting native-speaker review
  'customers.edit.title': 'Correct a customer record',
  // not translated — awaiting native-speaker review
  'customers.edit.body':
    'Change what the record says about the person. A changed name is kept as an alias, so she is still found under the name on her old receipts.',
  // not translated — awaiting native-speaker review
  'customers.edit.loading': 'the customer record',
  // not translated — awaiting native-speaker review
  'customers.edit.back': 'Back to the record',
  // not translated — awaiting native-speaker review
  'customers.edit.reason': 'Why this correction',
  // not translated — awaiting native-speaker review
  'customers.edit.reasonHint': 'Recorded in the audit trail with your name. A sentence is enough.',
  // not translated — awaiting native-speaker review
  'customers.edit.action': 'Save the correction',
  // not translated — awaiting native-speaker review
  'customers.edit.saving': 'Saving…',
  // not translated — awaiting native-speaker review
  'customers.edit.saved': 'The correction was saved.',
  // not translated — awaiting native-speaker review
  'customers.edit.offlineAction': 'Correcting a customer record',
  // not translated — awaiting native-speaker review
  'customers.edit.nameRequired': 'A customer record must have a name.',
  // not translated — awaiting native-speaker review
  'customers.edit.reasonRequired': 'Say why this record is being corrected.',
  // not translated — awaiting native-speaker review
  'customers.edit.conflict.title': 'Somebody else changed this record',
  // not translated — awaiting native-speaker review
  'customers.edit.conflict.body':
    'It was corrected by somebody else while you were typing. Reload it to see their version — what you typed is kept, so you can check it against theirs before saving again.',
  // not translated — awaiting native-speaker review
  'customers.edit.conflict.reload': 'Reload the record',
  // not translated — awaiting native-speaker review
  'customers.edit.contactWithheld':
    'Correcting a record sends every field back, contact details included — and those are shown to holders of a permission you do not have. Ask somebody who holds it to make this correction.',
  // not translated — awaiting native-speaker review
  'customers.detail.tabs': 'Sections of this record',
  // not translated — awaiting native-speaker review
  'customers.detail.tab.record': 'Details',
  // not translated — awaiting native-speaker review
  'customers.detail.tab.history': 'History',
  // not translated — awaiting native-speaker review
  'customers.timeline.label': 'What has happened to this customer',
  // not translated — awaiting native-speaker review
  'customers.timeline.loading': 'this customer’s history',
  // not translated — awaiting native-speaker review
  'customers.timeline.empty': 'Nothing has been recorded against this customer yet.',
  // not translated — awaiting native-speaker review
  'customers.timeline.older': 'Show older',
  // not translated — awaiting native-speaker review
  'customers.timeline.loadingOlder': 'Loading…',
  // not translated — awaiting native-speaker review
  'customers.timeline.olderAdded':
    '{count, plural, one {# older entry added below} other {# older entries added below}}',
  // not translated — awaiting native-speaker review
  'customers.timeline.actor.system': 'the system',
  // not translated — awaiting native-speaker review
  'customers.timeline.reason': 'Reason: {reason}',
  // not translated — awaiting native-speaker review
  'customers.timeline.reasonWithheld':
    'A reason was given. Reading it needs a permission you do not have.',
  // not translated — awaiting native-speaker review
  'customers.timeline.partial.title': 'Part of this history could not be loaded',
  // not translated — awaiting native-speaker review
  'customers.timeline.partial.body':
    '{count, plural, one {# part of the record} other {# parts of the record}} did not answer ({sources}), so this history is incomplete. Do not read a gap below as nothing having happened.',
  // not translated — awaiting native-speaker review
  'customers.timeline.source.customers': 'customer records',
  // not translated — awaiting native-speaker review
  'customers.timeline.source.orders': 'orders',
  // not translated — awaiting native-speaker review
  'customers.timeline.source.billing': 'billing',
  // not translated — awaiting native-speaker review
  'customers.timeline.source.custody': 'garment tracking',
  // not translated — awaiting native-speaker review
  'customers.detail.duplicates': 'Check for duplicate records',
  // not translated — awaiting native-speaker review
  'customers.merge.title': 'Possible duplicates of {name}',
  // not translated — awaiting native-speaker review
  'customers.merge.body':
    '{name} ({number}) is the record that will survive. Anything you fold in here is merged into it and cannot be separated again.',
  // not translated — awaiting native-speaker review
  'customers.merge.loading': 'the possible duplicates',
  // not translated — awaiting native-speaker review
  'customers.merge.empty':
    'No other record resembles this one closely enough to be worth reviewing.',
  // not translated — awaiting native-speaker review
  'customers.merge.open': 'Open this record',
  // not translated — awaiting native-speaker review
  'customers.merge.action': 'Fold {number} into {name}',
  // not translated — awaiting native-speaker review
  'customers.merge.offlineAction': 'Merging two customer records',
  // not translated — awaiting native-speaker review
  'customers.merge.confirm.title': 'Fold {merged} into {survivor}?',
  // not translated — awaiting native-speaker review
  'customers.merge.confirm.body':
    '{merged} ({mergedNumber}) will stop being used. {survivor} ({survivorNumber}) carries on, keeps {mergedNumber} searchable as an alias, and becomes visible to every branch that could see {mergedNumber}. Measurements and orders move across; anything already printed on an invoice or a job card is left exactly as it was.',
  // not translated — awaiting native-speaker review
  'customers.merge.confirm.action': 'folding {number} into {name}',
  // not translated — awaiting native-speaker review
  'customers.merge.confirm.label': 'Fold {number} in',
  // not translated — awaiting native-speaker review
  'customers.merge.conflict.survivor':
    'The record that would survive has been corrected since you opened this screen. Close this, read it again, and decide once more — the pair you approved is not the pair that would be merged.',
  // not translated — awaiting native-speaker review
  'customers.merge.conflict.merged':
    'The record you are folding in has been corrected since you opened this screen. Close this and read it again before deciding — this is the record that would stop existing.',
  // not translated — awaiting native-speaker review
  'customers.merge.done.title': 'The records were merged',
  // not translated — awaiting native-speaker review
  'customers.merge.done.body': '{number} was folded into {survivor}.',
  // not translated — awaiting native-speaker review
  'customers.merge.done.aliases':
    '{count, plural, one {# alias recorded} other {# aliases recorded}} on the surviving record',
  // not translated — awaiting native-speaker review
  'customers.merge.done.repointed':
    '{count, plural, one {# record re-pointed} other {# records re-pointed}} to the surviving customer',
  // not translated — awaiting native-speaker review
  'customers.merge.done.branches':
    '{count, plural, one {# branch added} other {# branches added}} to what the surviving record is visible to',
  // not translated — awaiting native-speaker review
  'customers.detail.consent': 'Consent and how to reach her',
  // not translated — awaiting native-speaker review
  'customers.consent.title': 'Consent and communication',
  // not translated — awaiting native-speaker review
  'customers.consent.body':
    'What she has agreed to, and how she wants to be reached. An answer is added to the record rather than replacing the last one, so what she said before is still there.',
  // not translated — awaiting native-speaker review
  'customers.consent.loading': 'her consent record',
  // not translated — awaiting native-speaker review
  'customers.consent.empty': 'The shop asks about nothing that needs consent.',
  // not translated — awaiting native-speaker review
  'customers.consent.status.Granted': 'She agreed',
  // not translated — awaiting native-speaker review
  'customers.consent.status.Declined': 'She said no',
  // not translated — awaiting native-speaker review
  'customers.consent.status.Withdrawn': 'She withdrew',
  // not translated — awaiting native-speaker review
  'customers.consent.status.NeverAsked': 'Nobody has asked her',
  // not translated — awaiting native-speaker review
  'customers.consent.retired': 'This is no longer asked about, so no answer can be recorded.',
  // not translated — awaiting native-speaker review
  'customers.consent.noWording':
    'No wording has been published for this yet. An answer names the words she was read, and there are none to name.',
  // not translated — awaiting native-speaker review
  'customers.consent.answers': 'What she has said',
  // not translated — awaiting native-speaker review
  'customers.consent.answer': '{decision} — {when}, wording version {version} — {source}',
  // not translated — awaiting native-speaker review
  'customers.consent.source': 'Where she said it',
  // not translated — awaiting native-speaker review
  'customers.consent.sourceHint':
    'At the counter, over the telephone, on a signed form. Kept on the record exactly as you write it.',
  // not translated — awaiting native-speaker review
  'customers.consent.sourceRequired': 'Say where she said it.',
  // not translated — awaiting native-speaker review
  'customers.consent.grant': 'She agreed',
  // not translated — awaiting native-speaker review
  'customers.consent.decline': 'She said no',
  // not translated — awaiting native-speaker review
  'customers.consent.withdraw': 'She withdrew it',
  // not translated — awaiting native-speaker review
  'customers.consent.offlineAction': 'Recording what she said',
  // not translated — awaiting native-speaker review
  'customers.consent.recorded': 'Recorded: {decision}.',
  // not translated — awaiting native-speaker review
  'customers.preferences.title': 'How to reach her',
  // not translated — awaiting native-speaker review
  'customers.preferences.body':
    'This replaces what was there, so it always reads as one answer to “which channels does she accept”.',
  // not translated — awaiting native-speaker review
  'customers.preferences.loading': 'how she wants to be reached',
  // not translated — awaiting native-speaker review
  'customers.preferences.channels': 'Channels she accepts',
  // not translated — awaiting native-speaker review
  'customers.preferences.channel.Sms': 'Text message',
  // not translated — awaiting native-speaker review
  'customers.preferences.channel.WhatsApp': 'WhatsApp',
  // not translated — awaiting native-speaker review
  'customers.preferences.channel.Email': 'Email',
  // not translated — awaiting native-speaker review
  'customers.preferences.noChannels':
    'No channels chosen, which is how she says do not message me. It does not withdraw any consent.',
  // not translated — awaiting native-speaker review
  'customers.preferences.quietHoursStart': 'Do not message after',
  // not translated — awaiting native-speaker review
  'customers.preferences.quietHoursEnd': 'Message again from',
  // not translated — awaiting native-speaker review
  'customers.preferences.quietHoursHint':
    'Branch time. It may run across midnight — 9 pm to 8 am is an ordinary quiet night.',
  // not translated — awaiting native-speaker review
  'customers.preferences.quietHoursBothEnds': 'Give both ends of the quiet hours, or neither.',
  // not translated — awaiting native-speaker review
  'customers.preferences.save': 'Save how to reach her',
  // not translated — awaiting native-speaker review
  'customers.preferences.saved': 'Saved.',
  // not translated — awaiting native-speaker review
  'customers.preferences.offlineAction': 'Saving how to reach her',
  // not translated — awaiting native-speaker review
  // not translated — awaiting native-speaker review
  'customers.layout.title': 'Customers',
  'customers.layout.list': 'Customer search and results',
  // not translated — awaiting native-speaker review
  'customers.layout.detail': 'The customer record',
  // not translated — awaiting native-speaker review
  'customers.detail.export': 'Export her data for a subject-access request',
  // not translated — awaiting native-speaker review
  'customers.export.title': 'Export {name}’s data',
  // not translated — awaiting native-speaker review
  'customers.export.body':
    'This is the copy that answers a subject-access request. Generating one replaces any earlier copy, so only one exists outside the record at a time.',
  // not translated — awaiting native-speaker review
  'customers.export.contains': 'What the copy holds',
  // not translated — awaiting native-speaker review
  'customers.export.contains.profile': 'Her record: name, contact details, branch and status',
  // not translated — awaiting native-speaker review
  'customers.export.contains.consent': 'Every answer she has given about consent, and when',
  // not translated — awaiting native-speaker review
  'customers.export.contains.preferences': 'How she has asked to be reached',
  // not translated — awaiting native-speaker review
  'customers.export.excludes':
    'It does not hold images, duplicate scores or merge reasons, and it holds no measurements — the system does not record any against a customer yet. Say so if you are asked whether the copy is everything.',
  // not translated — awaiting native-speaker review
  'customers.export.action': 'Generate the copy',
  // not translated — awaiting native-speaker review
  'customers.export.again': 'Generate a fresh copy',
  // not translated — awaiting native-speaker review
  'customers.export.offlineAction': 'Generating a subject-access export',
  // not translated — awaiting native-speaker review
  'customers.export.offlineDownload': 'Downloading the export',
  // not translated — awaiting native-speaker review
  'customers.export.confirm.title': 'Generate a copy of {name}’s data?',
  // not translated — awaiting native-speaker review
  'customers.export.confirm.body':
    'Everything the shop holds about her is written to a file that can be downloaded until it expires. Any earlier copy stops working the moment this one is made, so a download you have already given somebody will stop working. The reason you give is kept against her record.',
  // not translated — awaiting native-speaker review
  'customers.export.confirm.action': 'generating a copy of her data',
  // not translated — awaiting native-speaker review
  'customers.export.confirm.label': 'Generate the copy',
  // not translated — awaiting native-speaker review
  'customers.export.ready.title': 'The copy is ready',
  // not translated — awaiting native-speaker review
  'customers.export.generatedAt': 'Made at',
  // not translated — awaiting native-speaker review
  'customers.export.expiresAt': 'Stops working at',
  // not translated — awaiting native-speaker review
  'customers.export.classification': 'Handling class',
  // not translated — awaiting native-speaker review
  'customers.export.superseded':
    '{count, plural, one {# earlier copy} other {# earlier copies}} stopped working when this one was made.',
  // not translated — awaiting native-speaker review
  'customers.export.download': 'Download the copy',
  // not translated — awaiting native-speaker review
  'customers.export.gone.title': 'That copy has gone',
  // not translated — awaiting native-speaker review
  'customers.export.gone.body':
    'It expired, or a newer copy replaced it. The record that it was made, by whom and why is kept — only the copy of the data is destroyed. Generate a fresh one to answer the request.',
  // not translated — awaiting native-speaker review
  'customers.search.withdrawn': 'Include deactivated records',
  // not translated — awaiting native-speaker review
  'customers.search.withdrawnHint':
    'Off for an ordinary search: a deactivated record is not one to start a new order against. Turn it on to find somebody who was deactivated, so you can reactivate them.',
  // not translated — awaiting native-speaker review
  'customers.status.deactivate': 'Deactivate this record',
  // not translated — awaiting native-speaker review
  'customers.status.reactivate': 'Reactivate this record',
  // not translated — awaiting native-speaker review
  'customers.status.offlineDeactivate': 'Deactivating a customer record',
  // not translated — awaiting native-speaker review
  'customers.status.offlineReactivate': 'Reactivating a customer record',
  // not translated — awaiting native-speaker review
  'customers.status.withdrawn.title': 'This record has been deactivated',
  // not translated — awaiting native-speaker review
  'customers.status.withdrawn.body':
    'It is still here and its history still stands — an order placed last year still names her. What changed is that an ordinary search no longer offers her, so nobody starts a new order against this record by accident.',
  // not translated — awaiting native-speaker review
  'customers.status.merged':
    'This record was merged into another one, so it cannot be reactivated — a merge cannot be undone. Work on the record that survived.',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.deactivate.title': 'Deactivate {name}’s record?',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.deactivate.body':
    'She stops appearing in an ordinary search, so nobody starts a new order against this record. Nothing is deleted: the record stays readable, her history stands, and you can reactivate it. To find her afterwards, tick “Include deactivated records” when you search.',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.deactivate.action': 'deactivating this record',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.reactivate.title': 'Reactivate {name}’s record?',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.reactivate.body':
    'She appears in ordinary search results again and can be used for a new order.',
  // not translated — awaiting native-speaker review
  'customers.status.confirm.reactivate.action': 'reactivating this record',
  // not translated — awaiting native-speaker review
  'customers.status.conflict':
    'Somebody corrected this record while you were reading it, so this is not the record you looked at. Read it again and decide once more.',
  // not translated — awaiting native-speaker review
  'customers.status.already':
    'Somebody else already did that. The record is where you wanted it, so there is nothing more to do.',
}
