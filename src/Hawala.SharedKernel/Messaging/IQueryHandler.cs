using Hawala.SharedKernel.Results;

namespace Hawala.SharedKernel.Messaging;

/// <summary>Traite une requête de lecture.</summary>
public interface IQueryHandler<in TQuery, TResponse>
    where TQuery : IQuery<TResponse>
{
    Task<Result<TResponse>> Handle(TQuery query, CancellationToken cancellationToken);
}