using Audit.Contracts;
using Audit.Domain;

namespace Audit.Domain.Tests;

public class AuditEntryTests
{
    private static readonly Guid Id = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");
    private static readonly Guid Actor = Guid.Parse("0199f0a0-0000-7000-8000-00000000a002");
    private static readonly Guid Target = Guid.Parse("0199f0a0-0000-7000-8000-00000000a003");
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 9, 30, 0, TimeSpan.Zero);

    private static AuditRecord Record(
        string action = "Identity.Account.Blocked",
        string entityType = "UserAccount",
        string? reason = null,
        IReadOnlyList<AuditChange>? changes = null) =>
        new(action, entityType, Target, Actor, Reason: reason, Changes: changes);

    [Fact]
    public void Keeps_everything_it_was_given()
    {
        var agency = Guid.Parse("0199f0a0-0000-7000-8000-00000000a004");
        var record = new AuditRecord(
            "Identity.Account.RoleChanged",
            "UserAccount",
            Target,
            Actor,
            agency,
            "Promotion",
            [new AuditChange("role", "Agent", "Cashier")]);

        var entry = AuditEntry.Create(Id, Now, record, "203.0.113.7", "Mozilla/5.0 (test)").Value;

        Assert.Equal(Id, entry.Id);
        Assert.Equal(Now, entry.OccurredAt);
        Assert.Equal("Identity.Account.RoleChanged", entry.Action);
        Assert.Equal("UserAccount", entry.EntityType);
        Assert.Equal(Target, entry.EntityId);
        Assert.Equal(Actor, entry.ActorId);
        Assert.Equal(agency, entry.AgencyId);
        Assert.Equal("Promotion", entry.Reason);
        Assert.Equal([new AuditChange("role", "Agent", "Cashier")], entry.Changes);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("Mozilla/5.0 (test)", entry.UserAgent);
    }

    [Fact]
    public void System_action_has_no_actor_and_no_request()
    {
        var record = new AuditRecord("Organization.Agency.Seeded", "Agency", Target, ActorId: null);

        var entry = AuditEntry.Create(Id, Now, record, ipAddress: null, userAgent: null).Value;

        Assert.Null(entry.ActorId);
        Assert.Null(entry.IpAddress);
        Assert.Null(entry.UserAgent);
        Assert.Empty(entry.Changes);
    }

    [Fact]
    public void Texts_are_trimmed_and_an_empty_reason_becomes_null()
    {
        var entry = AuditEntry.Create(
            Id, Now, Record(action: "  Identity.Account.Blocked ", entityType: " UserAccount ", reason: "   "), " ", "").Value;

        Assert.Equal("Identity.Account.Blocked", entry.Action);
        Assert.Equal("UserAccount", entry.EntityType);
        Assert.Null(entry.Reason);
        Assert.Null(entry.IpAddress);
        Assert.Null(entry.UserAgent);
    }

    [Theory]
    [InlineData("", "Audit.Entry.ActionEmpty")]
    [InlineData("   ", "Audit.Entry.ActionEmpty")]
    public void Action_is_required(string action, string code)
    {
        var result = AuditEntry.Create(Id, Now, Record(action: action), null, null);

        Assert.Equal(code, result.Error.Code);
    }

    [Fact]
    public void Action_is_at_most_100_characters()
    {
        Assert.True(AuditEntry.Create(Id, Now, Record(action: new string('a', 100)), null, null).IsSuccess);
        Assert.Equal(
            "Audit.Entry.ActionTooLong",
            AuditEntry.Create(Id, Now, Record(action: new string('a', 101)), null, null).Error.Code);
    }

    [Fact]
    public void Entity_type_is_required_and_at_most_100_characters()
    {
        Assert.Equal("Audit.Entry.EntityTypeEmpty", AuditEntry.Create(Id, Now, Record(entityType: " "), null, null).Error.Code);
        Assert.Equal(
            "Audit.Entry.EntityTypeTooLong",
            AuditEntry.Create(Id, Now, Record(entityType: new string('e', 101)), null, null).Error.Code);
    }

    [Fact]
    public void Reason_is_at_most_500_characters()
    {
        Assert.True(AuditEntry.Create(Id, Now, Record(reason: new string('r', 500)), null, null).IsSuccess);
        Assert.Equal(
            "Audit.Entry.ReasonTooLong",
            AuditEntry.Create(Id, Now, Record(reason: new string('r', 501)), null, null).Error.Code);
    }

    [Fact]
    public void A_change_names_its_field()
    {
        var result = AuditEntry.Create(Id, Now, Record(changes: [new AuditChange(" ", "a", "b")]), null, null);

        Assert.Equal("Audit.Change.FieldEmpty", result.Error.Code);
    }

    [Fact]
    public void A_field_name_is_at_most_100_characters()
    {
        var result = AuditEntry.Create(
            Id, Now, Record(changes: [new AuditChange(new string('f', 101), "a", "b")]), null, null);

        Assert.Equal("Audit.Change.FieldTooLong", result.Error.Code);
    }

    [Fact]
    public void A_field_appears_only_once()
    {
        var result = AuditEntry.Create(
            Id, Now, Record(changes: [new AuditChange("name", "a", "b"), new AuditChange(" name", "b", "c")]), null, null);

        Assert.Equal("Audit.Change.FieldDuplicate", result.Error.Code);
    }

    [Fact]
    public void A_value_is_at_most_4000_characters()
    {
        Assert.True(AuditEntry.Create(
            Id, Now, Record(changes: [new AuditChange("note", null, new string('v', 4000))]), null, null).IsSuccess);
        Assert.Equal(
            "Audit.Change.ValueTooLong",
            AuditEntry.Create(Id, Now, Record(changes: [new AuditChange("note", new string('v', 4001), null)]), null, null).Error.Code);
    }

    [Fact]
    public void A_listed_change_must_change_something()
    {
        var result = AuditEntry.Create(Id, Now, Record(changes: [new AuditChange("name", "Ali", "Ali")]), null, null);

        Assert.Equal("Audit.Change.Unchanged", result.Error.Code);
    }

    [Fact]
    public void A_field_can_be_added_or_removed()
    {
        var entry = AuditEntry.Create(
            Id,
            Now,
            Record(changes: [new AuditChange("phone", null, "+22200000001"), new AuditChange("email", "a@example.com", null)]),
            null,
            null).Value;

        Assert.Equal(2, entry.Changes.Count);
    }

    [Fact]
    public void Request_details_are_clipped_rather_than_refused()
    {
        var entry = AuditEntry.Create(Id, Now, Record(), new string('1', 60), new string('u', 600)).Value;

        Assert.Equal(AuditEntry.IpAddressMaxLength, entry.IpAddress!.Length);
        Assert.Equal(AuditEntry.UserAgentMaxLength, entry.UserAgent!.Length);
    }
}
