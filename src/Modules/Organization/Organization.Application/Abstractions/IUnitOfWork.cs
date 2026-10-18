namespace Organization.Application.Abstractions;

/// <summary>Enregistre en une fois les modifications du module Organisation.</summary>
public interface IUnitOfWork
{
    Task SaveChangesAsync(CancellationToken cancellationToken);
}