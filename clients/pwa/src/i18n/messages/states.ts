/**
 * Screen-state messages — the network, permission, empty, loading and error states.
 *
 * These are the words of the five states DoD item 7 requires a story for, plus the two network
 * states of plan Section 4.6. They are the family most likely to be read under pressure: a Cashier
 * who has just been told the payment did not go through is not in a mood to interpret "error 503".
 *
 * Three rules shape the English here, all from docs/nfr/accessibility-localisation.md:
 *
 *  - **Say what happened and what to do**, never a code and never a stack (section 8.2). Every
 *    sentence below is one a person can act on.
 *  - **Say when something will not be queued.** Section 6 fixes the exact promise for a blocked
 *    action, and `states.blocked.title` carries it verbatim: money is never accepted into a queue,
 *    and the person must know that immediately (checklist item A11Y-OF-02).
 *  - **Say what has been kept.** Typed input is never discarded (section 8.3), and a person who has
 *    just seen a failure cannot tell that from the screen alone, so the state says it.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows.
 */
export const statesEn = {
  /* Connection ---------------------------------------------------------------------------- */
  'states.network.label': 'Connection status',
  'states.network.offline.title': 'No connection',
  'states.network.offline.body':
    'You are working offline. Looking things up and scanning carry on. Taking payment, posting an invoice and moving stock need a connection.',
  'states.network.restored.title': 'Connection returned',
  'states.network.restored.body': 'You are back online. Anything that was blocked can be done now.',
  'states.network.restored.dismiss': 'Hide this message',

  /* An action that needs a connection and will not be queued -------------------------------- */
  'states.blocked.title': 'Needs connection — this will not be queued',
  'states.blocked.body':
    '{action} needs a connection. It has not been sent, and it will not be sent later.',
  'states.blocked.inputKept': 'Everything you have typed is still on the screen.',
  'states.blocked.retry': 'Try again',

  /* A request that failed and can safely be repeated ---------------------------------------- */
  'states.retryable.title': '{action} did not go through',
  'states.retryable.retry': 'Try again',
  'states.retryable.retrying': 'Trying again…',
  'states.retryable.safe':
    'Trying again sends the same request, so this cannot end up happening twice.',
  'states.retryable.inputKept': 'Nothing you have typed has been lost.',
  'states.retryable.reference': 'If you ask for help, quote this reference: {correlationId}',

  /* The plain language a failure is described in. One per failure cause. --------------------- */
  'states.problem.network': 'The connection dropped before the shop system answered.',
  'states.problem.timeout': 'The shop system took too long to answer.',
  'states.problem.rateLimited': 'Too many requests were sent at once. Wait a moment, then try again.',
  'states.problem.server': 'The shop system could not finish this. It is not something you did wrong.',
  'states.problem.conflict':
    'Somebody else changed this while you were working on it. Look at it again before you try.',
  'states.problem.notFound': 'This could not be found. It may have been changed or removed.',
  'states.problem.unknown': 'This did not go through, and the reason is not clear.',

  /* Permission ---------------------------------------------------------------------------- */
  'states.forbidden.title': 'You do not have permission to do this',
  'states.forbidden.body': '{action} is not part of what your role can do.',
  'states.forbidden.whoCan': 'This is done by: {roles}.',
  'states.forbidden.askManager': 'Ask your branch manager who can do this.',
  'states.forbidden.back': 'Go back',

  /* Empty, loading and error ---------------------------------------------------------------- */
  'states.empty.title': 'Nothing here yet',
  'states.empty.body': 'There is nothing to show. Nothing has gone wrong.',
  'states.loading.label': 'Loading {what}…',
  'states.error.title': 'This could not be shown',
  'states.error.body':
    'Something went wrong while this was being loaded. Try again, and tell your shop administrator if it keeps happening.',
  'states.error.retry': 'Try again',
} as const

export const statesTa: Record<keyof typeof statesEn, string> = {
  'states.network.label': 'இணைப்பு நிலை',
  'states.network.offline.title': 'இணைப்பு இல்லை',
  // not translated — awaiting native-speaker review
  'states.network.offline.body':
    'You are working offline. Looking things up and scanning carry on. Taking payment, posting an invoice and moving stock need a connection.',
  'states.network.restored.title': 'இணைப்பு திரும்பியது',
  // not translated — awaiting native-speaker review
  'states.network.restored.body': 'You are back online. Anything that was blocked can be done now.',
  'states.network.restored.dismiss': 'இந்தச் செய்தியை மறை',

  // not translated — awaiting native-speaker review
  'states.blocked.title': 'Needs connection — this will not be queued',
  // not translated — awaiting native-speaker review
  'states.blocked.body':
    '{action} needs a connection. It has not been sent, and it will not be sent later.',
  // not translated — awaiting native-speaker review
  'states.blocked.inputKept': 'Everything you have typed is still on the screen.',
  'states.blocked.retry': 'மீண்டும் முயற்சிக்கவும்',

  // not translated — awaiting native-speaker review
  'states.retryable.title': '{action} did not go through',
  'states.retryable.retry': 'மீண்டும் முயற்சிக்கவும்',
  'states.retryable.retrying': 'மீண்டும் முயற்சிக்கிறது…',
  // not translated — awaiting native-speaker review
  'states.retryable.safe':
    'Trying again sends the same request, so this cannot end up happening twice.',
  // not translated — awaiting native-speaker review
  'states.retryable.inputKept': 'Nothing you have typed has been lost.',
  // not translated — awaiting native-speaker review
  'states.retryable.reference': 'If you ask for help, quote this reference: {correlationId}',

  // not translated — awaiting native-speaker review
  'states.problem.network': 'The connection dropped before the shop system answered.',
  // not translated — awaiting native-speaker review
  'states.problem.timeout': 'The shop system took too long to answer.',
  // not translated — awaiting native-speaker review
  'states.problem.rateLimited': 'Too many requests were sent at once. Wait a moment, then try again.',
  // not translated — awaiting native-speaker review
  'states.problem.server': 'The shop system could not finish this. It is not something you did wrong.',
  // not translated — awaiting native-speaker review
  'states.problem.conflict':
    'Somebody else changed this while you were working on it. Look at it again before you try.',
  // not translated — awaiting native-speaker review
  'states.problem.notFound': 'This could not be found. It may have been changed or removed.',
  // not translated — awaiting native-speaker review
  'states.problem.unknown': 'This did not go through, and the reason is not clear.',

  'states.forbidden.title': 'இதைச் செய்ய உங்களுக்கு அனுமதி இல்லை',
  // not translated — awaiting native-speaker review
  'states.forbidden.body': '{action} is not part of what your role can do.',
  // not translated — awaiting native-speaker review
  'states.forbidden.whoCan': 'This is done by: {roles}.',
  // not translated — awaiting native-speaker review
  'states.forbidden.askManager': 'Ask your branch manager who can do this.',
  'states.forbidden.back': 'திரும்பிச் செல்',

  'states.empty.title': 'இங்கே இன்னும் எதுவும் இல்லை',
  // not translated — awaiting native-speaker review
  'states.empty.body': 'There is nothing to show. Nothing has gone wrong.',
  'states.loading.label': '{what} ஏற்றப்படுகிறது…',
  // not translated — awaiting native-speaker review
  'states.error.title': 'This could not be shown',
  // not translated — awaiting native-speaker review
  'states.error.body':
    'Something went wrong while this was being loaded. Try again, and tell your shop administrator if it keeps happening.',
  'states.error.retry': 'மீண்டும் முயற்சிக்கவும்',
}
