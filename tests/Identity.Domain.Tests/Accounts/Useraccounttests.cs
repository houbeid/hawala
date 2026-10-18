using Identity.Domain;
using Identity.Domain.Accounts;
using Identity.Domain.Roles;

namespace Identity.Domain.Tests.Accounts;

public class UserAccountTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 13, 8, 0, 0, TimeSpan.Zero);

    // Identifiants d'agence fictifs : le Domain ne fait que les porter.
    private static readonly Guid Nouakchott = Guid.Parse("0199f0a0-0000-7000-8000-00000000a001");
    private static readonly Guid Dakar = Guid.Parse("0199f0a0-0000-7000-8000-00000000a002");

    private static Guid? AgencyFor(RoleTemplate role) => role == RoleTemplate.Supervisor ? null : Nouakchott;

    private static UserAccount NewAccount(RoleTemplate role = RoleTemplate.Agent) =>
        UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value,
            phone: null,
            fullName: "Agent Fictif",
            role,
            AgencyFor(role),
            Now).Value;

    // --- Création ----------------------------------------------------------

    [Fact]
    public void New_account_is_active_and_must_change_its_temporary_password()
    {
        var phone = PhoneNumber.Create("+222 22 12 34 56").Value;

        var result = UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value, phone, "  Agent Fictif  ", RoleTemplate.Cashier, Nouakchott, Now);

        Assert.True(result.IsSuccess);
        var account = result.Value;
        Assert.NotEqual(Guid.Empty, account.Id);
        Assert.Equal("Agent Fictif", account.FullName);
        Assert.Equal(phone, account.Phone);
        Assert.Equal(RoleTemplate.Cashier, account.Role);
        Assert.Equal(Nouakchott, account.AgencyId);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.True(account.MustChangePassword);
        Assert.Equal(1, account.SecurityVersion);
        Assert.Equal(Now, account.CreatedAt);
    }

    [Fact]
    public void Phone_is_optional()
    {
        Assert.Null(NewAccount().Phone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_full_name_is_refused(string? fullName)
    {
        var result = UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value, null, fullName, RoleTemplate.Agent, Nouakchott, Now);

        Assert.Equal(IdentityErrors.Account.FullNameEmpty, result.Error);
    }

    [Fact]
    public void Full_name_is_limited_to_100_characters()
    {
        var email = Email.Create("agent@example.com").Value;

        var ok = UserAccount.CreateWithTemporaryPassword(
            email, null, new string('a', 100), RoleTemplate.Agent, Nouakchott, Now);
        var tooLong = UserAccount.CreateWithTemporaryPassword(
            email, null, new string('a', 101), RoleTemplate.Agent, Nouakchott, Now);

        Assert.True(ok.IsSuccess);
        Assert.Equal(IdentityErrors.Account.FullNameTooLong, tooLong.Error);
    }

    // --- Agence à la création ----------------------------------------------

    [Theory]
    [InlineData(RoleTemplate.Agent)]
    [InlineData(RoleTemplate.Cashier)]
    [InlineData(RoleTemplate.ReadOnly)]
    public void Agent_without_agency_is_refused(RoleTemplate role)
    {
        var missing = UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value, null, "Agent Fictif", role, null, Now);
        var empty = UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value, null, "Agent Fictif", role, Guid.Empty, Now);

        Assert.Equal(IdentityErrors.Account.AgencyRequired, missing.Error);
        Assert.Equal(IdentityErrors.Account.AgencyRequired, empty.Error);
    }

    [Fact]
    public void Supervisor_has_no_agency()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        Assert.Null(supervisor.AgencyId);
    }

    [Fact]
    public void Supervisor_with_an_agency_is_refused()
    {
        var result = UserAccount.CreateWithTemporaryPassword(
            Email.Create("chef@example.com").Value, null, "Chef Fictif", RoleTemplate.Supervisor, Nouakchott, Now);

        Assert.Equal(IdentityErrors.Account.AgencyNotAllowedForSupervisor, result.Error);
    }

    // --- Blocage / déblocage -----------------------------------------------

    [Fact]
    public void Blocking_an_agent_blocks_it_and_bumps_security_version()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.Block(activeSupervisorCount: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Blocked, account.Status);
        Assert.Equal(2, account.SecurityVersion);
    }

    [Fact]
    public void Blocking_an_already_blocked_account_is_refused_without_change()
    {
        var account = NewAccount();
        _ = account.Block(activeSupervisorCount: 1);

        var result = account.Block(activeSupervisorCount: 1);

        Assert.Equal(IdentityErrors.Account.AlreadyBlocked, result.Error);
        Assert.Equal(2, account.SecurityVersion);
    }

    [Fact]
    public void Last_active_supervisor_cannot_be_blocked()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.Block(activeSupervisorCount: 1);

        Assert.Equal(IdentityErrors.Account.LastActiveSupervisor, result.Error);
        Assert.Equal(AccountStatus.Active, supervisor.Status);
        Assert.Equal(1, supervisor.SecurityVersion);
    }

    [Fact]
    public void Supervisor_can_be_blocked_when_another_supervisor_is_active()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.Block(activeSupervisorCount: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Blocked, supervisor.Status);
    }

    [Fact]
    public void Unblocking_reactivates_the_account()
    {
        var account = NewAccount();
        _ = account.Block(activeSupervisorCount: 1);

        var result = account.Unblock();

        Assert.True(result.IsSuccess);
        Assert.Equal(AccountStatus.Active, account.Status);
        Assert.Equal(3, account.SecurityVersion);
    }

    [Fact]
    public void Unblocking_an_active_account_is_refused()
    {
        var result = NewAccount().Unblock();

        Assert.Equal(IdentityErrors.Account.NotBlocked, result.Error);
    }

    // --- Changement de rôle ------------------------------------------------

    [Fact]
    public void Last_active_supervisor_cannot_be_demoted()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.ChangeRole(RoleTemplate.Agent, Nouakchott, activeSupervisorCount: 1);

        Assert.Equal(IdentityErrors.Account.LastActiveSupervisor, result.Error);
        Assert.Equal(RoleTemplate.Supervisor, supervisor.Role);
        Assert.Null(supervisor.AgencyId);
    }

    [Fact]
    public void Demoted_supervisor_gets_the_given_agency()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.ChangeRole(RoleTemplate.Agent, Dakar, activeSupervisorCount: 2);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleTemplate.Agent, supervisor.Role);
        Assert.Equal(Dakar, supervisor.AgencyId);
        Assert.Equal(2, supervisor.SecurityVersion);
    }

    [Fact]
    public void Demoting_a_supervisor_without_agency_is_refused()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.ChangeRole(RoleTemplate.Agent, agencyId: null, activeSupervisorCount: 2);

        Assert.Equal(IdentityErrors.Account.AgencyRequired, result.Error);
        Assert.Equal(RoleTemplate.Supervisor, supervisor.Role);
        Assert.Equal(1, supervisor.SecurityVersion);
    }

    [Fact]
    public void Blocked_supervisor_can_be_demoted_since_it_is_not_active()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);
        _ = supervisor.Block(activeSupervisorCount: 2);

        // Il ne reste qu'un Superviseur actif : l'autre. Rétrograder celui-ci (bloqué) ne le retire pas.
        var result = supervisor.ChangeRole(RoleTemplate.Agent, Nouakchott, activeSupervisorCount: 1);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void Promoted_agent_loses_its_agency()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeRole(RoleTemplate.Supervisor, agencyId: null, activeSupervisorCount: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleTemplate.Supervisor, account.Role);
        Assert.Null(account.AgencyId);
    }

    [Fact]
    public void Promoting_an_agent_while_keeping_an_agency_is_refused()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeRole(RoleTemplate.Supervisor, Nouakchott, activeSupervisorCount: 1);

        Assert.Equal(IdentityErrors.Account.AgencyNotAllowedForSupervisor, result.Error);
        Assert.Equal(RoleTemplate.Agent, account.Role);
    }

    [Fact]
    public void Changing_between_agent_roles_can_keep_the_same_agency()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeRole(RoleTemplate.Cashier, Nouakchott, activeSupervisorCount: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(RoleTemplate.Cashier, account.Role);
        Assert.Equal(Nouakchott, account.AgencyId);
    }

    [Fact]
    public void Changing_to_the_same_role_changes_nothing()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.ChangeRole(RoleTemplate.Supervisor, agencyId: null, activeSupervisorCount: 1);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, supervisor.SecurityVersion);
    }

    // --- Changement d'agence -----------------------------------------------

    [Fact]
    public void Agent_can_be_moved_to_another_agency()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeAgency(Dakar);

        Assert.True(result.IsSuccess);
        Assert.Equal(Dakar, account.AgencyId);
        Assert.Equal(2, account.SecurityVersion);
    }

    [Fact]
    public void Moving_to_the_same_agency_changes_nothing()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeAgency(Nouakchott);

        Assert.True(result.IsSuccess);
        Assert.Equal(1, account.SecurityVersion);
    }

    [Fact]
    public void Supervisor_cannot_be_given_an_agency()
    {
        var supervisor = NewAccount(RoleTemplate.Supervisor);

        var result = supervisor.ChangeAgency(Dakar);

        Assert.Equal(IdentityErrors.Account.AgencyNotAllowedForSupervisor, result.Error);
        Assert.Null(supervisor.AgencyId);
    }

    [Fact]
    public void Moving_to_an_empty_agency_is_refused()
    {
        var account = NewAccount(RoleTemplate.Agent);

        var result = account.ChangeAgency(Guid.Empty);

        Assert.Equal(IdentityErrors.Account.AgencyRequired, result.Error);
        Assert.Equal(Nouakchott, account.AgencyId);
    }

    // --- Mot de passe ------------------------------------------------------

    [Fact]
    public void Changing_password_clears_the_temporary_flag()
    {
        var account = NewAccount();

        account.PasswordChanged();

        Assert.False(account.MustChangePassword);
        Assert.Equal(2, account.SecurityVersion);
    }

    [Fact]
    public void Supervisor_reset_requires_a_new_password_change()
    {
        var account = NewAccount();
        account.PasswordChanged();

        account.PasswordResetBySupervisor();

        Assert.True(account.MustChangePassword);
        Assert.Equal(3, account.SecurityVersion);
    }

    // --- Modification du profil --------------------------------------------

    [Fact]
    public void Updating_the_profile_changes_name_email_and_phone_without_cutting_sessions()
    {
        var account = NewAccount();
        var email = Email.Create("nouvel.email@example.com").Value;
        var phone = PhoneNumber.Create("+221 77 123 45 67").Value;

        var result = account.UpdateProfile(email, phone, "  Nom Corrigé  ");

        Assert.True(result.IsSuccess);
        Assert.Equal(email, account.Email);
        Assert.Equal(phone, account.Phone);
        Assert.Equal("Nom Corrigé", account.FullName);
        Assert.Equal(1, account.SecurityVersion);
    }

    [Fact]
    public void Updating_the_profile_can_remove_the_phone()
    {
        var account = UserAccount.CreateWithTemporaryPassword(
            Email.Create("agent@example.com").Value,
            PhoneNumber.Create("+222 22 12 34 56").Value,
            "Agent Fictif",
            RoleTemplate.Agent,
            Nouakchott,
            Now).Value;

        var result = account.UpdateProfile(account.Email, phone: null, account.FullName);

        Assert.True(result.IsSuccess);
        Assert.Null(account.Phone);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("   ")]
    public void Updating_the_profile_with_an_empty_name_changes_nothing(string? fullName)
    {
        var account = NewAccount();
        var originalEmail = account.Email;

        var result = account.UpdateProfile(Email.Create("autre@example.com").Value, phone: null, fullName);

        Assert.Equal(IdentityErrors.Account.FullNameEmpty, result.Error);
        Assert.Equal(originalEmail, account.Email);
        Assert.Equal("Agent Fictif", account.FullName);
    }

    [Fact]
    public void Updating_the_profile_with_a_too_long_name_is_refused()
    {
        var account = NewAccount();

        var result = account.UpdateProfile(
            account.Email, phone: null, new string('a', UserAccount.FullNameMaxLength + 1));

        Assert.Equal(IdentityErrors.Account.FullNameTooLong, result.Error);
        Assert.Equal("Agent Fictif", account.FullName);
    }
}