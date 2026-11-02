using Parties.Domain;
using Parties.Domain.Parties;

namespace Parties.Domain.Tests.Parties;

/// <summary>Rattachement d'un tiers (2 novembre 2026) : une de ses agences, ou les deux (null).</summary>
public class PartyHomeAgencyTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 9, 0, 0, TimeSpan.Zero);
    private static readonly Guid Nkc = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0001");
    private static readonly Guid Dkr = Guid.Parse("0199f0a0-0000-7000-8000-0000000a0002");

    private static Party NewParty()
    {
        var party = Party.Create("Tiers Fictif", PartyType.Client, null, null, null, null, Now).Value;
        party.AttachAgencyAccount(Nkc, Guid.NewGuid());
        party.AttachAgencyAccount(Dkr, Guid.NewGuid());
        return party;
    }

    [Fact]
    public void A_new_party_belongs_to_both_agencies_and_is_visible_from_each()
    {
        var party = NewParty();

        Assert.Null(party.HomeAgencyId);
        Assert.True(party.IsVisibleFrom(Nkc));
        Assert.True(party.IsVisibleFrom(Dkr));
    }

    [Fact]
    public void Attached_to_dakar_it_is_hidden_from_nouakchott_and_the_change_is_reported_once()
    {
        var party = NewParty();

        Assert.True(party.AssignHomeAgency(Dkr).Value);
        Assert.False(party.AssignHomeAgency(Dkr).Value);
        Assert.True(party.IsVisibleFrom(Dkr));
        Assert.False(party.IsVisibleFrom(Nkc));
        Assert.True(party.AssignHomeAgency(null).Value);
        Assert.True(party.IsVisibleFrom(Nkc));
    }

    [Fact]
    public void An_agency_without_an_account_for_this_party_is_refused()
    {
        var party = NewParty();

        Assert.Equal(PartiesErrors.Party.AgencyInvalid, party.AssignHomeAgency(Guid.NewGuid()).Error);
        Assert.Null(party.HomeAgencyId);
    }
}
