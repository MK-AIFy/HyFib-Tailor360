/**
 * Authentication messages — signing in, second factors, recovery, devices and re-authentication.
 *
 * See the top of `messages/shell.ts` for the rules every family file follows. Three of them bind
 * harder here than anywhere else in the application:
 *
 *  - **Never name what went wrong.** "The sign-in details supplied are not valid" is the whole answer
 *    to a wrong password, an unknown account, a suspended account and a locked-out one. The server
 *    already answers all four identically; a client that offered "no such account" would put the
 *    enumeration oracle back that the server went to some trouble to remove.
 *  - **Never show a secret twice.** The authenticator key and the recovery codes appear on screen
 *    once, in the words below, and nowhere else — no log, no telemetry, no storage, no address bar.
 *  - **Say what is safe.** A person whose session has just ended mid-measurement needs to be told, in
 *    the first sentence, that nothing they typed has been lost. It is the difference between signing
 *    back in and starting again.
 *
 * ## Why every string below is English in the Tamil catalogue
 *
 * This family is deliberately untranslated pending native-speaker review, and the reason is the same
 * one recorded for the identity email templates: a mistranslated security instruction is a security
 * defect, not a cosmetic one. "Do not share this code with anybody who asks for it" has to survive
 * translation exactly, and a plausible-looking approximation is worse than English text a person can
 * ask a colleague about. Every line therefore carries the marker the enablement-gate report counts,
 * and the Tamil catalogue becomes selectable only when this family has been reviewed with the rest.
 */
export const authEn = {
  /* Failures ------------------------------------------------------------------------------- */
  'auth.problem.invalidCredentials':
    'Those sign-in details are not right. Check them and try again.',
  'auth.problem.tooManyAttempts':
    'Too many attempts have been made. Wait about {seconds, plural, one {# second} other {# seconds}} and try again.',
  'auth.problem.captchaRequired':
    'Complete the check that confirms you are a person, then try again.',
  'auth.problem.codeInvalid': 'That code is not right. Wait for the next one and try again.',
  'auth.problem.recoveryTokenInvalid':
    'That link cannot be used. It may have been used already, or it may have expired. Ask for a new one.',
  'auth.problem.sessionRequired': 'You need to sign in again before doing this.',
  'auth.problem.passkeysUnavailable':
    'Passkeys are not switched on for this shop yet. Use your password instead.',
  'auth.problem.passkeyRefused':
    'That passkey was not accepted. Try again, or sign in with your password.',
  'auth.problem.securityRefused':
    'The shop system refused that request for safety. Reload the page and try again.',
  'auth.problem.mfaAlreadyEnrolled': 'An authenticator is already set up on this account.',
  'auth.problem.secondFactorRequired':
    'Confirm it is you with your authenticator or a recovery code before changing how you sign in.',
  'auth.problem.signInIncomplete': 'Finish signing in first, then try that again.',
  'auth.problem.lastFactor':
    'This is the only way you can confirm it is you, so it cannot be removed. Add another one first.',

  /* What this application checks before it asks the server ---------------------------------- */
  'auth.validation.identifierRequired': 'Type your sign-in name or email address.',
  'auth.validation.passwordRequired': 'Type your password.',
  'auth.validation.codeRequired': 'Type the code.',
  'auth.validation.emailRequired': 'Type the email address on your account.',
  'auth.validation.newPasswordRequired': 'Choose a new password.',
  'auth.validation.repeatRequired': 'Type the new password again.',

  /* Signing in ----------------------------------------------------------------------------- */
  'auth.signIn.title': 'Sign in',
  'auth.signIn.intro': 'Sign in with the details your shop administrator gave you.',
  'auth.signIn.identifier.label': 'Sign-in name or email address',
  'auth.signIn.identifier.description': 'Whichever your shop administrator set up for you.',
  'auth.signIn.password.label': 'Password',
  'auth.signIn.password.description':
    'Never share it, and never type it while somebody is watching.',
  'auth.signIn.submit': 'Sign in',
  'auth.signIn.failed': 'Signing in',
  'auth.signIn.forgot': 'I have forgotten my password',
  'auth.signIn.passkey': 'Sign in with a passkey',
  'auth.signIn.passkey.description':
    'If you have set up a passkey, your device can confirm it is you with a fingerprint, a face or a device code.',
  'auth.signIn.passkey.unsupported':
    'This browser cannot use passkeys. Sign in with your password.',
  'auth.signIn.passkey.cancelled': 'That was cancelled. Nothing has changed.',
  'auth.signIn.captcha.label': 'Human check',
  'auth.signIn.captcha.description':
    'Paste the response from the check shown by your browser. Your administrator can explain this.',
  'auth.signIn.signedOut': 'You have been signed out.',
  'auth.signIn.otherSection': 'Other ways to sign in',

  /* The second factor ---------------------------------------------------------------------- */
  'auth.challenge.title': 'Confirm it is you',
  'auth.challenge.intro':
    'Your password is right. One more step confirms that it is you and not somebody who has learned it.',
  'auth.challenge.action': 'Confirming it is you',
  'auth.challenge.factor.legend': 'How would you like to confirm?',
  'auth.challenge.factor.totp': 'A code from my authenticator app',
  'auth.challenge.factor.recoveryCode': 'One of my printed recovery codes',
  'auth.challenge.code.label.totp': 'Code from your authenticator',
  'auth.challenge.code.description.totp':
    'Six digits. It changes every 30 seconds, so use the one showing now.',
  'auth.challenge.code.label.recoveryCode': 'Recovery code',
  'auth.challenge.code.description.recoveryCode':
    'One of the codes printed when you set up your authenticator. Each one works once.',
  'auth.challenge.remember.label': 'Remember this device',
  'auth.challenge.remember.description':
    'Only on a device that is yours. Never on a shared counter or workshop device.',
  'auth.challenge.submit': 'Confirm',
  'auth.challenge.remaining':
    '{count, plural, =0 {No recovery codes left} one {# recovery code left} other {# recovery codes left}}',
  'auth.challenge.reissueSoon':
    'You are running low on recovery codes. Print a new sheet from Sign-in and security.',
  'auth.challenge.none.title': 'There is no way to confirm it is you',
  'auth.challenge.none.body':
    'This account needs a second step, and nothing is set up to provide one. Ask your shop administrator to reset it for you.',
  'auth.challenge.passkey': 'Use a passkey instead',

  /* Setting up an authenticator ------------------------------------------------------------- */
  'auth.enrol.title': 'Set up your authenticator',
  'auth.enrol.action': 'Setting up your authenticator',
  'auth.enrol.intro':
    'An authenticator app on your phone shows a new code every 30 seconds. You will be asked for one when you sign in.',
  'auth.enrol.required.title': 'This account needs a second step',
  'auth.enrol.required.body':
    'Set up an authenticator app before you carry on. It takes about a minute, and you only do it once.',
  'auth.enrol.begin': 'Start setting up',
  'auth.enrol.secret.warning':
    'What is on this screen is a key to your account. Anybody who photographs it can sign in as you, so keep the screen to yourself until you have finished.',
  'auth.enrol.scan.title': 'Point your authenticator app at this',
  'auth.enrol.qr.alt':
    'A QR code that adds {account} at {issuer} to an authenticator app. The setup key below says the same thing in words.',
  'auth.enrol.open': 'Open in my authenticator app',
  'auth.enrol.open.description':
    'Use this when the authenticator app is on the same phone as this screen — a phone cannot photograph itself.',
  'auth.enrol.manual.title': 'Or type it in by hand',
  'auth.enrol.manual.label': 'Setup key',
  'auth.enrol.manual.description':
    'Groups of four, in any order of upper or lower case. The app will ask for {digits} digits and change them every {seconds} seconds.',
  'auth.enrol.copy': 'Copy the setup key',
  'auth.enrol.copied': 'Setup key copied.',
  'auth.enrol.copyFailed': 'This browser would not copy it. Type the key in instead.',
  'auth.enrol.confirm.title': 'Now type the code it shows',
  'auth.enrol.confirm.intro':
    'This proves the app and this shop system agree, before your password stops being enough on its own.',
  'auth.enrol.code.label': 'Code from your authenticator',
  'auth.enrol.code.description': 'Six digits, as they appear in the app right now.',
  'auth.enrol.confirm.submit': 'Confirm the authenticator',
  'auth.enrol.confirmed': 'Your authenticator is set up.',
  'auth.enrol.restart': 'Start again with a new key',

  /* Recovery codes -------------------------------------------------------------------------- */
  'auth.codes.title': 'Your recovery codes',
  'auth.codes.intro':
    'These get you back in if you lose your phone. Print them, or write them down, and keep them where you keep the shop keys.',
  'auth.codes.once':
    'This is the only time these are shown. Nobody — not your administrator, not this shop system — can show them to you again. Printing a new sheet destroys this one.',
  'auth.codes.list.label': 'Recovery codes',
  'auth.codes.print': 'Print these codes',
  'auth.codes.copy': 'Copy the codes',
  'auth.codes.copied': 'Recovery codes copied.',
  'auth.codes.acknowledge': 'I have written these down or printed them',
  'auth.codes.continue': 'Finish',
  'auth.codes.acknowledge.missing': 'Confirm that you have kept the codes before you finish.',
  'auth.codes.reissue': 'Print a new sheet of recovery codes',
  'auth.codes.reissue.action': 'Printing a new sheet of recovery codes',
  'auth.codes.reissue.title': 'Print a new sheet of recovery codes?',
  'auth.codes.reissue.body':
    'The codes you have now stop working straight away, whether or not you have used them. You will be shown the new ones once.',
  'auth.codes.reissue.confirm': 'Print a new sheet',

  /* Passkeys -------------------------------------------------------------------------------- */
  'auth.passkeys.title': 'Passkeys',
  'auth.passkeys.intro':
    'A passkey lets this device confirm it is you with a fingerprint, a face or a device code. It replaces both your password and the code from your authenticator.',
  'auth.passkeys.unsupported':
    'This browser cannot use passkeys. Your password and authenticator still work everywhere.',
  'auth.passkeys.action': 'Adding a passkey',
  'auth.passkeys.add': 'Add a passkey on this device',
  'auth.passkeys.label.label': 'What should this passkey be called?',
  'auth.passkeys.label.description':
    'Something you will recognise in a list — "Counter tablet", "My phone".',
  'auth.passkeys.label.missing': 'Give the passkey a name you will recognise.',
  'auth.passkeys.save': 'Add this passkey',
  'auth.passkeys.cancel': 'Cancel',
  'auth.passkeys.added': '{label} has been added.',
  'auth.passkeys.removed': '{label} has been removed.',
  'auth.passkeys.cancelled': 'That was cancelled. Nothing has been added.',
  'auth.passkeys.empty.title': 'No passkeys yet',
  'auth.passkeys.empty.body':
    'Adding one on a device that is yours makes signing in a single step. Do not add one on a shared device.',
  'auth.passkeys.list.label': 'Your passkeys',
  'auth.passkeys.added.on': 'Added {date}',
  'auth.passkeys.lastUsed': 'Last used {date}',
  'auth.passkeys.neverUsed': 'Not used yet',
  'auth.passkeys.backedUp': 'Also on your other devices',
  'auth.passkeys.deviceOnly': 'On this device only',
  'auth.passkeys.remove': 'Remove',
  'auth.passkeys.remove.action': 'removing {label}',
  'auth.passkeys.remove.title': 'Remove {label}?',
  'auth.passkeys.remove.body':
    'That device will no longer be able to sign in with a passkey. Your password and authenticator are unaffected.',
  'auth.passkeys.remove.confirm': 'Remove this passkey',

  /* Sign-in and security hub ---------------------------------------------------------------- */
  'auth.security.title': 'Sign-in and security',
  'auth.security.intro': 'How you prove it is you, and where your account is signed in.',
  'auth.security.authenticator.title': 'Authenticator app',
  'auth.security.authenticator.Enrolled': 'Set up and working.',
  'auth.security.authenticator.NotEnrolled': 'Not set up yet.',
  'auth.security.authenticator.PendingConfirmation':
    'Started but never finished. Set it up again to complete it.',
  'auth.security.authenticator.ResetRequired':
    'Reset by an administrator. Set it up again before you next sign in.',
  'auth.security.authenticator.setUp': 'Set up an authenticator',
  'auth.security.authenticator.replace': 'Set up a different authenticator',
  'auth.security.codes.title': 'Recovery codes',
  'auth.security.codes.count':
    '{count, plural, =0 {No unused codes left} one {# unused code left} other {# unused codes left}}',
  'auth.security.codes.none': 'You will get a sheet of codes when you set up an authenticator.',
  'auth.security.sessions.title': 'Devices',
  'auth.security.sessions.body': 'See where your account is signed in, and sign a device out.',
  'auth.security.sessions.link': 'Devices you are signed in on',
  'auth.security.signOut.title': 'Signing out',
  'auth.security.signOut.body':
    'Ends this session on this device only. Your other devices stay signed in.',
  'auth.security.signOut': 'Sign out of this device',
  'auth.security.mustChangePassword.title': 'You need to change your password',
  'auth.security.mustChangePassword.body':
    'Your administrator has asked for a new password. Changing it from here arrives shortly; until then, ask them to reset it for you.',

  /* Recovery -------------------------------------------------------------------------------- */
  'auth.recovery.request.title': 'Reset your password',
  'auth.recovery.request.action': 'Asking for a reset link',
  'auth.recovery.request.intro':
    'Type the email address on your account. If it is one we know, a link to set a new password is on its way.',
  'auth.recovery.request.email.label': 'Email address',
  'auth.recovery.request.email.description':
    'The address your shop administrator set the account up with.',
  'auth.recovery.request.submit': 'Send me a link',
  'auth.recovery.request.sent.title': 'Check your email',
  'auth.recovery.request.sent.body':
    'If that address belongs to an account, a link is on its way. It can be used once and expires shortly, so look for it now.',
  'auth.recovery.request.sent.note':
    'We say the same thing whether or not the address is one we know, so that nobody can use this screen to find out who has an account.',
  'auth.recovery.backToSignIn': 'Back to signing in',

  'auth.recovery.confirm.title': 'Choose a new password',
  'auth.recovery.confirm.action': 'Setting your new password',
  'auth.recovery.confirm.intro':
    'Pick something long that you have not used anywhere else. A short phrase of unrelated words is easier to remember and harder to guess than a short jumble.',
  'auth.recovery.confirm.password.label': 'New password',
  'auth.recovery.confirm.password.description':
    'At least 12 characters. It must not contain your name, your sign-in name or your email address.',
  'auth.recovery.confirm.repeat.label': 'Type the new password again',
  'auth.recovery.confirm.repeat.description': 'So that a typing mistake does not lock you out.',
  'auth.recovery.confirm.mismatch': 'The two passwords are not the same.',
  'auth.recovery.confirm.submit': 'Set the new password',
  'auth.recovery.confirm.missing.title': 'That link is not complete',
  'auth.recovery.confirm.missing.body':
    'Open the link from the email again, or ask for a new one. Copying it by hand often loses the end of it.',
  'auth.recovery.confirm.done.title': 'Your password is set',
  'auth.recovery.confirm.done.body':
    '{count, plural, =0 {No other devices were signed in.} one {# device was signed out.} other {# devices were signed out.}} Sign in with the new password.',
  'auth.recovery.confirm.done.stillMfa':
    'You will still be asked for a code from your authenticator, as usual.',

  /* Devices --------------------------------------------------------------------------------- */
  'auth.sessions.title': 'Devices you are signed in on',
  'auth.sessions.intro':
    'If you do not recognise something here, sign it out and change your password. Only you can see this list.',
  'auth.sessions.loading': 'the devices you are signed in on',
  'auth.sessions.action': 'Loading your devices',
  'auth.sessions.list.label': 'Devices signed in',
  'auth.sessions.current': 'This device',
  'auth.sessions.mfaSatisfied': 'Confirmed with a second step',
  'auth.sessions.passwordOnly': 'Password only',
  'auth.sessions.startedAt': 'Signed in {date}',
  'auth.sessions.lastSeen': 'Last used {date}',
  'auth.sessions.endsAt': 'Ends {date}',
  'auth.sessions.address': 'From {address}',
  'auth.sessions.address.unknown': 'Address not recorded',
  'auth.sessions.revoke': 'Sign this device out',
  'auth.sessions.revoke.action': 'signing {device} out',
  'auth.sessions.revoke.title': 'Sign {device} out?',
  'auth.sessions.revoke.body':
    'Whoever is using it will have to sign in again. Anything they have typed and not saved is lost.',
  'auth.sessions.revoke.bodyCurrent':
    'This is the device you are using. You will be signed out and taken back to the sign-in screen.',
  'auth.sessions.revoke.confirm': 'Sign it out',
  'auth.sessions.revoked': '{device} has been signed out.',
  'auth.sessions.everywhere': 'Sign out everywhere',
  'auth.sessions.everywhere.action': 'signing out everywhere',
  'auth.sessions.everywhere.title': 'Sign out of every device?',
  'auth.sessions.everywhere.body':
    'Every device is signed out, including this one, and every remembered device is forgotten. Use this if you think somebody knows your password.',
  'auth.sessions.everywhere.confirm': 'Sign out everywhere',
  'auth.sessions.empty.title': 'Only this device',
  'auth.sessions.empty.body': 'Your account is not signed in anywhere else.',

  /* The session ending, and getting back in ------------------------------------------------- */
  'auth.expiry.title': 'You will be signed out soon',
  'auth.expiry.body':
    'This device has been idle. Nothing you have typed will be lost — choosing to carry on keeps you exactly where you are.',
  'auth.expiry.countdown': '{seconds, plural, one {# second} other {# seconds}} left',
  'auth.expiry.announce':
    'You will be signed out in about {seconds, plural, one {# second} other {# seconds}}. Choose carry on working to stay signed in.',
  'auth.expiry.keepWorking': 'Carry on working',
  'auth.expiry.signOutNow': 'Sign out now',

  'auth.reauth.title.expired': 'Your session ended',
  'auth.reauth.title.revoked': 'You were signed out',
  'auth.reauth.title.stepUp': 'Confirm it is you',
  'auth.reauth.body.expired':
    'Nothing you have typed has been lost. Sign back in and you will carry on exactly where you were.',
  'auth.reauth.body.revoked':
    'This account was signed out from somewhere else. Nothing you have typed has been lost. If you did not do that, sign back in and change your password.',
  'auth.reauth.body.stepUp':
    'Before {action}, type your password again. It has been a while since you last did.',
  'auth.reauth.as': 'Signed in as {name}',
  'auth.reauth.password.label': 'Password',
  'auth.reauth.submit': 'Sign back in',
  'auth.reauth.submit.stepUp': 'Confirm',
  'auth.reauth.abandon': 'Not now',
  'auth.reauth.action': 'continuing with this action',
  'auth.reauth.done': 'Signed back in. Carrying on where you left off.',
  'auth.reauth.code.title': 'One more step',
  'auth.reauth.code.body': 'Type the code from your authenticator to finish signing back in.',

  /* The guard ------------------------------------------------------------------------------- */
  'auth.guard.loading': 'your account',
  'auth.guard.unavailable.title': 'Your account could not be loaded',
  'auth.guard.unavailable.action': 'Loading your account',
  'auth.guard.signIn': 'Go to signing in',
} as const

export const authTa: Record<keyof typeof authEn, string> = {
  // not translated — awaiting native-speaker review
  'auth.problem.invalidCredentials':
    'Those sign-in details are not right. Check them and try again.',
  // not translated — awaiting native-speaker review
  'auth.problem.tooManyAttempts':
    'Too many attempts have been made. Wait about {seconds, plural, one {# second} other {# seconds}} and try again.',
  // not translated — awaiting native-speaker review
  'auth.problem.captchaRequired':
    'Complete the check that confirms you are a person, then try again.',
  // not translated — awaiting native-speaker review
  'auth.problem.codeInvalid': 'That code is not right. Wait for the next one and try again.',
  // not translated — awaiting native-speaker review
  'auth.problem.recoveryTokenInvalid':
    'That link cannot be used. It may have been used already, or it may have expired. Ask for a new one.',
  // not translated — awaiting native-speaker review
  'auth.problem.sessionRequired': 'You need to sign in again before doing this.',
  // not translated — awaiting native-speaker review
  'auth.problem.passkeysUnavailable':
    'Passkeys are not switched on for this shop yet. Use your password instead.',
  // not translated — awaiting native-speaker review
  'auth.problem.passkeyRefused':
    'That passkey was not accepted. Try again, or sign in with your password.',
  // not translated — awaiting native-speaker review
  'auth.problem.securityRefused':
    'The shop system refused that request for safety. Reload the page and try again.',
  // not translated — awaiting native-speaker review
  'auth.problem.mfaAlreadyEnrolled': 'An authenticator is already set up on this account.',
  'auth.problem.secondFactorRequired':
    'Confirm it is you with your authenticator or a recovery code before changing how you sign in.',
  'auth.problem.signInIncomplete': 'Finish signing in first, then try that again.',
  // not translated — awaiting native-speaker review
  'auth.problem.lastFactor':
    'This is the only way you can confirm it is you, so it cannot be removed. Add another one first.',

  // not translated — awaiting native-speaker review
  'auth.validation.identifierRequired': 'Type your sign-in name or email address.',
  // not translated — awaiting native-speaker review
  'auth.validation.passwordRequired': 'Type your password.',
  // not translated — awaiting native-speaker review
  'auth.validation.codeRequired': 'Type the code.',
  // not translated — awaiting native-speaker review
  'auth.validation.emailRequired': 'Type the email address on your account.',
  // not translated — awaiting native-speaker review
  'auth.validation.newPasswordRequired': 'Choose a new password.',
  // not translated — awaiting native-speaker review
  'auth.validation.repeatRequired': 'Type the new password again.',

  // not translated — awaiting native-speaker review
  'auth.signIn.title': 'Sign in',
  // not translated — awaiting native-speaker review
  'auth.signIn.intro': 'Sign in with the details your shop administrator gave you.',
  // not translated — awaiting native-speaker review
  'auth.signIn.identifier.label': 'Sign-in name or email address',
  // not translated — awaiting native-speaker review
  'auth.signIn.identifier.description': 'Whichever your shop administrator set up for you.',
  // not translated — awaiting native-speaker review
  'auth.signIn.password.label': 'Password',
  // not translated — awaiting native-speaker review
  'auth.signIn.password.description':
    'Never share it, and never type it while somebody is watching.',
  // not translated — awaiting native-speaker review
  'auth.signIn.submit': 'Sign in',
  // not translated — awaiting native-speaker review
  'auth.signIn.failed': 'Signing in',
  // not translated — awaiting native-speaker review
  'auth.signIn.forgot': 'I have forgotten my password',
  // not translated — awaiting native-speaker review
  'auth.signIn.passkey': 'Sign in with a passkey',
  // not translated — awaiting native-speaker review
  'auth.signIn.passkey.description':
    'If you have set up a passkey, your device can confirm it is you with a fingerprint, a face or a device code.',
  // not translated — awaiting native-speaker review
  'auth.signIn.passkey.unsupported':
    'This browser cannot use passkeys. Sign in with your password.',
  // not translated — awaiting native-speaker review
  'auth.signIn.passkey.cancelled': 'That was cancelled. Nothing has changed.',
  // not translated — awaiting native-speaker review
  'auth.signIn.captcha.label': 'Human check',
  // not translated — awaiting native-speaker review
  'auth.signIn.captcha.description':
    'Paste the response from the check shown by your browser. Your administrator can explain this.',
  // not translated — awaiting native-speaker review
  'auth.signIn.signedOut': 'You have been signed out.',
  // not translated — awaiting native-speaker review
  'auth.signIn.otherSection': 'Other ways to sign in',

  // not translated — awaiting native-speaker review
  'auth.challenge.title': 'Confirm it is you',
  // not translated — awaiting native-speaker review
  'auth.challenge.intro':
    'Your password is right. One more step confirms that it is you and not somebody who has learned it.',
  // not translated — awaiting native-speaker review
  'auth.challenge.action': 'Confirming it is you',
  // not translated — awaiting native-speaker review
  'auth.challenge.factor.legend': 'How would you like to confirm?',
  // not translated — awaiting native-speaker review
  'auth.challenge.factor.totp': 'A code from my authenticator app',
  // not translated — awaiting native-speaker review
  'auth.challenge.factor.recoveryCode': 'One of my printed recovery codes',
  // not translated — awaiting native-speaker review
  'auth.challenge.code.label.totp': 'Code from your authenticator',
  // not translated — awaiting native-speaker review
  'auth.challenge.code.description.totp':
    'Six digits. It changes every 30 seconds, so use the one showing now.',
  // not translated — awaiting native-speaker review
  'auth.challenge.code.label.recoveryCode': 'Recovery code',
  // not translated — awaiting native-speaker review
  'auth.challenge.code.description.recoveryCode':
    'One of the codes printed when you set up your authenticator. Each one works once.',
  // not translated — awaiting native-speaker review
  'auth.challenge.remember.label': 'Remember this device',
  // not translated — awaiting native-speaker review
  'auth.challenge.remember.description':
    'Only on a device that is yours. Never on a shared counter or workshop device.',
  // not translated — awaiting native-speaker review
  'auth.challenge.submit': 'Confirm',
  // not translated — awaiting native-speaker review
  'auth.challenge.remaining':
    '{count, plural, =0 {No recovery codes left} one {# recovery code left} other {# recovery codes left}}',
  // not translated — awaiting native-speaker review
  'auth.challenge.reissueSoon':
    'You are running low on recovery codes. Print a new sheet from Sign-in and security.',
  // not translated — awaiting native-speaker review
  'auth.challenge.none.title': 'There is no way to confirm it is you',
  // not translated — awaiting native-speaker review
  'auth.challenge.none.body':
    'This account needs a second step, and nothing is set up to provide one. Ask your shop administrator to reset it for you.',
  // not translated — awaiting native-speaker review
  'auth.challenge.passkey': 'Use a passkey instead',

  // not translated — awaiting native-speaker review
  'auth.enrol.title': 'Set up your authenticator',
  // not translated — awaiting native-speaker review
  'auth.enrol.action': 'Setting up your authenticator',
  // not translated — awaiting native-speaker review
  'auth.enrol.intro':
    'An authenticator app on your phone shows a new code every 30 seconds. You will be asked for one when you sign in.',
  // not translated — awaiting native-speaker review
  'auth.enrol.required.title': 'This account needs a second step',
  // not translated — awaiting native-speaker review
  'auth.enrol.required.body':
    'Set up an authenticator app before you carry on. It takes about a minute, and you only do it once.',
  // not translated — awaiting native-speaker review
  'auth.enrol.begin': 'Start setting up',
  // not translated — awaiting native-speaker review
  'auth.enrol.secret.warning':
    'What is on this screen is a key to your account. Anybody who photographs it can sign in as you, so keep the screen to yourself until you have finished.',
  // not translated — awaiting native-speaker review
  'auth.enrol.scan.title': 'Point your authenticator app at this',
  // not translated — awaiting native-speaker review
  'auth.enrol.qr.alt':
    'A QR code that adds {account} at {issuer} to an authenticator app. The setup key below says the same thing in words.',
  // not translated — awaiting native-speaker review
  'auth.enrol.open': 'Open in my authenticator app',
  // not translated — awaiting native-speaker review
  'auth.enrol.open.description':
    'Use this when the authenticator app is on the same phone as this screen — a phone cannot photograph itself.',
  // not translated — awaiting native-speaker review
  'auth.enrol.manual.title': 'Or type it in by hand',
  // not translated — awaiting native-speaker review
  'auth.enrol.manual.label': 'Setup key',
  // not translated — awaiting native-speaker review
  'auth.enrol.manual.description':
    'Groups of four, in any order of upper or lower case. The app will ask for {digits} digits and change them every {seconds} seconds.',
  // not translated — awaiting native-speaker review
  'auth.enrol.copy': 'Copy the setup key',
  // not translated — awaiting native-speaker review
  'auth.enrol.copied': 'Setup key copied.',
  // not translated — awaiting native-speaker review
  'auth.enrol.copyFailed': 'This browser would not copy it. Type the key in instead.',
  // not translated — awaiting native-speaker review
  'auth.enrol.confirm.title': 'Now type the code it shows',
  // not translated — awaiting native-speaker review
  'auth.enrol.confirm.intro':
    'This proves the app and this shop system agree, before your password stops being enough on its own.',
  // not translated — awaiting native-speaker review
  'auth.enrol.code.label': 'Code from your authenticator',
  // not translated — awaiting native-speaker review
  'auth.enrol.code.description': 'Six digits, as they appear in the app right now.',
  // not translated — awaiting native-speaker review
  'auth.enrol.confirm.submit': 'Confirm the authenticator',
  // not translated — awaiting native-speaker review
  'auth.enrol.confirmed': 'Your authenticator is set up.',
  // not translated — awaiting native-speaker review
  'auth.enrol.restart': 'Start again with a new key',

  // not translated — awaiting native-speaker review
  'auth.codes.title': 'Your recovery codes',
  // not translated — awaiting native-speaker review
  'auth.codes.intro':
    'These get you back in if you lose your phone. Print them, or write them down, and keep them where you keep the shop keys.',
  // not translated — awaiting native-speaker review
  'auth.codes.once':
    'This is the only time these are shown. Nobody — not your administrator, not this shop system — can show them to you again. Printing a new sheet destroys this one.',
  // not translated — awaiting native-speaker review
  'auth.codes.list.label': 'Recovery codes',
  // not translated — awaiting native-speaker review
  'auth.codes.print': 'Print these codes',
  // not translated — awaiting native-speaker review
  'auth.codes.copy': 'Copy the codes',
  // not translated — awaiting native-speaker review
  'auth.codes.copied': 'Recovery codes copied.',
  // not translated — awaiting native-speaker review
  'auth.codes.acknowledge': 'I have written these down or printed them',
  // not translated — awaiting native-speaker review
  'auth.codes.continue': 'Finish',
  // not translated — awaiting native-speaker review
  'auth.codes.acknowledge.missing': 'Confirm that you have kept the codes before you finish.',
  // not translated — awaiting native-speaker review
  'auth.codes.reissue': 'Print a new sheet of recovery codes',
  // not translated — awaiting native-speaker review
  'auth.codes.reissue.action': 'Printing a new sheet of recovery codes',
  // not translated — awaiting native-speaker review
  'auth.codes.reissue.title': 'Print a new sheet of recovery codes?',
  // not translated — awaiting native-speaker review
  'auth.codes.reissue.body':
    'The codes you have now stop working straight away, whether or not you have used them. You will be shown the new ones once.',
  // not translated — awaiting native-speaker review
  'auth.codes.reissue.confirm': 'Print a new sheet',

  // not translated — awaiting native-speaker review
  'auth.passkeys.title': 'Passkeys',
  // not translated — awaiting native-speaker review
  'auth.passkeys.intro':
    'A passkey lets this device confirm it is you with a fingerprint, a face or a device code. It replaces both your password and the code from your authenticator.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.unsupported':
    'This browser cannot use passkeys. Your password and authenticator still work everywhere.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.action': 'Adding a passkey',
  // not translated — awaiting native-speaker review
  'auth.passkeys.add': 'Add a passkey on this device',
  // not translated — awaiting native-speaker review
  'auth.passkeys.label.label': 'What should this passkey be called?',
  // not translated — awaiting native-speaker review
  'auth.passkeys.label.description':
    'Something you will recognise in a list — "Counter tablet", "My phone".',
  // not translated — awaiting native-speaker review
  'auth.passkeys.label.missing': 'Give the passkey a name you will recognise.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.save': 'Add this passkey',
  // not translated — awaiting native-speaker review
  'auth.passkeys.cancel': 'Cancel',
  // not translated — awaiting native-speaker review
  'auth.passkeys.added': '{label} has been added.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.removed': '{label} has been removed.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.cancelled': 'That was cancelled. Nothing has been added.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.empty.title': 'No passkeys yet',
  // not translated — awaiting native-speaker review
  'auth.passkeys.empty.body':
    'Adding one on a device that is yours makes signing in a single step. Do not add one on a shared device.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.list.label': 'Your passkeys',
  // not translated — awaiting native-speaker review
  'auth.passkeys.added.on': 'Added {date}',
  // not translated — awaiting native-speaker review
  'auth.passkeys.lastUsed': 'Last used {date}',
  // not translated — awaiting native-speaker review
  'auth.passkeys.neverUsed': 'Not used yet',
  // not translated — awaiting native-speaker review
  'auth.passkeys.backedUp': 'Also on your other devices',
  // not translated — awaiting native-speaker review
  'auth.passkeys.deviceOnly': 'On this device only',
  // not translated — awaiting native-speaker review
  'auth.passkeys.remove': 'Remove',
  // not translated — awaiting native-speaker review
  'auth.passkeys.remove.action': 'removing {label}',
  // not translated — awaiting native-speaker review
  'auth.passkeys.remove.title': 'Remove {label}?',
  // not translated — awaiting native-speaker review
  'auth.passkeys.remove.body':
    'That device will no longer be able to sign in with a passkey. Your password and authenticator are unaffected.',
  // not translated — awaiting native-speaker review
  'auth.passkeys.remove.confirm': 'Remove this passkey',

  // not translated — awaiting native-speaker review
  'auth.security.title': 'Sign-in and security',
  // not translated — awaiting native-speaker review
  'auth.security.intro': 'How you prove it is you, and where your account is signed in.',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.title': 'Authenticator app',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.Enrolled': 'Set up and working.',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.NotEnrolled': 'Not set up yet.',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.PendingConfirmation':
    'Started but never finished. Set it up again to complete it.',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.ResetRequired':
    'Reset by an administrator. Set it up again before you next sign in.',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.setUp': 'Set up an authenticator',
  // not translated — awaiting native-speaker review
  'auth.security.authenticator.replace': 'Set up a different authenticator',
  // not translated — awaiting native-speaker review
  'auth.security.codes.title': 'Recovery codes',
  // not translated — awaiting native-speaker review
  'auth.security.codes.count':
    '{count, plural, =0 {No unused codes left} one {# unused code left} other {# unused codes left}}',
  // not translated — awaiting native-speaker review
  'auth.security.codes.none': 'You will get a sheet of codes when you set up an authenticator.',
  // not translated — awaiting native-speaker review
  'auth.security.sessions.title': 'Devices',
  // not translated — awaiting native-speaker review
  'auth.security.sessions.body': 'See where your account is signed in, and sign a device out.',
  // not translated — awaiting native-speaker review
  'auth.security.sessions.link': 'Devices you are signed in on',
  // not translated — awaiting native-speaker review
  'auth.security.signOut.title': 'Signing out',
  // not translated — awaiting native-speaker review
  'auth.security.signOut.body':
    'Ends this session on this device only. Your other devices stay signed in.',
  // not translated — awaiting native-speaker review
  'auth.security.signOut': 'Sign out of this device',
  // not translated — awaiting native-speaker review
  'auth.security.mustChangePassword.title': 'You need to change your password',
  // not translated — awaiting native-speaker review
  'auth.security.mustChangePassword.body':
    'Your administrator has asked for a new password. Changing it from here arrives shortly; until then, ask them to reset it for you.',

  // not translated — awaiting native-speaker review
  'auth.recovery.request.title': 'Reset your password',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.action': 'Asking for a reset link',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.intro':
    'Type the email address on your account. If it is one we know, a link to set a new password is on its way.',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.email.label': 'Email address',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.email.description':
    'The address your shop administrator set the account up with.',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.submit': 'Send me a link',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.sent.title': 'Check your email',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.sent.body':
    'If that address belongs to an account, a link is on its way. It can be used once and expires shortly, so look for it now.',
  // not translated — awaiting native-speaker review
  'auth.recovery.request.sent.note':
    'We say the same thing whether or not the address is one we know, so that nobody can use this screen to find out who has an account.',
  // not translated — awaiting native-speaker review
  'auth.recovery.backToSignIn': 'Back to signing in',

  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.title': 'Choose a new password',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.action': 'Setting your new password',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.intro':
    'Pick something long that you have not used anywhere else. A short phrase of unrelated words is easier to remember and harder to guess than a short jumble.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.password.label': 'New password',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.password.description':
    'At least 12 characters. It must not contain your name, your sign-in name or your email address.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.repeat.label': 'Type the new password again',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.repeat.description': 'So that a typing mistake does not lock you out.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.mismatch': 'The two passwords are not the same.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.submit': 'Set the new password',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.missing.title': 'That link is not complete',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.missing.body':
    'Open the link from the email again, or ask for a new one. Copying it by hand often loses the end of it.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.done.title': 'Your password is set',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.done.body':
    '{count, plural, =0 {No other devices were signed in.} one {# device was signed out.} other {# devices were signed out.}} Sign in with the new password.',
  // not translated — awaiting native-speaker review
  'auth.recovery.confirm.done.stillMfa':
    'You will still be asked for a code from your authenticator, as usual.',

  // not translated — awaiting native-speaker review
  'auth.sessions.title': 'Devices you are signed in on',
  // not translated — awaiting native-speaker review
  'auth.sessions.intro':
    'If you do not recognise something here, sign it out and change your password. Only you can see this list.',
  // not translated — awaiting native-speaker review
  'auth.sessions.loading': 'the devices you are signed in on',
  // not translated — awaiting native-speaker review
  'auth.sessions.action': 'Loading your devices',
  // not translated — awaiting native-speaker review
  'auth.sessions.list.label': 'Devices signed in',
  // not translated — awaiting native-speaker review
  'auth.sessions.current': 'This device',
  // not translated — awaiting native-speaker review
  'auth.sessions.mfaSatisfied': 'Confirmed with a second step',
  // not translated — awaiting native-speaker review
  'auth.sessions.passwordOnly': 'Password only',
  // not translated — awaiting native-speaker review
  'auth.sessions.startedAt': 'Signed in {date}',
  // not translated — awaiting native-speaker review
  'auth.sessions.lastSeen': 'Last used {date}',
  // not translated — awaiting native-speaker review
  'auth.sessions.endsAt': 'Ends {date}',
  // not translated — awaiting native-speaker review
  'auth.sessions.address': 'From {address}',
  // not translated — awaiting native-speaker review
  'auth.sessions.address.unknown': 'Address not recorded',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke': 'Sign this device out',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke.action': 'signing {device} out',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke.title': 'Sign {device} out?',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke.body':
    'Whoever is using it will have to sign in again. Anything they have typed and not saved is lost.',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke.bodyCurrent':
    'This is the device you are using. You will be signed out and taken back to the sign-in screen.',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoke.confirm': 'Sign it out',
  // not translated — awaiting native-speaker review
  'auth.sessions.revoked': '{device} has been signed out.',
  // not translated — awaiting native-speaker review
  'auth.sessions.everywhere': 'Sign out everywhere',
  // not translated — awaiting native-speaker review
  'auth.sessions.everywhere.action': 'signing out everywhere',
  // not translated — awaiting native-speaker review
  'auth.sessions.everywhere.title': 'Sign out of every device?',
  // not translated — awaiting native-speaker review
  'auth.sessions.everywhere.body':
    'Every device is signed out, including this one, and every remembered device is forgotten. Use this if you think somebody knows your password.',
  // not translated — awaiting native-speaker review
  'auth.sessions.everywhere.confirm': 'Sign out everywhere',
  // not translated — awaiting native-speaker review
  'auth.sessions.empty.title': 'Only this device',
  // not translated — awaiting native-speaker review
  'auth.sessions.empty.body': 'Your account is not signed in anywhere else.',

  // not translated — awaiting native-speaker review
  'auth.expiry.title': 'You will be signed out soon',
  // not translated — awaiting native-speaker review
  'auth.expiry.body':
    'This device has been idle. Nothing you have typed will be lost — choosing to carry on keeps you exactly where you are.',
  // not translated — awaiting native-speaker review
  'auth.expiry.countdown': '{seconds, plural, one {# second} other {# seconds}} left',
  // not translated — awaiting native-speaker review
  'auth.expiry.announce':
    'You will be signed out in about {seconds, plural, one {# second} other {# seconds}}. Choose carry on working to stay signed in.',
  // not translated — awaiting native-speaker review
  'auth.expiry.keepWorking': 'Carry on working',
  // not translated — awaiting native-speaker review
  'auth.expiry.signOutNow': 'Sign out now',

  // not translated — awaiting native-speaker review
  'auth.reauth.title.expired': 'Your session ended',
  // not translated — awaiting native-speaker review
  'auth.reauth.title.revoked': 'You were signed out',
  // not translated — awaiting native-speaker review
  'auth.reauth.title.stepUp': 'Confirm it is you',
  // not translated — awaiting native-speaker review
  'auth.reauth.body.expired':
    'Nothing you have typed has been lost. Sign back in and you will carry on exactly where you were.',
  // not translated — awaiting native-speaker review
  'auth.reauth.body.revoked':
    'This account was signed out from somewhere else. Nothing you have typed has been lost. If you did not do that, sign back in and change your password.',
  // not translated — awaiting native-speaker review
  'auth.reauth.body.stepUp':
    'Before {action}, type your password again. It has been a while since you last did.',
  // not translated — awaiting native-speaker review
  'auth.reauth.as': 'Signed in as {name}',
  // not translated — awaiting native-speaker review
  'auth.reauth.password.label': 'Password',
  // not translated — awaiting native-speaker review
  'auth.reauth.submit': 'Sign back in',
  // not translated — awaiting native-speaker review
  'auth.reauth.submit.stepUp': 'Confirm',
  // not translated — awaiting native-speaker review
  'auth.reauth.abandon': 'Not now',
  // not translated — awaiting native-speaker review
  'auth.reauth.action': 'continuing with this action',
  // not translated — awaiting native-speaker review
  'auth.reauth.done': 'Signed back in. Carrying on where you left off.',
  // not translated — awaiting native-speaker review
  'auth.reauth.code.title': 'One more step',
  // not translated — awaiting native-speaker review
  'auth.reauth.code.body': 'Type the code from your authenticator to finish signing back in.',

  // not translated — awaiting native-speaker review
  'auth.guard.loading': 'your account',
  // not translated — awaiting native-speaker review
  'auth.guard.unavailable.title': 'Your account could not be loaded',
  // not translated — awaiting native-speaker review
  'auth.guard.unavailable.action': 'Loading your account',
  // not translated — awaiting native-speaker review
  'auth.guard.signIn': 'Go to signing in',
}
