# Customer name normalisation

One person's name reaches a tailoring shop written several ways. **Lakshmi**, **Laxmi**, **Lakshmy** and **Lakshmee**
are one customer; so are **Shanthi** and **Santhi**, **Muthu** and **Muttu**, **Vijaya** and **Wijaya**. Nobody at a
counter is going to try four spellings before deciding the customer is new, so the second record gets created — and a
duplicate customer is exception EX-01 in [`../prd/exceptions.md`](../prd/exceptions.md), whose only remedy is an
irreversible, step-up merge.

Collapsing the spellings **before** they are compared is much cheaper than merging afterwards. This document is the
rule table that does it, and its vectors in section 5 are the unit tests
([`../../tests/Tailor360.UnitTests/Customers/CustomerNameNormaliserTests.cs`](../../tests/Tailor360.UnitTests/Customers/CustomerNameNormaliserTests.cs)).
The implementation is
[`CustomerNameNormaliser`](../../src/Modules/Customers/Tailor360.Modules.Customers.Domain/Naming/CustomerNameNormaliser.cs).
**Changing the table changes both.**

---

## 1. What the key is, and what it is not

The output is a **search key**, not a name.

| It is | It is not |
| --- | --- |
| Lower-case ASCII letters and digits, single-spaced, at most 200 characters | Ever displayed, printed, spoken to a customer or put on a receipt |
| Stored in `customers.normalised_name` and in `customer_aliases.normalised_value` | Ever an identifier — resource identifiers are UUIDv7, and the display number is `C-<branch>-000000` |
| Compared by the counter search and by the duplicate score | A transliteration: it is not a spelling anyone chose, and nothing renders it |

The name the customer actually gave is stored separately and unaltered in `display_name`, and the optional
Tamil-script form is stored in `native_name` and searched on its own.

**The folding is lossy in one direction only.** It may bring two different people together, and it may never keep one
person apart from themselves. That trade is safe here because nothing acts on the key by itself: a duplicate suspicion
is shown to a person with its reasons and requires an authorised decision (issue #26), and a search result is a list
somebody reads. It would not be safe if anything merged on it, which is why merging is a separate, step-up,
reason-carrying permission.

---

## 2. The pipeline

Four steps, in this order.

1. **Fold accents.** `Révathi` → `Revathi`, `Kṛṣṇa` → `Krsna`. Section 3.
2. **Tokenise on anything that is not an ASCII letter or digit**, lower-casing as it goes. Dots, hyphens and
   apostrophes are separators rather than characters: `R.`, `R` and `R-` are one initial, and `D'Souza` and `D Souza`
   are one surname. Anything still outside ASCII after step 1 — Tamil script, for instance — is dropped here.
3. **Apply the substitutions to each token**, in the published order. Section 4.
4. **Collapse runs of a repeated letter.** `muttu` → `mutu`, `sellvam` → `selvam`, `kaala` → `kala`. It runs last so
   that `ee` can become `i` in step 3 rather than being flattened to `e` first.

A name with no Latin letter or digit in it folds to the **empty string**, and that is deliberate. Transliterating
`கவிதா` in code would invent a Latin spelling nobody chose and put a guess into a search index; the native-script
column is stored and searched directly instead.

An empty key is not the end of it, and the caller is where that is handled. When a customer gives her name **only**
in native script, `CustomerDetails.Create` stores that name in `native_name` as well as in `display_name`, so the
record still carries a name the search and the duplicate score can compare. That is not a transliteration — it is
the same string, in the column that is searched directly — and a name given in both scripts is untouched. Without
it the record would have no name key at all: found by no name search, contributing no name reason to a duplicate
score, and therefore likely to be created a second time, which is the outcome this whole document exists to
prevent.

---

## 3. Accent folding

Accented Latin letters are folded onto the letters underneath them by an **explicit table**, not by Unicode
normalisation.

That is not a style choice. The solution builds with `InvariantGlobalization` (`Directory.Build.props`), and under
invariant globalization `string.Normalize(NormalizationForm.FormD)` **returns the string unchanged rather than
failing**. An accented letter would survive to the tokeniser, be rejected as "not an ASCII letter", and split the
name in half: `Révathi` folded to `r vati` and was never found again. The failure is silent, which is why the table is
written out rather than delegated.

The set covers:

| Range | Why a name arrives with it |
| --- | --- |
| Latin-1 Supplement and Latin Extended-A | `é`, `ö`, `ç`, `ł`, `š` — the letters of any name typed on a European keyboard layout |
| Dot-below and macron letters of scholarly Indic transliteration | `ṇ`, `ṭ`, `ś`, `ṣ`, `ṛ`, `ṃ` — how a name copied from an identity document sometimes arrives |
| Nine letters that fold to more than one, held separately because they cannot live in a parallel string | `Æ`→`AE`, `æ`→`ae`, `Œ`→`OE`, `œ`→`oe`, `ß`→`ss`, `Þ`→`TH`, `þ`→`th`, `Ð`→`D`, `ð`→`d` |

The mapping is held as **two parallel strings** rather than a dictionary so a reviewer can see the whole thing on one
screen and check it by eye. `LatinFolding.TablesAgree` asserts that the two are the same length, and a unit test
asserts that assertion — a table one character out of step would fold half the alphabet to the wrong letter and still
compile.

A **combining mark** — a base letter followed by its own accent, which is what some phone keyboards produce — is
dropped rather than looked up, so `e` + U+0301 and `é` fold to the same `e`. Anything else outside the set is left
alone, and the tokeniser then treats it as a separator.

---

## 4. The substitution table

Applied in this order, to each token. **Order matters**, and two places in it are load-bearing: `tch` has to become
`ch` before `ch` becomes `c`, and `sh` has to become `s` before `w` becomes `v`, so that Bhuvaneshwari and
Bhuvaneswari meet.

| # | From | To | Applies | The pair it exists for |
| --- | --- | --- | --- | --- |
| 1 | `x` | `ks` | anywhere | Laxmi and Lakshmi |
| 2 | `tch` | `ch` | anywhere | Kutchi and Kuchi |
| 3 | `chh` | `ch` | anywhere | Chhaya and Chaya |
| 4 | `zh` | `l` | anywhere | Tamizh and Tamil — the Tamil letter ழ that has no Latin equivalent |
| 5 | `sh` | `s` | anywhere | Shanthi and Santhi, and Lakshmi and Laksmi |
| 6 | `ch` | `c` | anywhere | Chitra and Citra |
| 7 | `th` | `t` | anywhere | Kavitha and Kavita |
| 8 | `dh` | `d` | anywhere | Radha and Rada |
| 9 | `bh` | `b` | anywhere | Bhuvana and Buvana |
| 10 | `gh` | `g` | anywhere | Meghna and Megna |
| 11 | `kh` | `k` | anywhere | Lekha and Leka |
| 12 | `jh` | `j` | anywhere | Jhansi and Jansi |
| 13 | `ph` | `p` | anywhere | Sophia and Sopia |
| 14 | `w` | `v` | anywhere | Wijaya and Vijaya |
| 15 | `ee` | `i` | anywhere | Deepa and Dipa |
| 16 | `oo` | `u` | anywhere | Poornima and Purnima |
| 17 | `y` | `i` | **end of word only** | Lakshmy and Lakshmi — the interchangeable final vowel |

Rule 17 is the only word-final one, and that restriction is what keeps **Yamuna** and **Iamuna** apart while bringing
**Lakshmy** and **Lakshmi** together: a `y` that starts a name is a consonant and a `y` that ends one is a vowel.

Rules 5 to 13 are one family — the **optional aspirate `h`** — written out one digraph at a time rather than as a
single "drop `h` after a consonant" rule, so that each line carries the pair it exists for and a reviewer can delete
one without touching the others.

---

## 5. Test vectors

These are the vectors in the unit tests, and the tests are the enforcement. A change to section 4 that does not
change this section is a change nobody reviewed.

### 5.1 Pairs that must fold to one key

| Written one way | Written another | Rule |
| --- | --- | --- |
| Lakshmi | Laxmi | 1 |
| Lakshmi | Lakshmy | 17 |
| Lakshmi | Lakshmee | 15 |
| Shanthi | Santhi | 5 |
| Shanthi | Shanti | 7 |
| Muthu | Muttu | 7, then the repeat collapse |
| Vijaya | Wijaya | 14 |
| Selvam | Sellvam | repeat collapse |
| Kavitha | Kavita | 7 |
| Revathi | Revati | 7 |
| Bhuvaneswari | Bhuvaneshwari | 5 before 14 — the order that matters |
| Deepa | Dipa | 15 |
| Tamizh | Tamil | 4 |
| Chitra | Citra | 6 |
| Poornima | Purnima | 16 |
| Satish | Sathish | 7 |
| Krishna | Krisna | 5 |
| Ashwin | Ashvin | 14 |
| Kaala | Kala | repeat collapse |

### 5.2 Pairs that must **not** fold together

Every folding rule is a decision to lose information, and these are the vectors that say how much is too much. A
rule added later that broke one of these would be offering the counter a duplicate warning on two different people,
which is how a warning becomes something staff click through.

| One name | A different name |
| --- | --- |
| Kavitha | Revathi |
| Anitha | Amitha |
| Cheran | Seran |
| Ramesh | Rajesh |

### 5.3 Shape

| Written | Key |
| --- | --- |
| `Kavitha Raman` | `kavita raman` |
| `  Kavitha   Raman  ` | `kavita raman` |
| `Kavitha R.` | `kavita r` |
| `D'Souza` | `d souza` |
| `Anitha-Selvam` | `anita selvam` |
| `Révathi` | `revati` |
| `கவிதா` | *(empty)* |
| `null`, `""`, `"   "` | *(empty)* |

### 5.4 Properties

- **Folding is idempotent.** The key is written to a column and later compared against freshly folded input, so
  folding a key again has to leave it alone. A rule that broke this would make a record unfindable under the very
  name it was saved with.
- **A long name is truncated to the column.** At most 200 characters, trimmed at the end.
- **Every substitution carries the spelling pair it exists for.** The `Because` text is what this document publishes,
  so an empty one would ship a rule nobody can review.

---

## 6. How the key is used

| Use | What compares |
| --- | --- |
| Counter search on a non-numeric term | `ILIKE` against `normalised_name`, `native_name` and `customer_aliases.normalised_value`, accelerated by three `pg_trgm` GIN indexes |
| Counter search on a numeric term | Not this key at all — digits are matched against the last six of the telephone number |
| Duplicate detection at create time | The key is one of the inputs to the score in [`DuplicateScoring`](../../src/Modules/Customers/Tailor360.Modules.Customers.Domain/Deduplication/DuplicateScoring.cs); a shared telephone number alone is a `High` confidence, a name match plus a place match is `Medium`, a name match alone is `Low`, and a place match with no name match is **not a candidate at all** |
| Alias matching after a correction | A corrected name keeps the previous one as an alias, folded the same way, so the customer stays findable under the name on her old receipts |

The key is never the only thing a decision rests on. The duplicate flow shows candidates with the reasons that
produced them and requires a person to choose; that requirement is what makes an aggressive folding rule safe.

---

## 7. Changing the table

1. Add or change the row in `CustomerNameNormaliser.Substitutions`, with the spelling pair in its `Because`.
2. Add the vector to section 5.1 or 5.2 here **and** to `CustomerNameNormaliserTests`.
3. Check section 5.2 still holds. A new rule that collapses two different names is a rule to narrow, not to ship.
4. Note that existing rows keep the key they were saved with until they are next written. The folding is applied on
   write, so a rule change does not retrospectively re-fold the table — a backfill migration does, and it belongs in
   the same pull request as the rule if the rule is meant to apply to records already held.

---

## 8. Maintenance

| Field | Value |
| --- | --- |
| Owner | Customers and Measurements module |
| Source | Issue #26; plan Section 6.3 (`docs/IMPLEMENTATION_PLAN.md`) |
| Enforced by | `tests/Tailor360.UnitTests/Customers/CustomerNameNormaliserTests.cs` |
| Related | [`../prd/exceptions.md`](../prd/exceptions.md) EX-01, [`../prd/glossary.md`](../prd/glossary.md), [`../nfr/data-classification.md`](../nfr/data-classification.md) section 5.2 |
