namespace Identity.Contracts;

/// <summary>Lecture d'un compte par les autres modules (ex. les Tiers, pour la liste des tiers autorisés d'un compte).</summary>
public interface IAccountDirectory
{
    /// <summary>Le compte, ou null s'il n'existe pas.</summary>
    Task<AccountInfo?> FindAsync(Guid accountId, CancellationToken cancellationToken);
}

/// <param name="IsSupervisor">Vrai pour le rôle Superviseur (il voit tout, quelles que soient ses autres règles).</param>
/// <param name="AgencyId">Agence du compte ; null pour un Superviseur.</param>
/// <param name="Limits">Plafonds du compte, dans la devise de son agence (jamais pour un Superviseur).</param>
public sealed record AccountInfo(Guid Id, string FullName, bool IsSupervisor, Guid? AgencyId, AccountLimitsInfo Limits);

/// <summary>Plafonds d'un compte (null = pas de limite), dans la devise de son agence.</summary>
public sealed record AccountLimitsInfo(decimal? PerTransfer, decimal? PerDay, decimal? CreditPerDay);
