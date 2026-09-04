# Feature flags

Flags exist so that an unfinished feature can be merged without being live, and so that a feature can
be turned off during business hours without a deployment.

## Ownership

The Platform module owns flags: the store, evaluation and the evaluation audit. The administration
screens in Identity call the platform contract; they do not read the flag tables. One owner is what
stops two modules from keeping two stores that disagree.

## Evaluation

```csharp
if (await featureFlags.IsEnabledAsync("orders.doorstep_collection", context, cancellationToken))
```

- An **unknown flag is off.** A feature nobody has configured must not be live.
- A **branch value overrides the organisation value** for that branch only. A branch with no value of
  its own follows the organisation.
- Every change increments the row's version, so an evaluation can record exactly which value it used.

## Propagation bound

Evaluation reads a cached snapshot, refreshed when it is older than `FeatureFlags:PropagationBound`
(30 seconds by default). The guarantee is therefore stated rather than vague:

- On the node that made the change: **immediately**, because the mutation invalidates its own cache.
- On every other node: **within the propagation bound**.

Querying the database on every evaluation would put a query on every code path that asks whether a
feature is on, which is the wrong trade for a value that changes a few times a month.

## Changing a value

```bash
dotnet run --project src/Tools/Tailor360.Cli -- flags list
dotnet run --project src/Tools/Tailor360.Cli -- flags set orders.doorstep_collection true \
    --reason "Pilot at the main branch from Monday"
dotnet run --project src/Tools/Tailor360.Cli -- flags set orders.doorstep_collection false \
    --scope branch --branch <guid> --reason "Second branch is not trained yet"
```

A reason is required and the change is audited, because turning a feature on or off is an operational
act with user-visible consequences. Issue #25 exposes the same operation over HTTP behind the
`admin.feature_flags` permission, which requires multi-factor authentication and a reason.

## Naming and lifecycle

- Keys are `<module>.<feature>`, lower case with underscores.
- A flag is temporary. Once a feature is on everywhere and has stayed on, remove the flag and the dead
  branch; a flag that has been on for a year is not a switch, it is a comment.
- A flag guarding an incomplete feature defaults to off in every environment including development, so
  that "it worked on my machine" cannot mean "the flag was on there".
