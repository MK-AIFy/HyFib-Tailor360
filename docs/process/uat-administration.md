# User acceptance — administering people, branches and settings

The scripted manual procedure a business owner runs to accept issue #25. Four journeys —
**onboarding, transfer, suspension and emergency revocation** — driven through the administration
screens by a person, against a running installation with synthetic data.

It exists because the automated tiers cannot answer the question this issue is actually about. They
prove that a suspension revokes sessions, that a reason is demanded, that a stale version is refused
and that every change is recorded. They cannot prove that the person who has to do it at half past
five on a Friday can find the screen, understand what the confirmation is telling them, and believe
the answer. That is what an owner sitting at the application decides.

Read it with [`../security/permission-matrix.md`](../security/permission-matrix.md) for the grants
being exercised, [`../security/role-walkthrough.md`](../security/role-walkthrough.md) for the
authorisation demonstration this one sits on top of, and
[`definition-of-done.md`](definition-of-done.md) item 9 for what counts as evidence.

---

## 1. Status

> **Written and not yet run.** The screens exist; the run needs a business owner at a keyboard and
> half an hour, and it has not been scheduled. Nothing below is a record of anything having happened.

| | |
| --- | --- |
| **Written by** | Issue #25, on 2026-09-07 |
| **Last run** | Never |
| **Run by** | The business owner, with the technical reviewer taking the evidence |
| **Prerequisite** | An installation with the reference data seeded and at least two branches open. `./scripts/dev reset` produces one |
| **Evidence expected** | Per step: a screenshot of what the person saw, and — for every refusal — the RFC 9457 problem-details body with its correlation identifier. Personal data is edited out of the evidence before it is attached, per the role walkthrough's section 7 |

**Who runs each part.** Sections 3 to 6 are run as an **Owner**. Section 7 is run twice, once as an
**Admin** and once as an account holding no administrative permission at all, because half of what
this issue promises is what a person *cannot* do.

---

## 2. What a step is asking

Each row has an action, an expectation, and a **question for the person running it** — because a step
that only checks the software would be an integration test, and this is not one. The question is the
acceptance: if the answer is no, the step fails even when the software did exactly what the
expectation says.

Three of those questions recur, and they are the ones worth reading before starting:

- **Did the confirmation tell you what would happen to the person, before you pressed it?** Not what
  would happen to the record — what the colleague standing at a till would experience.
- **Would you have known what to write in the reason box without being told?** The reason is read a
  year later by somebody reconstructing a decision. A box that collects the word "reason" has failed.
- **When it refused you, did you know what to do next?** A refusal that leaves a person phoning the
  owner has failed even when the refusal was correct.

---

## 3. Onboarding — a new member of staff can work by the end of the conversation

The journey a shop runs most: somebody starts on Monday.

| # | Action | Expected | Question for the runner | Result |
| --- | --- | --- | --- | --- |
| 3.1 | Open **Administration → Staff accounts** | The list shows every account with its status and roles | Could you tell at a glance who can currently sign in? | |
| 3.2 | Search for the new person by name | No match, and an empty state that says so | Did it read as "nobody by that name" rather than as a broken search? | |
| 3.3 | Type a phone number into the same box | No match | Did the screen tell you it matches names only, **before** you concluded it was broken? | |
| 3.4 | Invite the person: name, sign-in name, address, home branch, roles | The account is created as **Invited**, and cannot sign in yet | Was it clear that they cannot sign in until they set a password? | |
| 3.5 | Check the invitation reached the address (Mailpit in a development installation) | One message, with a single-use link | | |
| 3.6 | Follow the link as the new person and set a password | They are asked to enrol a second factor before reaching anything | Did the enrolment explain why, in terms of the shop rather than of security? | |
| 3.7 | Sign in as them | They reach their branch's screens and nothing else | | |
| 3.8 | Back as the Owner, open **Audit trail** and filter on `identity.user.` | The invitation is there, with your name and the reason you gave | Does the entry say enough for somebody to understand the decision in a year? | |

---

## 4. Transfer — somebody moves branch, or changes what they do

| # | Action | Expected | Question for the runner | Result |
| --- | --- | --- | --- | --- |
| 4.1 | Open the person from section 3, then **Roles and branches** | Their current roles and branches, with the primary one marked | Was it obvious which branch their screens open on? | |
| 4.2 | Tick a second branch and save, with a reason | Saved; they now work in both | Did you understand you were sending the whole set rather than adding one? | |
| 4.3 | Sign in as them and confirm they can reach the second branch's work | Permitted | | |
| 4.4 | As the Owner, remove the first branch and save | Saved | | |
| 4.5 | As them, try to open something in the branch just removed | Refused, and the refusal says which branch they may work in | Did the refusal tell them what to do — ask an administrator — rather than just "forbidden"? | |
| 4.6 | As the Owner, open **Roles and permissions**, open the role they hold, and read what it allows | Each permission has a sentence, and the strict ones say so | Did you understand, before ticking anything, that some permissions make everybody holding the role re-authenticate mid-task? | |
| 4.7 | Try to grant that role a permission **you do not hold yourself** | **Refused**, and the refusal says you cannot give away what you do not have | Did you understand why, and who to ask? | |

---

## 5. Suspension — somebody stops working here, reversibly

| # | Action | Expected | Question for the runner | Result |
| --- | --- | --- | --- | --- |
| 5.1 | Have the person sign in on a second device and leave it open | | | |
| 5.2 | As the Owner, open their account and press **Suspend** | A confirmation saying they will be signed out of every device immediately | Did it tell you what happens to the person, not to the record? | |
| 5.3 | Confirm, with a reason | Applied; the account reads **Suspended** | | |
| 5.4 | Watch the second device | The next thing they do is refused, and the message says the session ended | Was it clear to them that this was not a fault? | |
| 5.5 | Try to sign in as them | Refused, with no hint about which of password, account or status was wrong | | |
| 5.6 | As the Owner, press **Lift suspension**, with a reason | They can sign in again with the password and second factor they already had | Did you expect their credentials to survive? Was that the right answer? | |
| 5.7 | Open **Audit trail**, filter on the account | Both entries, each with its reason, actor and time, and the before-and-after behind the disclosure | Could a reviewer reconstruct what happened without asking you? | |

---

## 6. Emergency revocation — somebody's password is known and it is now

The journey that matters most and is practised least. Run it against the clock, and record how long
it took: a control that takes eleven minutes to find is a control that is not there.

| # | Action | Expected | Question for the runner | Result |
| --- | --- | --- | --- | --- |
| 6.1 | Starting from the home screen, get to the account | | **How many seconds?** Write the number down | |
| 6.2 | Press **Sign out everywhere**, with a reason | Every session ends at once, including any they are standing at. The account is **not** stopped — they can sign back in | Was the difference between this and Suspend clear from the two confirmations? | |
| 6.3 | Decide the account itself is compromised, and press **Suspend** as well | Signed out and unable to return | | |
| 6.4 | Press **Reset second factor**, with a reason | Their authenticator and recovery codes are cleared; they enrol again next time they sign in | Did the confirmation warn you that the account is protected by a password alone until they do? | |
| 6.5 | Try to do any of the above **to your own account** | **Refused**, and the screen says why before you press anything | Did you understand you would have locked yourself out of the screen that undoes it? | |
| 6.6 | Press **Close account** on a different, genuinely departing account | A confirmation saying nothing is deleted and that reopening starts their credentials from scratch | Did you believe the promise that their orders and invoices stay resolvable? | |
| 6.7 | Find one of that person's old orders | Still there, still naming them | | |

---

## 7. What an administrator cannot do

Run once as an **Admin**, and once as an account holding no administrative permission.

| # | As | Action | Expected | Result |
| --- | --- | --- | --- | --- |
| 7.1 | Admin | Open **Administration** | The sections they hold a permission for, and no others | |
| 7.2 | Admin | Change a feature setting | **Refused** — Owner and the vendor principal only | |
| 7.3 | Admin | Open the audit trail | **Refused** — Owner and Auditor only | |
| 7.4 | No permissions | Open `/admin/users` directly by typing the address | A plain sentence saying the account does not hold the permission, and who to ask — never a blank screen and never a redirect loop | |
| 7.5 | No permissions | Call `POST /api/v1/admin/users/{id}/suspend` directly, with a valid session | **Refused** by the server, whatever the screen showed | |
| 7.6 | Owner, signed in over twenty minutes ago without re-authenticating | Suspend an account | **Refused for staleness**, and the response makes clear that re-authenticating would work | |
| 7.7 | Owner | Re-authenticate and repeat 7.6 | Permitted | |

---

## 8. Recording the result

Fill the **Result** column with `pass`, `fail` or `n/a` and a one-line note. A failed step is an
issue, linked here, not a note in the margin — and a step whose *question* was answered no is a
failed step even when the software behaved as the expectation says.

Attach, in the pull request or the release evidence:

- One screenshot per journey, at the width the person actually used
- The problem-details body for every refusal, with its correlation identifier
- The number from step 6.1

Personal data is edited out of every screenshot before it is attached. The synthetic data this is run
against contains none, and the edit is the habit rather than the remedy.
