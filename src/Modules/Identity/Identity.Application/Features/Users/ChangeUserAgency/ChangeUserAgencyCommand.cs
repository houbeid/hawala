using Hawala.SharedKernel.Messaging;

namespace Identity.Application.Features.Users.ChangeUserAgency;

/// <summary>Le Superviseur déplace un agent vers une autre agence (ex. un employé qui part travailler à Dakar).</summary>
public sealed record ChangeUserAgencyCommand(Guid UserId, Guid? AgencyId) : ICommand;
