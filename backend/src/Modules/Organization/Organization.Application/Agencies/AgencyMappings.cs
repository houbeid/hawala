using Organization.Contracts;
using Organization.Domain.Agencies;

namespace Organization.Application.Agencies;

internal static class AgencyMappings
{
    public static AgencySummary ToSummary(this Agency agency) => new(
        agency.Id,
        agency.Code,
        agency.Name,
        agency.Country.ToString(),
        agency.Currency,
        agency.Color.Value,
        agency.TimeZoneId);
}