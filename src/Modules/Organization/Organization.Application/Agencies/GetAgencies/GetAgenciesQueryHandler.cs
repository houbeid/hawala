using Hawala.SharedKernel.Messaging;
using Hawala.SharedKernel.Results;
using Organization.Contracts;

namespace Organization.Application.Agencies.GetAgencies;

public sealed class GetAgenciesQueryHandler(IAgencyDirectory directory)
    : IQueryHandler<GetAgenciesQuery, IReadOnlyList<AgencySummary>>
{
    public async Task<Result<IReadOnlyList<AgencySummary>>> Handle(
        GetAgenciesQuery query, CancellationToken cancellationToken)
    {
        var agencies = await directory.GetAllAsync(cancellationToken);
        return Result.Success(agencies);
    }
}