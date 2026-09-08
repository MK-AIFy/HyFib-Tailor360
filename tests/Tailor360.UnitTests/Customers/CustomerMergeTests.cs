using Shouldly;
using Tailor360.Modules.Customers.Domain.Customers;
using Tailor360.Modules.Customers.Domain.Deduplication;
using Tailor360.Modules.Customers.Domain.Naming;

namespace Tailor360.UnitTests.Customers;

/// <summary>
/// The invariants of an irreversible merge: what it does to both records, what it refuses, and what
/// the evidence it leaves behind is obliged to say.
/// </summary>
/// <remarks>
/// The rule these all serve is <c>docs/prd/exceptions.md</c> EX-01: a merge is authorised, it names one
/// surviving record, it keeps the merged number searchable, and there is no un-merge. Every refusal
/// below is a way of not ending up somewhere that rule cannot be undone from.
/// </remarks>
[Trait("Category", "Unit")]
public sealed class CustomerMergeTests
{
    private static readonly Guid MergeId = CustomersTestData.Id("merge");
    private static readonly Guid EventId = CustomersTestData.Id("event");

    [Fact]
    public void AbsorbingPointsTheMergedRecordAtTheSurvivorAndWithdrawsIt()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");
        var later = CustomersTestData.Now.AddDays(3);

        var absorbed = survivor.Absorb(merged, new CountingIds(), later, CustomersTestData.Actor);

        absorbed.IsSuccess.ShouldBeTrue();

        merged.MergedIntoCustomerId.ShouldBe(survivor.Id);
        merged.MergedAt.ShouldBe(later);
        merged.Status.ShouldBe(CustomerStatus.Deactivated);
        merged.DeactivatedAt.ShouldBe(later);
        merged.IsMerged.ShouldBeTrue();

        // The survivor is untouched as a record: it is still active, still itself, and carries no
        // pointer of its own.
        survivor.Status.ShouldBe(CustomerStatus.Active);
        survivor.MergedIntoCustomerId.ShouldBeNull();
        survivor.MergedAt.ShouldBeNull();
        survivor.IsMerged.ShouldBeFalse();
    }

    [Fact]
    public void TheMergedNumberStaysSearchableAgainstTheSurvivor()
    {
        // EX-01: "the merged number survives as an alias". The normalised value is the key the search
        // folds a typed customer number down to, so this is what makes the promise true rather than
        // merely recorded.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        survivor.Absorb(merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var alias = survivor.Aliases.ShouldHaveSingleItem();

        alias.Kind.ShouldBe(CustomerAliasKind.MergedCustomerNumber);
        alias.Value.ShouldBe("C-MDU01-000917");
        alias.NormalisedValue.ShouldBe(CustomerNameNormaliser.Normalise("C-MDU01-000917"));
    }

    [Fact]
    public void ADifferentNameOnTheMergedRecordIsKeptAsAPreviousName()
    {
        // The two records are one person written two ways, which is the commonest cause of the
        // duplicate in the first place. Losing the other spelling would lose the search that finds her.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Ramanathan", "90000 21174", "C-MDU01-000917");

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.Value.AliasesRecorded.ShouldBe(2);

        var name = survivor.Aliases.Single(alias => alias.Kind is CustomerAliasKind.PreviousName);

        name.Value.ShouldBe("Kavitha Ramanathan");
        name.NormalisedValue.ShouldBe(merged.NormalisedName);
    }

    [Fact]
    public void TwoRecordsUnderOneNameRecordOneAliasAndNotTwo()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.Value.AliasesRecorded.ShouldBe(1);
        survivor.Aliases.ShouldAllBe(alias => alias.Kind == CustomerAliasKind.MergedCustomerNumber);
    }

    [Fact]
    public void EveryBranchThatCouldSeeTheMergedRecordCanSeeTheSurvivor()
    {
        // Otherwise the merge would take the customer off a branch's screen altogether, which is a
        // worse outcome than the duplicate it was performed to end.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered(
            "Kavitha Raman", "90000 21174", "C-MDU01-000917",
            owningBranchId: CustomersTestData.OtherBranch);

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.Value.VisibilityBranchesAdded.ShouldBe(1);
        survivor.IsVisibleTo(CustomersTestData.OtherBranch).ShouldBeTrue();
        survivor.IsVisibleTo(CustomersTestData.Branch).ShouldBeTrue();
    }

    [Fact]
    public void ABranchThatAlreadySawBothRecordsIsNotCountedAsAdded()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000002");

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.Value.VisibilityBranchesAdded.ShouldBe(0);
        survivor.Visibility.Count.ShouldBe(1);
    }

    [Fact]
    public void ARecordCannotBeMergedIntoItself()
    {
        var customer = CustomersTestData.Registered();

        var absorbed = customer.Absorb(
            customer, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsFailure.ShouldBeTrue();
        absorbed.Error.Code.ShouldBe("customers.cannot-merge-into-itself");
    }

    [Fact]
    public void TwoRecordsInDifferentOrganisationsAreNotTheSamePerson()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered(
            "Kavitha Raman", "90000 21174", "C-XXX01-000001",
            organisationId: CustomersTestData.Id("another-organisation"));

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsFailure.ShouldBeTrue();
        absorbed.Error.Code.ShouldBe("customers.cannot-merge-across-organisations");
    }

    [Fact]
    public void ARecordThatHasBeenMergedCannotAbsorbAnother()
    {
        // Following the chain would fold a record into one the person at the screen never chose, so
        // the answer is to open the record that survived and merge into that.
        var first = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var second = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");
        var third = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-TRY01-000004");

        first.Absorb(second, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var absorbed = second.Absorb(
            third, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsFailure.ShouldBeTrue();
        absorbed.Error.Code.ShouldBe("customers.merge-target-is-merged");
    }

    [Fact]
    public void ARecordThatHasBeenMergedCannotBeMergedAgain()
    {
        var first = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var second = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");
        var third = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-TRY01-000004");

        first.Absorb(second, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var absorbed = third.Absorb(
            second, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsFailure.ShouldBeTrue();
        absorbed.Error.Code.ShouldBe("customers.already-merged");
    }

    [Fact]
    public void AWithdrawnRecordCannotBeChosenAsTheSurvivor()
    {
        // Almost always the two records picked the wrong way round. The remedy is two deliberate steps
        // — reactivate, then merge — rather than one silent guess.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        survivor.Deactivate(CustomersTestData.Now, CustomersTestData.Actor).IsSuccess.ShouldBeTrue();

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsFailure.ShouldBeTrue();
        absorbed.Error.Code.ShouldBe("customers.status-transition-not-allowed");
    }

    [Fact]
    public void TheSurvivorTakesOverEveryAliasTheFoldedRecordWasCarrying()
    {
        // A merged into B, then B merged into C. A's number lives on B as an alias, and a search only
        // matches aliases on records it can see — B is deactivated, so it is not one of them. Without
        // carrying them over, the second merge would silently take A's number out of search and the
        // promise EX-01 makes about an old receipt would hold for one merge and not for two.
        var a = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var b = CustomersTestData.Registered("Kavitha Ramanathan", "90000 21174", "C-MDU01-000917");
        var c = CustomersTestData.Registered("Kavitha R", "90000 21174", "C-TRY01-000004");

        b.Absorb(a, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var second = c.Absorb(b, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        second.IsSuccess.ShouldBeTrue();

        var numbers = c.Aliases
            .Where(alias => alias.Kind is CustomerAliasKind.MergedCustomerNumber)
            .Select(alias => alias.Value)
            .ToList();

        numbers.ShouldBe(["C-MDU01-000917", "C-CBE01-000001"], ignoreOrder: true);

        // Both names too: B's own, and A's, which B was holding.
        c.Aliases
            .Where(alias => alias.Kind is CustomerAliasKind.PreviousName)
            .Select(alias => alias.Value)
            .ShouldBe(["Kavitha Ramanathan", "Kavitha Raman"], ignoreOrder: true);

        // Two of its own plus two carried.
        second.Value.AliasesRecorded.ShouldBe(4);
    }

    [Fact]
    public void AnAliasTheSurvivorAlreadyHoldsIsNotRecordedTwice()
    {
        // Two records can arrive at the same previous name — she was written under it at both
        // counters — and a second row saying so would only make the record's history harder to read.
        var survivor = CustomersTestData.Registered("Kavitha Sundaram", "90000 21174", "C-CBE01-000001");
        var first = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");
        var second = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-TRY01-000004");

        survivor.Absorb(first, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var absorbed = survivor.Absorb(
            second, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        absorbed.IsSuccess.ShouldBeTrue();

        survivor.Aliases
            .Count(alias => alias.Kind is CustomerAliasKind.PreviousName
                && alias.Value == "Kavitha Raman")
            .ShouldBe(1);

        // Only the second record's number: its previous name was one the survivor already held.
        absorbed.Value.AliasesRecorded.ShouldBe(1);
    }

    [Fact]
    public void AMergedRecordIsNeverReturnedToOrdinaryUse()
    {
        // Reactivating it would put two records for one person back in search, which is the state the
        // merge existed to end.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        survivor.Absorb(merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var reactivated = merged.Reactivate(CustomersTestData.Now, CustomersTestData.Actor);

        reactivated.IsFailure.ShouldBeTrue();
        reactivated.Error.Code.ShouldBe("customers.already-merged");
        merged.Status.ShouldBe(CustomerStatus.Deactivated);
    }

    [Fact]
    public void AMergedRecordIsNeverCorrected()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        survivor.Absorb(merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        var corrected = merged.Correct(
            CustomersTestData.Details("Kavitha Raman", "90000 21175"),
            CustomersTestData.Now,
            CustomersTestData.Actor,
            CustomersTestData.Id("correction"));

        corrected.IsFailure.ShouldBeTrue();
        corrected.Error.Code.ShouldBe("customers.status-transition-not-allowed");
    }

    [Fact]
    public void MergingARecordThatWasAlreadyWithdrawnKeepsTheDateItWasWithdrawnOn()
    {
        // When it stopped being offered is a different fact from when it was folded in, and the merge
        // did not change the first of them.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");
        var withdrawn = CustomersTestData.Now;
        var mergedAt = CustomersTestData.Now.AddMonths(2);

        merged.Deactivate(withdrawn, CustomersTestData.Actor).IsSuccess.ShouldBeTrue();

        survivor.Absorb(merged, new CountingIds(), mergedAt, CustomersTestData.Actor)
            .IsSuccess.ShouldBeTrue();

        merged.DeactivatedAt.ShouldBe(withdrawn);
        merged.MergedAt.ShouldBe(mergedAt);
    }

    [Fact]
    public void TheMergeRecordSaysWhatHappenedRatherThanWhatWasAskedFor()
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered(
            "Kavitha Ramanathan", "90000 21174", "C-MDU01-000917",
            owningBranchId: CustomersTestData.OtherBranch);

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        var record = CustomerMerge.Record(
            MergeId,
            CustomersTestData.Organisation,
            survivor,
            merged,
            "  Same phone and she confirmed it at the counter.  ",
            CustomersTestData.Branch,
            absorbed.Value,
            EventId,
            CustomersTestData.Now,
            CustomersTestData.Actor);

        record.IsSuccess.ShouldBeTrue();
        record.Value.SurvivorCustomerId.ShouldBe(survivor.Id);
        record.Value.MergedCustomerId.ShouldBe(merged.Id);
        record.Value.MergedCustomerNumber.ShouldBe("C-MDU01-000917");
        record.Value.NumberAliasId.ShouldBe(
            survivor.Aliases.Single(alias => alias.Kind is CustomerAliasKind.MergedCustomerNumber).Id);
        record.Value.AliasesRecorded.ShouldBe(2);
        record.Value.VisibilityBranchesAdded.ShouldBe(1);
        record.Value.EventId.ShouldBe(EventId);
        record.Value.Reason.ShouldBe("Same phone and she confirmed it at the counter.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMergeIsNeverRecordedWithoutAReason(string? reason)
    {
        var record = RecordWith(reason);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe("customers.reason-required");
    }

    [Fact]
    public void AReasonLongerThanTheColumnIsRefusedRatherThanTruncated()
    {
        var record = RecordWith(new string('x', CustomerMerge.MaximumReasonLength + 1));

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe("customers.value-too-long");
    }

    [Fact]
    public void AMergeIsNeverRecordedWithoutTheEventThatAnnouncesIt()
    {
        // Every other module learns of the merge from that event. A record without one would describe
        // a merge nobody outside Customers was ever told about.
        var record = RecordWith("Same person.", eventId: Guid.Empty);

        record.IsFailure.ShouldBeTrue();
        record.Error.Code.ShouldBe("customers.value-required");
    }

    [Fact]
    public void CreatingANewRecordAnywayIsEvidenceWithNoMergeBehindIt()
    {
        var decision = DuplicateCandidateDecision.CreatedNewAnyway(
            CustomersTestData.Id("decision"),
            CustomersTestData.Organisation,
            CustomersTestData.Id("subject"),
            CustomersTestData.Id("candidate"),
            new DuplicateMatch(
                DuplicateConfidence.Medium,
                [DuplicateReason.SameFoldedName, DuplicateReason.SameLocality]),
            CustomersTestData.Branch,
            CustomersTestData.Now,
            CustomersTestData.Actor);

        decision.IsSuccess.ShouldBeTrue();
        decision.Value.Decision.ShouldBe(DuplicateDecision.CreatedNewAnyway);
        decision.Value.MergeId.ShouldBeNull();
        decision.Value.Confidence.ShouldBe(DuplicateConfidence.Medium);
        decision.Value.Reasons.ShouldBe(
            [DuplicateReason.SameFoldedName, DuplicateReason.SameLocality]);
    }

    [Fact]
    public void AMergeDecisionWithoutAMergeToPointAtIsRefused()
    {
        var decision = DuplicateCandidateDecision.Merged(
            CustomersTestData.Id("decision"),
            CustomersTestData.Organisation,
            CustomersTestData.Id("subject"),
            CustomersTestData.Id("candidate"),
            new DuplicateMatch(DuplicateConfidence.High, [DuplicateReason.SharedTelephoneNumber]),
            Guid.Empty,
            CustomersTestData.Branch,
            CustomersTestData.Now,
            CustomersTestData.Actor);

        decision.IsFailure.ShouldBeTrue();
        decision.Error.Code.ShouldBe("customers.value-required");
    }

    [Fact]
    public void ARecordIsNeverItsOwnDuplicateCandidate()
    {
        // Stored, it would make the pair look reviewed for ever, and it can only come from a defect in
        // whatever raised it.
        var same = CustomersTestData.Id("subject");

        var decision = DuplicateCandidateDecision.CreatedNewAnyway(
            CustomersTestData.Id("decision"),
            CustomersTestData.Organisation,
            same,
            same,
            new DuplicateMatch(DuplicateConfidence.High, [DuplicateReason.SharedTelephoneNumber]),
            CustomersTestData.Branch,
            CustomersTestData.Now,
            CustomersTestData.Actor);

        decision.IsFailure.ShouldBeTrue();
        decision.Error.Code.ShouldBe("customers.cannot-merge-into-itself");
    }

    [Fact]
    public void AMergeRecordNeedsItsOwnIdentifierAndAnOrganisation()
    {
        // Both are supplied by the handler from IIdGenerator and the caller's session, so a failure
        // here is a wiring defect — which is exactly the kind that is easiest to ship.
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        Record(Guid.Empty, CustomersTestData.Organisation).Error.Code.ShouldBe("customers.value-required");
        Record(MergeId, Guid.Empty).Error.Code.ShouldBe("customers.value-required");

        Tailor360.Platform.Abstractions.Results.Result<CustomerMerge> Record(Guid id, Guid organisationId)
            => CustomerMerge.Record(
                id,
                organisationId,
                survivor,
                merged,
                "Same person.",
                CustomersTestData.Branch,
                absorbed.Value,
                EventId,
                CustomersTestData.Now,
                CustomersTestData.Actor);
    }

    [Theory]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    public void ADuplicateDecisionNamesItselfItsOrganisationAndBothRecords(
        bool hasId,
        bool hasOrganisation,
        bool hasSubject)
    {
        var decision = DuplicateCandidateDecision.CreatedNewAnyway(
            hasId ? CustomersTestData.Id("decision") : Guid.Empty,
            hasOrganisation ? CustomersTestData.Organisation : Guid.Empty,
            hasSubject ? CustomersTestData.Id("subject") : Guid.Empty,
            CustomersTestData.Id("candidate"),
            new DuplicateMatch(DuplicateConfidence.High, [DuplicateReason.SharedTelephoneNumber]),
            CustomersTestData.Branch,
            CustomersTestData.Now,
            CustomersTestData.Actor);

        decision.IsFailure.ShouldBeTrue();
        decision.Error.Code.ShouldBe("customers.value-required");
    }

    private static Tailor360.Platform.Abstractions.Results.Result<CustomerMerge> RecordWith(
        string? reason,
        Guid? eventId = null)
    {
        var survivor = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-CBE01-000001");
        var merged = CustomersTestData.Registered("Kavitha Raman", "90000 21174", "C-MDU01-000917");

        var absorbed = survivor.Absorb(
            merged, new CountingIds(), CustomersTestData.Now, CustomersTestData.Actor);

        return CustomerMerge.Record(
            MergeId,
            CustomersTestData.Organisation,
            survivor,
            merged,
            reason,
            CustomersTestData.Branch,
            absorbed.Value,
            eventId ?? EventId,
            CustomersTestData.Now,
            CustomersTestData.Actor);
    }
}
