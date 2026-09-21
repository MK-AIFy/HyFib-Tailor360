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
  'customers.detail.back': 'Back to customers',
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
  'customers.detail.back': 'Back to customers',
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
}
