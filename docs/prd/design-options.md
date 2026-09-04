# Design options

This document specifies the proposed design option groups, options and selection rules for every category in
[category-hierarchy.md](./category-hierarchy.md) — Blouse (Pattern), Blouse (Aari work), Salwar, Lehenga, Gown and
Kids — together with the selection cardinality, the requires / excludes / conditional-note rules and the price and
time impacts each option carries. It is the seed source for issue #30 (visual shape and design catalogue with
garment-level selections) and it is **link 3** of the five links every service type carries:
`Tailor360.Cli init-reference-data` loads exactly the groups described here, and the business review of the seeded
groups is an acceptance criterion of #30.

It is also the other half of the conditional-rule contract in
[measurement-templates.md](./measurement-templates.md): a measurement field written `Hidden when
design.sleeve_style = SLEEVELESS` reads a group defined in Section 9 below. Section 8 states that contract
precisely, so a template rule and a design group can never drift apart silently.

Every group, option, label, rule and impact below is **proposed and to be confirmed** by the Owner with the Tailor
Master (Section 11 item 10 of [../IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md), tracked centrally as
**OD-10**); the model, the code conventions and the rule semantics are settled.

Design options are versioned configuration, not code. An administrator adds a group, adds an option, uploads a new
illustration, changes a label or adds a rule through the design administration screens; no deployment is needed.

---

## 1. Scope and ownership

| Aspect | Statement |
| --- | --- |
| Owning module | Catalog/Design (`catalog` schema): `design_option_groups` → `design_options`, with `design_rules` between them |
| Implementing issues | #30 (groups, options, rules, rule engine, picker, snapshot builder, job-card component), #29 (catalog versioning and publish validation), #32a (snapshot persistence and the design-revision command), #31 (uploaded illustration media; bundled line drawings until then), #41 (the rates the impacts resolve to) |
| Linked from | The catalogue: every service type carries an ordered set of published groups — link 3 in [category-hierarchy.md](./category-hierarchy.md) |
| Selected by | Reception at intake, on the tablet picker, with the customer present; revised after confirmation only by the audited design-revision command |
| Read by | The Tailor Master and the assigned Tailor on the job card and its printed fallback; QC (#34); pricing (#41) |
| Data class | Not personal data. A design snapshot is business data attached to a garment job and is readable wherever the job card is readable. Customer reference photographs are separate media (#31) and carry their own classification. |
| Seeded groups | 43 groups across the six orderable categories — 7, 11, 6, 6, 8 and 5 — all in catalog version 1 (Section 9) |

---

## 2. Groups, options and codes

| Rule | Convention | Example |
| --- | --- | --- |
| Group code | `lower_snake_case`, ASCII, unique **within a category**, immutable once the catalog version that introduced it is published | `sleeve_style` |
| Option code | `UPPER_SNAKE_CASE`, ASCII, unique **within its group**, immutable once published | `THREE_QUARTER` |
| Fully qualified reference | `<CATEGORY_CODE>.<group_code>.<OPTION_CODE>` — the form used in seed files, rules, event payloads and exports | `BLOUSE_PATTERN.sleeve_style.CAP` |
| Label | Free text, editable at any time, localisable (`en-IN` plus optional `ta-IN`). The label may differ from the code, and changing it never changes behaviour, pricing or history. | `THREE_QUARTER` → "Three-quarter sleeve" |
| Help text | One short sentence in the customer's language of the counter, stating what the choice means for the finished garment. Required for every option. | "Sleeve ends midway between elbow and wrist." |
| Display order | Integer per group, and per option within a group; the order the picker and the printed job card use | — |
| Storage | The option **code** is stored, never the label. The snapshot additionally embeds the label and illustration reference as they stood at confirmation (Section 7). | — |
| Reused codes across categories | The same group code may appear in more than one category and means the same thing — `sleeve_style` in `BLOUSE_PATTERN` and in `GOWN` — but each category holds its **own** group record with its own option list, because a gown offers sleeve lengths a blouse does not. | — |
| `NONE` | `NONE` is a **reserved option code** meaning "the customer chose not to have this feature". It is a real, selectable option, not a sentinel: it is what makes "no slit" an explicit, priced, printed decision rather than an omission. Reserved in every group; may not be given any other meaning. | `gown_slit.NONE` |

> **Note on the reserved-word list.** Section 4 of [category-hierarchy.md](./category-hierarchy.md) forbids `NONE`,
> `DEFAULT`, `ALL` and `UNKNOWN` as **category and service-type** codes, because those are sentinel values in
> catalogue filters and exports. That rule is not extended to option codes. Design options need an explicit "none"
> the customer can be shown and can choose, and the measurement rules in
> [measurement-templates.md](./measurement-templates.md) are written against it. `DEFAULT`, `ALL` and `UNKNOWN`
> remain forbidden everywhere, and Section 8 defines how `NONE` and "nothing selected" differ.

---

## 3. Selection cardinality and the required flag

Two independent properties per group. Confusing them is the most common way a picker ends up unusable.

| Property | Values | Meaning |
| --- | --- | --- |
| Selection mode | `single` \| `multiple` | How many options may be chosen. `single` renders as a radio-style card grid, `multiple` as checkable cards with a running count. |
| Required | `true` \| `false` | Whether the garment may be confirmed with nothing chosen in this group. A required `multiple` group needs at least one option. |
| Branch availability | Set of branches | A group or an option is offered only where the branch can deliver it — the Aari groups only where an Aari specialist works. A group's availability must be a subset of its category's. |
| Active dates | `active_from`, `active_to` | Seasonal options without an administrator having to remember to switch them off. Evaluated in the branch timezone, default `Asia/Kolkata` (D11). |

**Rules**

1. A required group with every option unavailable at a branch is a publish-time error — it would make the category
   unorderable at that branch without saying so.
2. A group that is not required, and in which nothing is selected, is **unset**. Unset is not `NONE` (Section 8).
3. Making a group required in a new catalog version never invalidates a confirmed garment job; the requirement applies
   to new drafts and to any subsequent design revision.
4. The picker never pre-selects an option on the customer's behalf. A default would be printed on the job card and cut
   by a tailor without anyone having chosen it.

---

## 4. Rules: requires, excludes and conditional notes

Three rule types, evaluated by `IDesignSelectionValidator.Validate(catalogVersionId, categoryId, selections)`,
which returns violations. The engine is pure and deterministic, it holds no state, and it is the same code path at
intake, at estimate, at confirmation and at design revision.

| Type | Meaning | Effect on the picker | Effect on confirmation |
| --- | --- | --- | --- |
| **Requires** | Selecting A obliges B to be selected | Selecting A offers B and explains why; the summary lists what A brought with it | Blocking. Confirmation is refused with a field-level problem detail naming both options |
| **Excludes** | A and B may not both be selected | Selecting A disables B with the reason shown on the disabled card — never silently hidden | Blocking |
| **Conditional note** | Selecting A attaches a standing instruction to the garment job | Shown at selection and carried into the summary | Non-blocking. The note is copied into the snapshot and printed on the job card |

**Rules about rules**

1. A rule references options and groups **within one category**. Cross-category rules are not supported and are a
   publish-time error; a garment job belongs to exactly one category.
2. `requires` is not transitive by declaration but is by evaluation: if A requires B and B requires C, selecting A
   yields all three, and the picker's summary shows the chain.
3. A `requires` cycle, or a rule whose two sides are the same option, is a publish-time error.
4. A rule may not make a **required** group unsatisfiable — for example excluding every option of a required group.
   Publish-time error.
5. A conditional note is free text on the rule, localisable, and is the mechanism for craft instructions that are not
   themselves choices ("cut the lining 5 mm wider than the shell at the armhole").
6. Rules are evaluated **server-side and authoritatively**. The picker's copy is a convenience; a selection set that
   reaches confirmation is re-validated against the published rules regardless of what the client believed.
7. Every seeded rule carries a stable identifier, `DR-nn`, unique across the whole catalogue rather than per
   category. Identifiers are never re-used: a deleted rule's number is retired with it. They exist because a rule
   cannot be named reliably by the options it mentions — `DR-01` and `DR-06` both concern `padding = MOULDED_CUP`,
   so a publish-time error or a test that named only the option would be ambiguous between them. A rule an
   administrator adds is allocated the next free number by the catalogue, not by hand.

---

## 5. Price and time impacts

| Aspect | Specification |
| --- | --- |
| Time impact | Signed whole **working days**, added to the service type's expected duration (Section 3 of [category-hierarchy.md](./category-hierarchy.md)) when computing the proposed due date against the branch calendar. Applied once per garment job, summed across selected options. Never a promise to the customer — Reception may always set a later date. |
| Price impact | The option names a **price-list item code** (`PI_…`); the amount, the branch it applies at, its effective dates and its HSN/SAC and GST treatment live in the published price-list version owned by #41. The catalogue holds the code, never the money — the same invariant link 4 states in [category-hierarchy.md](./category-hierarchy.md). |
| Zero-impact options | The great majority of options cost nothing extra — a boat neck is not dearer than a round neck. An option with no price-list item produces **no invoice line**, which is why the bills in [walkthroughs.md](./walkthroughs.md) list only the two or three selections that actually carry a charge. |
| Where impacts appear | On the estimate and the invoice as their own lines, labelled from the option's label at the time of the snapshot; in the price delta shown before a design revision is approved; never rolled silently into the base rate. |
| Proposed rates below | The ₹ amounts in Section 9 are the **proposed** rates for `PL_CBE01` version 4, the price list used throughout [walkthroughs.md](./walkthroughs.md). They are indicative seed values confirmed under **OD-10** with **OD-05**; the authoritative amount is always the price-list version, per branch and effective-dated. |
| Material consumption is not a price impact | Stock issued against a job — an AD stone and bead kit, canvas, bias binding — is charged on at the configured consumption rate by Inventory (#40), on its own line. A design option chooses the look; it does not itself value the material. |

---

## 6. Illustrations and alternative text

A design picker that shows only words is a picker Reception has to translate at the counter, so every option is
primarily a **picture**.

| Rule | Specification |
| --- | --- |
| Illustration | `design_options.illustration_media_id` once media upload exists (#31); until then the bundled line drawing identified by `illustration_key` (#30) |
| Reference format | `<sheet_key>#<option_code>`, for example `design_blouse_front_neck_v1#KATORI` |
| Alt text | `illustration_alt` is **required** for every option and describes the shape in words, so the picker is usable with a screen reader and the job card is usable when printed in monochrome: "Deep rounded cup-shaped neckline with a curved seam under each cup." |
| Seeded sheets | `design_blouse_front_neck_v1`, `design_blouse_back_neck_v1`, `design_blouse_sleeve_v1`, `design_blouse_closure_v1`, `design_blouse_finish_v1`, `design_aari_motif_v1`, `design_aari_placement_v1`, `design_salwar_neck_v1`, `design_salwar_bottom_v1`, `design_lehenga_skirt_v1`, `design_lehenga_dupatta_v1`, `design_gown_silhouette_v1`, `design_gown_neckline_v1`, `design_kids_style_v1` |
| Versioning | The `_v1` suffix is part of the key. A redrawn sheet is `_v2`, referenced by a new catalog version; existing snapshots keep pointing at the drawing the customer was actually shown. |
| Namespace | Every key here begins `design_`. Design illustrations and the measurement diagrams of [measurement-templates.md](./measurement-templates.md) section 8 share the single `diagram/` object-storage prefix ([../architecture/module-ownership.md](../architecture/module-ownership.md) section 4), and the two sets would otherwise have collided on `blouse_sleeve_v1`, `salwar_bottom_v1`, `lehenga_skirt_v1` and `lehenga_dupatta_v1` — four keys naming a different drawing in each document. The anchor vocabulary differs too: a measurement diagram is anchored by field key, a design illustration by option code. |
| Zoom | Every illustration opens full-screen on tap, because a customer choosing a neckline is looking closely at it (#30, #50) |
| Printable fallback | The job card renders the illustration where it can and the label plus alt text where it cannot, so a thermal or monochrome print is never ambiguous |

---

## 7. How a selection travels

```mermaid
flowchart LR
  P[Reception and customer<br/>choose on the picker] --> D[Draft selections<br/>catalog.design_selection_drafts]
  D --> V[Rules validated server-side<br/>requires, excludes, notes]
  V --> E[Estimate<br/>impacts as their own lines]
  V --> C[Order confirmation]
  C --> S[GarmentDesignSnapshot<br/>immutable, embeds labels and<br/>illustration references]
  S --> JC[Job card and printed fallback]
  S --> QC[QC criteria that the design adds]
  S --> R[Design revision<br/>new revision row, never an edit]
```

| Stage | Rule |
| --- | --- |
| Draft | Server-side in `catalog.design_selection_drafts`, autosaved, shared within the branch, reused for intake autosave (#32b) |
| Estimate | Selections must pass the rules before an estimate may be issued ([state-transitions.md](./state-transitions.md)); measurements are not required at this point, selections are |
| Confirmation | `GarmentDesignSnapshot.From(catalogVersion, selections, notes, instructions)` — a value object serialised as JSON embedding the option codes, **labels**, illustration references and option versions, so the job card renders without a catalogue lookup, for ever |
| Immutability | The snapshot is a copy, not a reference. Republishing the catalogue, retiring a group or renaming an option never changes a confirmed garment job |
| Revision | Never an edit — `POST /jobs/{id}/design-revisions` (`orders.revise_design`) writes a new immutable row with `revision_number`, the reason, the approver, and the price and due-date delta shown **before** approval. Emits `orders.design-revised.v1`, consumed by #47 for notification |
| Freeze | Once the workflow marks the design frozen (#33), revision is refused and the change becomes an alteration (#34). Walkthrough 6 in [walkthroughs.md](./walkthroughs.md) is exactly this boundary: the gown is revised at 10:28 on 16 June and production starts at 09:00 on 17 June, after which the same request would have been an alteration |
| Re-validation on revision | A revision is validated against the **currently published** rules, not the rules the order was confirmed under. This is deliberate: a revision is a new decision taken today |

---

## 8. The `design.<group_code>` operand contract

[measurement-templates.md](./measurement-templates.md) Section 6 lets a conditional measurement rule read the
garment's design selections, written `design.<group_code>`. This section is the other side of that contract; it
exists so a template rule can never reference a group that does not exist, and so the three-state semantics are
written down once rather than assumed twice.

| Operand state | When it holds | How a hide condition evaluates |
| --- | --- | --- |
| **A value** | The group has one or more selected options | Compared normally: `=` against a single-selection group, `excludes` / `includes` against a multiple-selection group |
| **`NONE`** | The customer explicitly chose the `NONE` option | A real value. `design.gown_slit = NONE` is **true**, so `slit_height` is hidden — the customer decided against a slit |
| **Unset** | The group is not required and nothing was chosen, or measurements are being captured **outside** order intake, where there are no design selections at all | Every condition over that operand evaluates **false**, so the field stays **shown and optional**. Nothing is lost when a customer is measured before a design is chosen |

The five groups the seeded measurement templates depend on, and the exact fields they gate:

| Operand | Category or categories | Selection | Gates |
| --- | --- | --- | --- |
| `design.sleeve_style` | `BLOUSE_PATTERN`, `BLOUSE_AARI`, `SALWAR`, `LEHENGA`, `GOWN`, `KIDS` | single | `sleeve_length`, `sleeve_round`, `sleeve_upper_round` hidden when `= SLEEVELESS`; also gates `aari_sleeve_work_length` |
| `design.aari_placement` | `BLOUSE_AARI` | multiple | `aari_front_work_height/width` (`FRONT`), `aari_back_work_height/width` (`BACK`), `aari_neck_work_depth` (`NECK`), `aari_sleeve_work_length` (`SLEEVE`), `aari_border_width` (`BORDER`) |
| `design.kameez_slit` | `SALWAR` | single | `kameez_slit_height` hidden when `= NONE` |
| `design.gown_slit` | `GOWN` | single | `slit_height` hidden when `= NONE` |
| `design.dupatta` | `LEHENGA` | single | `dupatta_length`, `dupatta_width` hidden when `= NONE` |

**Guarantees.**

1. A template rule naming a group absent from the template's category is a **publish-time error** in #27, and a
   catalogue publish that would orphan such a rule is a publish-time error in #29 and #30. The two validators run
   against the same catalog version, so the pair cannot be published in an inconsistent state.
2. Renaming an option's **label** never affects a rule; rules read codes. This is why codes are immutable once
   published.
3. Hiding a field clears any value already typed, with a visible notice — a hidden value is never carried silently
   into the measurement snapshot ([measurement-templates.md](./measurement-templates.md) Section 6).
4. Whether rules may read design selections at all is open decision **OD-MEA-02**, whose fallback is a seeded
   `has_sleeves` yes/no measurement field. If that fallback is taken, only the `sleeve_style` row above changes;
   the slit, dupatta and Aari-placement rows have no template-side equivalent, and those fields would be seeded as
   shown unconditionally and optional. The groups themselves are unaffected either way — they are what the customer
   chooses, not how a template reads them.

---

## 9. Seeded option groups by category

Rate column: the proposed `PL_CBE01` version 4 amount for the named price-list item, `—` where the option carries
no charge. Days column: working days added to the service type's expected duration, `—` for none.

### 9.1 `BLOUSE_PATTERN` — Blouse, Pattern

Seven groups. The highest-volume category, so the picker is tuned for speed: five of the seven groups fit on one
tablet screen.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `front_neck` | Front neck shape | single | Yes | `design_blouse_front_neck_v1` |
| `back_neck` | Back neck shape | single | Yes | `design_blouse_back_neck_v1` |
| `sleeve_style` | Sleeve type and length | single | Yes | `design_blouse_sleeve_v1` |
| `closure` | Closure | single | Yes | `design_blouse_closure_v1` |
| `lining` | Lining and cup | single | Yes | `design_blouse_finish_v1` |
| `padding` | Padding | single | No | `design_blouse_finish_v1` |
| `finish` | Edge finish | multiple | No | `design_blouse_finish_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `front_neck` | `ROUND` | Round neck | — | — | — |
| `front_neck` | `DEEP_ROUND` | Deep round neck | — | — | — |
| `front_neck` | `V_NECK` | V neck | — | — | — |
| `front_neck` | `SWEETHEART` | Sweetheart neck | — | — | — |
| `front_neck` | `KATORI` | Katori (cup) neck | — | — | — |
| `front_neck` | `BOAT` | Boat neck | — | — | — |
| `front_neck` | `HIGH_NECK` | High neck | `PI_BLOUSE_HIGH_NECK` | ₹80.00 | — |
| `front_neck` | `SQUARE` | Square neck | — | — | — |
| `back_neck` | `ROUND` | Round back | — | — | — |
| `back_neck` | `ROUND_DEEP` | Deep round back | — | — | — |
| `back_neck` | `V_DEEP` | Deep V back | — | — | — |
| `back_neck` | `U_DEEP` | Deep U back | — | — | — |
| `back_neck` | `KEYHOLE` | Keyhole back | `PI_BLOUSE_KEYHOLE` | ₹60.00 | — |
| `back_neck` | `HIGH_NECK` | High back | — | — | — |
| `sleeve_style` | `SLEEVELESS` | Sleeveless | — | — | — |
| `sleeve_style` | `CAP` | Cap sleeve | — | — | — |
| `sleeve_style` | `SHORT` | Short sleeve | — | — | — |
| `sleeve_style` | `ELBOW` | Elbow sleeve | — | — | — |
| `sleeve_style` | `THREE_QUARTER` | Three-quarter sleeve | — | — | — |
| `sleeve_style` | `FULL` | Full sleeve | `PI_BLOUSE_FULL_SLEEVE` | ₹70.00 | — |
| `sleeve_style` | `PUFF` | Puff sleeve | `PI_BLOUSE_PUFF` | ₹90.00 | — |
| `closure` | `HOOK` | Hooks, back | — | — | — |
| `closure` | `HOOK_FRONT` | Hooks, front | — | — | — |
| `closure` | `ZIP_BACK` | Zip, back | `PI_BLOUSE_ZIP` | ₹70.00 | — |
| `closure` | `ZIP_SIDE` | Zip, side | `PI_BLOUSE_ZIP` | ₹70.00 | — |
| `closure` | `TIE_BACK` | Tie back (dori) | `PI_BLOUSE_TIE` | ₹50.00 | — |
| `lining` | `NONE` | No lining | — | — | — |
| `lining` | `FULL` | Full lining | `PI_BLOUSE_LINING_FULL` | ₹70.00 | — |
| `lining` | `KATORI_CUP` | Katori cup lining | `PI_BLOUSE_LINING_KATORI` | ₹90.00 | — |
| `padding` | `NONE` | No padding | — | — | — |
| `padding` | `LIGHT` | Light padding | `PI_BLOUSE_PADDING` | ₹80.00 | — |
| `padding` | `MOULDED_CUP` | Moulded cup | `PI_BLOUSE_PADDING_CUP` | ₹150.00 | — |
| `finish` | `PIPING` | Piping | `PI_BLOUSE_PIPING` | ₹40.00 | — |
| `finish` | `CONTRAST_BINDING` | Contrast binding | `PI_BLOUSE_BINDING` | ₹50.00 | — |
| `finish` | `LACE_EDGE` | Lace edging | `PI_BLOUSE_LACE` | ₹60.00 | — |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-01** | requires | `padding` in (`LIGHT`, `MOULDED_CUP`) requires `lining` ≠ `NONE` | Padding stitched against a single layer shows through and works loose. This is the rule named in the plan's #30 blueprint and exercised in walkthrough 1. |
| **DR-02** | requires | `front_neck = KATORI` requires `lining = KATORI_CUP` | A katori neck is defined by its cup seam; it cannot be cut unlined |
| **DR-03** | excludes | `front_neck = HIGH_NECK` excludes `back_neck = HIGH_NECK` | A blouse closed at both neck edges cannot be got into; one edge must open |
| **DR-04** | excludes | `sleeve_style = SLEEVELESS` excludes `finish = LACE_EDGE` | Seeded as a shop preference, not a physical law — listed here as the worked example of a rule the Owner may well delete at review (**OD-DES-04**) |
| **DR-05** | note | `closure` in (`ZIP_BACK`, `ZIP_SIDE`) → "Match the zip tape to the shell fabric; check the zip runs freely after lining." | Craft instruction, printed on the job card |
| **DR-06** | note | `padding = MOULDED_CUP` → "Confirm the cup size against the customer's reference garment before cutting." | Cup sizing is not in the measurement set |

Walkthrough 1 in [walkthroughs.md](./walkthroughs.md) selects `KATORI`, `ROUND_DEEP`, `THREE_QUARTER`, `HOOK`,
`KATORI_CUP` and `PIPING`; only the katori cup lining (₹90.00) and the piping (₹40.00) reach the bill, which is
why the taxable value is ₹580.00 against a ₹450.00 base rate.

### 9.2 `BLOUSE_AARI` — Blouse, Aari work

Inherits nothing from `BLOUSE_PATTERN`: the seven groups above are seeded again on this category, with the same
codes and the same meanings, plus four embroidery groups. Aari groups are branch-available only where an Aari
specialist works, and the embroidery groups are what the specialist phase is planned from.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `front_neck`, `back_neck`, `sleeve_style`, `closure`, `lining`, `padding`, `finish` | As Section 9.1 | As Section 9.1 | As Section 9.1 | As Section 9.1 |
| `aari_motif` | Aari motif | single | Yes | `design_aari_motif_v1` |
| `aari_density` | Work density | single | Yes | `design_aari_motif_v1` |
| `aari_stone` | Stone and bead type | multiple | Yes | `design_aari_motif_v1` |
| `aari_placement` | Work placement | multiple | Yes | `design_aari_placement_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `aari_motif` | `PEACOCK_MEDIUM` | Peacock, medium | — | — | — |
| `aari_motif` | `PEACOCK_LARGE` | Peacock, large | `PI_AARI_MOTIF_LARGE` | ₹600.00 | 2 |
| `aari_motif` | `FLORAL_VINE` | Floral vine | — | — | — |
| `aari_motif` | `TEMPLE_BORDER` | Temple border | — | — | — |
| `aari_motif` | `GEOMETRIC` | Geometric | — | — | — |
| `aari_motif` | `CUSTOM_REFERENCE` | To customer's reference image | `PI_AARI_MOTIF_CUSTOM` | ₹800.00 | 2 |
| `aari_density` | `LIGHT` | Light | `PI_AARI_DENSITY_LIGHT` | −₹400.00 | −2 |
| `aari_density` | `MEDIUM` | Medium | — | — | — |
| `aari_density` | `HEAVY` | Heavy (bridal) | `PI_AARI_DENSITY_HEAVY` | ₹900.00 | 3 |
| `aari_stone` | `AD_STONE` | AD stones | — | — | — |
| `aari_stone` | `KUNDAN` | Kundan | `PI_AARI_KUNDAN` | ₹450.00 | 1 |
| `aari_stone` | `BEADS` | Beads | — | — | — |
| `aari_stone` | `ZARI_THREAD` | Zari thread | — | — | — |
| `aari_stone` | `MIRROR` | Mirror work | `PI_AARI_MIRROR` | ₹350.00 | 1 |
| `aari_placement` | `FRONT` | Front panel | `PI_AARI_FRONT` | ₹2,200.00 | 3 |
| `aari_placement` | `BACK` | Back panel | `PI_AARI_BACK` | ₹1,800.00 | 3 |
| `aari_placement` | `NECK` | Neckline band | `PI_AARI_NECK` | ₹700.00 | 1 |
| `aari_placement` | `SLEEVE` | Sleeve bands, pair | `PI_AARI_SLEEVE` | ₹500.00 | 1 |
| `aari_placement` | `BORDER` | Hem border | `PI_AARI_BORDER` | ₹600.00 | 1 |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-07** | requires | `aari_placement` includes `SLEEVE` requires `sleeve_style` ≠ `SLEEVELESS` | There is no sleeve to embroider. This rule and the measurement rule on `aari_sleeve_work_length` are the same fact stated on both sides of the contract in Section 8 |
| **DR-08** | requires | `aari_motif = CUSTOM_REFERENCE` requires a reference image on the garment (#31) | The specialist cannot work from a code alone |
| **DR-09** | requires | `aari_density = HEAVY` requires `aari_stone` to include `AD_STONE` or `KUNDAN` | Thread-only work at bridal density does not hold its shape |
| **DR-10** | note | Any `aari_placement` selection → "Do not cut the embroidered panel closer than 15 mm to the worked edge." | Printed on the job card and read at cutting |
| **DR-11** | note | Any `aari_placement` selection → "Back the worked area so knots and stone settings do not sit against the skin." | Seeded as a note rather than as `requires lining = FULL`, because the specialist backs the panel as part of the work; whether it should instead force a lining selection is **OD-DES-04** |
| **DR-12** | note | `aari_density = HEAVY` → "Confirm the specialist's capacity before promising the due date." | Heavy work is the most common cause of a missed date on this category |

**Placement is the price.** The impacts sit on `aari_placement`, not on the motif or the density, because the
specialist is paid by the area worked. `MEDIUM` density and the common motifs are the ₹0 baseline, which is why
walkthrough 2 — `PEACOCK_MEDIUM`, `MEDIUM`, `AD_STONE`, placement `FRONT` + `NECK` + `SLEEVE` — bills exactly three
design lines (₹2,200.00 + ₹700.00 + ₹500.00) on a ₹600.00 base, and the AD stone kit appears separately as
inventory consumption at ₹340.00.

**Aari is a category, not an add-on.** Aari work is ordered as a `BLOUSE_AARI` garment job and never as an option on
a `BLOUSE_PATTERN` job — open decision **OD-CAT-03** in [category-hierarchy.md](./category-hierarchy.md). This is
why the embroidery groups are seeded on this category only.

### 9.3 `SALWAR` — Salwar

Six groups covering both pieces, because a salwar set is one garment job.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `kameez_neck` | Kameez neck | single | Yes | `design_salwar_neck_v1` |
| `sleeve_style` | Sleeve type and length | single | Yes | `design_blouse_sleeve_v1` |
| `kameez_slit` | Side slit | single | Yes | `design_salwar_neck_v1` |
| `bottom_style` | Bottom style | single | Yes | `design_salwar_bottom_v1` |
| `lining` | Lining | single | No | `design_salwar_neck_v1` |
| `dupatta_finish` | Dupatta finish | single | No | `design_lehenga_dupatta_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `kameez_neck` | `ROUND` | Round neck | — | — | — |
| `kameez_neck` | `V_NECK` | V neck | — | — | — |
| `kameez_neck` | `BOAT` | Boat neck | — | — | — |
| `kameez_neck` | `COLLAR` | Collar | `PI_SALWAR_COLLAR` | ₹90.00 | — |
| `kameez_neck` | `KEYHOLE` | Keyhole | — | — | — |
| `sleeve_style` | `SLEEVELESS`, `CAP`, `SHORT`, `ELBOW`, `THREE_QUARTER`, `FULL` | Codes and labels as Section 9.1, seeded here with no price or time impact | — | — | — |
| `kameez_slit` | `NONE` | No slit | — | — | — |
| `kameez_slit` | `SIDE_SHORT` | Short side slit | — | — | — |
| `kameez_slit` | `SIDE_HIGH` | High side slit | `PI_SALWAR_SLIT_HIGH` | ₹60.00 | — |
| `kameez_slit` | `FRONT_SLIT` | Front slit | `PI_SALWAR_SLIT_FRONT` | ₹80.00 | — |
| `bottom_style` | `SALWAR` | Salwar | — | — | — |
| `bottom_style` | `CHURIDAR` | Churidar | — | — | — |
| `bottom_style` | `PANT` | Pant | — | — | — |
| `bottom_style` | `PALAZZO` | Palazzo | `PI_SALWAR_PALAZZO` | ₹100.00 | — |
| `bottom_style` | `PATIALA` | Patiala | `PI_SALWAR_PATIALA` | ₹150.00 | 1 |
| `lining` | `NONE` | No lining | — | — | — |
| `lining` | `KAMEEZ_ONLY` | Kameez lined | `PI_SALWAR_LINING` | ₹110.00 | — |
| `lining` | `FULL` | Both pieces lined | `PI_SALWAR_LINING_FULL` | ₹190.00 | 1 |
| `dupatta_finish` | `NONE` | No dupatta | — | — | — |
| `dupatta_finish` | `HEM_ONLY` | Hemmed | — | — | — |
| `dupatta_finish` | `LACE_BORDER` | Lace border | `PI_SALWAR_DUPATTA_LACE` | ₹180.00 | 1 |
| `dupatta_finish` | `TASSELS` | Tassels | `PI_SALWAR_DUPATTA_TASSEL` | ₹120.00 | — |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-13** | requires | `kameez_slit = FRONT_SLIT` requires `lining` in (`KAMEEZ_ONLY`, `FULL`) | A front slit exposes the inside of the kameez |
| **DR-14** | excludes | `bottom_style = CHURIDAR` excludes `lining = FULL` | A churidar is cut to gather at the ankle and cannot take a second layer |
| **DR-15** | note | `bottom_style = PATIALA` → "Confirm the fabric length covers the pleats before cutting; a patiala takes about 0.5 m more." | The most frequent material shortage on this category |
| **DR-16** | note | `dupatta_finish` ≠ `NONE` → "The dupatta is finished with the set and delivered with it; it is not a separate job." | Distinguishes this from the Lehenga treatment in Section 9.4 |

Walkthrough 3 adds two garments to one draft and uses "duplicate garment" to copy the first garment's selections
into the second before changing `bottom_style` from `CHURIDAR` to `PALAZZO`. That is the only difference between the
two jobs and the only reason the second bills ₹880.00 against the first's ₹780.00.

### 9.4 `LEHENGA` — Lehenga

Six groups. The choli reuses the blouse groups; the skirt and dupatta groups are specific to this category.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `front_neck` | Choli front neck | single | Yes | `design_blouse_front_neck_v1` |
| `back_neck` | Choli back | single | Yes | `design_blouse_back_neck_v1` |
| `sleeve_style` | Choli sleeve | single | Yes | `design_blouse_sleeve_v1` |
| `skirt_style` | Skirt style | single | Yes | `design_lehenga_skirt_v1` |
| `waist_finish` | Skirt waist finish | single | Yes | `design_lehenga_skirt_v1` |
| `dupatta` | Dupatta | single | Yes | `design_lehenga_dupatta_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `front_neck`, `back_neck`, `sleeve_style` | As Section 9.1 | As Section 9.1 | As Section 9.1 | As Section 9.1 | — |
| `skirt_style` | `KALI` | Kali (panelled) | — | — | — |
| `skirt_style` | `CIRCULAR` | Circular | `PI_LEHENGA_CIRCULAR` | ₹700.00 | 2 |
| `skirt_style` | `A_LINE` | A-line | — | — | — |
| `skirt_style` | `MERMAID` | Mermaid | `PI_LEHENGA_MERMAID` | ₹900.00 | 2 |
| `skirt_style` | `LAYERED` | Layered with can-can | `PI_LEHENGA_LAYERED` | ₹1,200.00 | 3 |
| `waist_finish` | `HOOK_BAND` | Hook and band | — | — | — |
| `waist_finish` | `ZIP` | Concealed zip | `PI_LEHENGA_WAIST_ZIP` | ₹120.00 | — |
| `waist_finish` | `DRAWSTRING` | Drawstring (nada) | — | — | — |
| `waist_finish` | `ELASTIC_BACK` | Elastic at back only | — | — | — |
| `dupatta` | `NONE` | No dupatta | — | — | — |
| `dupatta` | `PLAIN` | Plain dupatta | — | — | — |
| `dupatta` | `BORDER_WORK` | Dupatta with border work | — | — | — |
| `dupatta` | `DOUBLE` | Two dupattas | — | — | — |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-17** | requires | `skirt_style = LAYERED` requires `waist_finish` in (`HOOK_BAND`, `ZIP`) | A drawstring will not hold the weight of a can-can layered skirt |
| **DR-18** | excludes | `skirt_style = MERMAID` excludes `waist_finish = ELASTIC_BACK` | A mermaid skirt is fitted through the hip and cannot gather at the waist |
| **DR-19** | note | `dupatta` ≠ `NONE` → "The dupatta is stitched as its own garment job on the same order, with a `deliver_together` dependency." | The reason this group carries no price impact — see below |
| **DR-20** | note | `skirt_style = KALI` → "Confirm the kali count with the Tailor Master against the flare before cutting." | `kali_count` is a measurement; the two must agree |

**Why the `dupatta` group is free.** On a lehenga the dupatta is a garment in its own right and is ordered as a
separate `LEHENGA.STITCHING` job — job `-03` at ₹900.00 in walkthrough 4 — with a `deliver_together` dependency
across the set. The group therefore records *whether and what kind*, which is what gates `dupatta_length` and
`dupatta_width` on `MT_LEHENGA` and what tells Reception to add the third job; it carries no impact of its own,
because the price is the job's. Walkthrough 4's three jobs (₹3,500.00 + ₹7,200.00 + ₹900.00 = ₹11,600.00
taxable) carry no design lines at all, which is exactly what the seeded rates above predict for `KALI`,
`HOOK_BAND` and a plain choli.

### 9.5 `GOWN` — Gown

Eight groups, the largest set: a gown is the category where the fewest customers arrive knowing what they want.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `silhouette` | Silhouette | single | Yes | `design_gown_silhouette_v1` |
| `neckline` | Neckline | single | Yes | `design_gown_neckline_v1` |
| `sleeve_style` | Sleeve type and length | single | Yes | `design_blouse_sleeve_v1` |
| `gown_slit` | Slit | single | Yes | `design_gown_silhouette_v1` |
| `trail` | Trail | single | Yes | `design_gown_silhouette_v1` |
| `closure` | Closure | single | Yes | `design_blouse_closure_v1` |
| `lining` | Lining | single | Yes | `design_gown_silhouette_v1` |
| `edge_finish` | Edge finish | multiple | No | `design_blouse_finish_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `silhouette` | `A_LINE` | A-line | — | — | — |
| `silhouette` | `FLARED` | Flared | — | — | — |
| `silhouette` | `FITTED` | Fitted | `PI_GOWN_FITTED` | ₹250.00 | 1 |
| `silhouette` | `MERMAID` | Mermaid | `PI_GOWN_MERMAID` | ₹450.00 | 2 |
| `silhouette` | `EMPIRE` | Empire line | — | — | — |
| `neckline` | `ROUND` | Round | — | — | — |
| `neckline` | `BOAT` | Boat | — | — | — |
| `neckline` | `V_NECK` | V neck | — | — | — |
| `neckline` | `SWEETHEART` | Sweetheart | — | — | — |
| `neckline` | `HALTER` | Halter | `PI_GOWN_HALTER` | ₹200.00 | — |
| `neckline` | `OFF_SHOULDER` | Off shoulder | `PI_GOWN_OFF_SHOULDER` | ₹300.00 | 1 |
| `sleeve_style` | `SLEEVELESS`, `CAP`, `SHORT`, `ELBOW`, `THREE_QUARTER`, `FULL` | Codes and labels as Section 9.1, seeded here with no price or time impact | — | — | — |
| `gown_slit` | `NONE` | No slit | — | — | — |
| `gown_slit` | `SIDE_LOW` | Low side slit | `PI_GOWN_SLIT_LOW` | ₹100.00 | — |
| `gown_slit` | `THIGH_HIGH` | Thigh-high slit | `PI_GOWN_SLIT_HIGH` | ₹150.00 | — |
| `gown_slit` | `FRONT_CENTRE` | Centre front slit | `PI_GOWN_SLIT_FRONT` | ₹150.00 | — |
| `trail` | `NONE` | No trail | — | — | — |
| `trail` | `SHORT_TRAIL` | Short trail | `PI_GOWN_TRAIL_SHORT` | ₹350.00 | — |
| `trail` | `LONG_TRAIL` | Long trail | `PI_GOWN_TRAIL_LONG` | ₹650.00 | 2 |
| `closure` | `CONCEALED_ZIP` | Concealed zip | `PI_GOWN_ZIP` | ₹100.00 | — |
| `closure` | `ZIP_BACK` | Exposed zip, back | — | — | — |
| `closure` | `LACE_UP` | Lace-up back | `PI_GOWN_LACE_UP` | ₹280.00 | 1 |
| `closure` | `HOOK` | Hooks | — | — | — |
| `lining` | `NONE` | No lining | — | — | — |
| `lining` | `BODICE_ONLY` | Bodice lined | `PI_GOWN_LINING_BODICE` | ₹180.00 | — |
| `lining` | `FULL` | Fully lined | `PI_GOWN_LINING_FULL` | ₹350.00 | — |
| `edge_finish` | `BOUND_EDGE` | Bound edge | `PI_GOWN_BOUND_EDGE` | ₹50.00 | — |
| `edge_finish` | `PIPING` | Piping | `PI_GOWN_PIPING` | ₹60.00 | — |
| `edge_finish` | `HORSEHAIR_HEM` | Horsehair braid hem | `PI_GOWN_HORSEHAIR` | ₹220.00 | 1 |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-21** | requires | `gown_slit` ≠ `NONE` requires `edge_finish` to include `BOUND_EDGE` | A raw slit edge frays on the first wear. This is the rule walkthrough 6 exercises: revising the gown to a thigh-high slit adds the bound edge automatically, and adds a QC criterion with it |
| **DR-22** | requires | `gown_slit` ≠ `NONE` requires `lining` ≠ `NONE` | An unlined slit shows the seam allowance |
| **DR-23** | requires | `trail` ≠ `NONE` requires `lining` in (`FULL`) | A trail drags on the floor and needs the second layer to hold its shape |
| **DR-24** | requires | `neckline = OFF_SHOULDER` requires `closure` in (`CONCEALED_ZIP`, `LACE_UP`) | The bodice must be held without shoulder support |
| **DR-25** | excludes | `neckline = HALTER` excludes `sleeve_style` other than `SLEEVELESS` | There is no shoulder to hang a sleeve from |
| **DR-26** | note | `trail = LONG_TRAIL` → "Measure the trail on the customer in the shoes she will wear." | Not in the measurement set; the most common gown remake |

Walkthrough 6 confirms `A_LINE`, `BOAT`, `FULL` sleeve, `FULL` lining, `SHORT_TRAIL`, `CONCEALED_ZIP` and slit
`NONE` — ₹350.00 + ₹350.00 + ₹100.00 of impacts on a ₹2,600.00 base, giving the ₹3,400.00 taxable value at
confirmation. The 16 June revision to `THIGH_HIGH` adds ₹150.00 and the rule-required `BOUND_EDGE` adds ₹50.00,
giving ₹3,600.00 — the ₹200.00 delta the customer sees and approves before the change is written, and the moment
`slit_height` stops being hidden on `MT_GOWN`.

### 9.6 `KIDS` — Kids

Five groups, deliberately the smallest set: a guardian with a restless child at the counter will not work through
twenty choices. Age-band guidance is attached as notes rather than as rules, because a band is a measurement
(`age_band` on `MT_KIDS`), not a design decision.

| Group code | Label | Selection | Required | Illustration sheet |
| --- | --- | --- | --- | --- |
| `garment_style` | Style | single | Yes | `design_kids_style_v1` |
| `sleeve_style` | Sleeve | single | Yes | `design_kids_style_v1` |
| `closure` | Closure | single | Yes | `design_kids_style_v1` |
| `lining` | Lining | single | No | `design_kids_style_v1` |
| `trim` | Trim | multiple | No | `design_kids_style_v1` |

| Group | Option code | Label | Price item | Rate | Days |
| --- | --- | --- | --- | --- | --- |
| `garment_style` | `A_LINE_FROCK` | A-line frock | — | — | — |
| `garment_style` | `A_LINE_FRILL` | A-line frock with frill | — | — | — |
| `garment_style` | `GATHERED_FROCK` | Gathered frock | — | — | — |
| `garment_style` | `PATTU_PAVADAI` | Pattu pavadai | `PI_KIDS_PAVADAI` | ₹120.00 | 1 |
| `garment_style` | `KIDS_SALWAR` | Kids salwar set | `PI_KIDS_SALWAR` | ₹100.00 | — |
| `garment_style` | `KIDS_GOWN` | Kids gown | `PI_KIDS_GOWN` | ₹200.00 | 1 |
| `sleeve_style` | `SLEEVELESS` | Sleeveless | — | — | — |
| `sleeve_style` | `CAP` | Cap sleeve | — | — | — |
| `sleeve_style` | `SHORT` | Short sleeve | — | — | — |
| `sleeve_style` | `PUFF` | Puff sleeve | — | — | — |
| `sleeve_style` | `FULL` | Full sleeve | — | — | — |
| `closure` | `BACK_BUTTON` | Buttons, back | — | — | — |
| `closure` | `BACK_ZIP` | Zip, back | `PI_KIDS_ZIP` | ₹40.00 | — |
| `closure` | `TIE_BACK` | Tie back | — | — | — |
| `closure` | `ELASTIC` | Elastic, no opening | — | — | — |
| `lining` | `NONE` | No lining | — | — | — |
| `lining` | `FULL` | Full lining | `PI_KIDS_LINING` | ₹30.00 | — |
| `trim` | `CONTRAST_FRILL` | Contrast frill | `PI_KIDS_FRILL` | ₹60.00 | — |
| `trim` | `LACE` | Lace trim | `PI_KIDS_LACE` | ₹40.00 | — |
| `trim` | `BOW` | Bow | — | — | — |
| `trim` | `POCKETS` | Pockets | `PI_KIDS_POCKETS` | ₹30.00 | — |

**Rules**

| Id | Type | Statement | Why |
| --- | --- | --- | --- |
| **DR-27** | requires | `garment_style = PATTU_PAVADAI` requires `lining = FULL` | Silk against a child's skin is lined as a matter of course |
| **DR-28** | excludes | `closure = ELASTIC` excludes `garment_style` in (`PATTU_PAVADAI`, `KIDS_GOWN`) | Neither garment can be pulled on over the head |
| **DR-29** | note | Every option → "Apply the published `KIDS` growth allowance at cutting; do not add it to the measurement." | The ease convention of [measurement-templates.md](./measurement-templates.md) Section 5, restated where the cutter reads it |
| **DR-30** | note | `trim = BOW` → "Stitch the bow down; a tied bow on a young child's garment is a safety hazard." | Safety instruction, always printed |

Walkthrough 7's two jobs take `A_LINE_FRILL`, `PUFF`, `BACK_BUTTON` and `FULL` lining — one ₹30.00 lining impact
per garment, the ₹60.00 line on the bill. The 20 April design revision adds `CONTRAST_FRILL` to job `-01` for
₹60.00 after the material is found short, which is why the taxable value moves from ₹540.00 to ₹600.00.

---

## 10. Publish-time validation

Registered by #30 as an `ICatalogDependencyValidator` and run by the catalogue publish command; a publish is
refused while any error remains. Errors are returned as field-level problem details. An error about a rule names
that rule's `DR-nn` identifier, because several of the checks below — a cycle, an unsatisfiable required group —
involve two rules at once, and an administrator has to be told which pair to change rather than which options are
implicated.

| Check | Severity | Owning issue |
| --- | --- | --- |
| Duplicate group code within a category, or duplicate option code within a group | Error | #30 |
| A rule referencing an unknown group or option | Error | #30, and listed in [category-hierarchy.md](./category-hierarchy.md) Section 10 |
| A rule crossing categories | Error | #30 |
| A `requires` cycle, or a rule whose two sides are the same option | Error | #30 |
| A rule that makes a required group unsatisfiable | Error | #30 |
| A required group with no available option at a branch in the category's availability set | Error | #30 |
| A group's branch availability not a subset of its category's | Error | #29 |
| A code changed on an already-published group or option | Error | #29 |
| An option missing `illustration_alt` | Error | #30, [../nfr/accessibility-localisation.md](../nfr/accessibility-localisation.md) |
| A price-list item code absent from the branch's published price-list version | Error | #41 |
| A measurement-template rule reading a `design.<group_code>` absent from the template's category | Error | #27 with #30 — the pair in Section 8 |
| An option with no illustration (bundled or uploaded) | Warning; the picker falls back to label plus alt text | #30 |
| A group with more than twelve options | Warning; the tablet picker becomes a scrolling list | #30 |
| Retiring a group still referenced by a service type of a published catalog version | Error | #29 |

---

## 11. Open decisions

Recorded here and tracked centrally in [assumptions-and-open-decisions.md](./assumptions-and-open-decisions.md);
all of them resolve under **OD-10**. Section references are to
[../IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md).

| ID | Question | Proposed default (not yet agreed) | Owner | Raised | Needed by |
| --- | --- | --- | --- | --- | --- |
| OD-DES-01 | Are the groups and options in Section 9 the choices this shop actually offers, and is anything missing that a customer asks for weekly? | The sets above, reviewed group by group against the counter's current practice. | Owner, with the Tailor Master and Reception | 2026-09-04 | #30 (wave 2). Tracked as Section 11 item 10. |
| OD-DES-02 | Does a design option carry a **literal amount**, or a price-list item code resolved by #41 at pricing time? | The price-list item code, as in Section 5, so money lives in one place, per branch and effective-dated, and the catalogue holds no amount. The plan's #30 model says "price impact" without settling which. | Technical reviewer, with the Owner and the accountant | 2026-09-04 | #30 and #41 (wave 2 design, wave 3 pricing). Related to **OD-05**. |
| OD-DES-03 | Are the proposed rates and day impacts in Section 9 right, and are the day impacts additive per option or capped per job? | The rates above; day impacts additive, with the proposed due date always editable by Reception. | Owner, with the Tailor Master | 2026-09-04 | #30 and #41 (wave 3). Related to **OD-CAT-05**. |
| OD-DES-04 | Which seeded rules are real craft constraints and which are shop preferences that should not block a confirmation? | Everything in Section 9 is blocking except the sleeveless/lace-edge exclusion in 9.1, which is flagged for deletion at review. | Owner, with the Tailor Master | 2026-09-04 | #30 (wave 2) |
| OD-DES-05 | Should `BLOUSE_AARI` reuse the seven `BLOUSE_PATTERN` groups by reference rather than by copy, so a change to a blouse neckline reaches both categories at once? | Copy at seed, as Section 9.2 describes. Shared groups would couple two categories that the shop may want to price and offer separately. Revisit if the duplication proves a maintenance burden. | Technical reviewer, with the Owner | 2026-09-04 | #30 (wave 2) |
| OD-DES-06 | May Reception add a free-text design instruction that is not any option, and does it price? | Yes, as a garment note carried into the snapshot and printed on the job card; it never prices and never affects the due date. Anything chargeable must become an option. | Owner | 2026-09-04 | #30 with #32a (wave 3) |
| OD-DES-07 | Who draws the fourteen illustration sheets, and are photographs of past work acceptable instead of line drawings? | Line drawings commissioned before #30; bundled with the application until media upload (#31) exists. Photographs of customers' garments may not be used without a consent record. | Owner | 2026-09-04 | #30 (wave 2). Same question as **OD-MEA-09** for the measurement diagrams. |
| OD-DES-08 | What are the Tamil labels for every group and option, and does the picker show Tamil beside English? | Tamil labels supplied with the Tamil glossary; the picker shows English with Tamil beneath, as the measurement sheet does. | Owner, with Reception staff | 2026-09-04 | [../nfr/accessibility-localisation.md](../nfr/accessibility-localisation.md) (#19), before the `ta-IN` catalogue reaches 95% |

---

## 12. Related documents

| Document | Why it matters here |
| --- | --- |
| [category-hierarchy.md](./category-hierarchy.md) | The categories and service types these groups are linked from; link 3, the code conventions and the publish-validation summary |
| [measurement-templates.md](./measurement-templates.md) | The conditional rules that read `design.sleeve_style`, `design.aari_placement`, `design.kameez_slit`, `design.gown_slit` and `design.dupatta` — the contract in Section 8 |
| [glossary.md](./glossary.md) | Definitions of design option group, design option, design revision, design snapshot and every role used above |
| [workflows/](./workflows/) | Where design selection sits in each category's flow, and the specialist phase the Aari groups plan |
| [state-transitions.md](./state-transitions.md) | The estimate and confirmation preconditions that require valid selections, and the design-revision transition with its actor and audit event |
| [configurable-vs-fixed.md](./configurable-vs-fixed.md) | What an administrator may change in a group, an option or a rule without a deployment |
| [walkthroughs.md](./walkthroughs.md) | The selections, impacts and revisions in Section 9 as they appear end to end, with the bills they produce |
| [exceptions.md](./exceptions.md) | The material-shortage and pre-production change paths that end in a design revision |
| [raci.md](./raci.md) | Who drafts, reviews and approves design catalogue changes |
| [assumptions-and-open-decisions.md](./assumptions-and-open-decisions.md) | Central register; every decision above resolves under **OD-10** |
| [../nfr/accessibility-localisation.md](../nfr/accessibility-localisation.md) | Alt-text requirements for illustrations; the Tamil glossary for design terms |
| [../IMPLEMENTATION_PLAN.md](../IMPLEMENTATION_PLAN.md) | Decisions D8, D9, D21; blueprints for #29, #30, #31, #32a, #41; Section 11 item 10 |
