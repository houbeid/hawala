namespace Identity.Application.Abstractions;

/// <summary>Enregistre en une fois toutes les modifications faites via les repositories du module.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}