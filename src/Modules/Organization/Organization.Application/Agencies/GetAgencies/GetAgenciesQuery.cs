using Hawala.SharedKernel.Messaging;
using Organization.Contracts;

namespace Organization.Application.Agencies.GetAgencies;

/// <summary>Liste des agences, pour l'interface (badges, couleurs, choix de l'agence d'un compte).</summary>
public sealed record GetAgenciesQuery : IQuery<IReadOnlyList<AgencySummary>>;