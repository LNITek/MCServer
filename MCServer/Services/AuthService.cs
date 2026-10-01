namespace MCServer.Services;

/// <summary>
/// Owns the user accounts stored in <see cref="AppSettings.Users"/>.
/// Designed for future multi-user support (users are a list with roles),
/// but for now the app enforces a single "Admin" user.
/// </summary>
public sealed class AuthService
{
    public const string DefaultUsername = "Admin";
    public const string DefaultPassword = "Admin";
    public const string AdminRole = "Admin";

    private readonly SettingsService _settingsStore;

    public AuthService(SettingsService settingsStore)
    {
        _settingsStore = settingsStore;
        EnsureDefaultAdmin(persist: true);
    }

    public AppSettings Settings => _settingsStore.Settings;

    /// <summary>Read-only view for future multi-user UI.</summary>
    public IReadOnlyList<UserAccount> Users => Settings.Users;

    /// <summary>The single admin account (first user with Admin role, or first user).</summary>
    public UserAccount AdminUser =>
        Settings.Users.FirstOrDefault(u => u.Role == AdminRole)
        ?? Settings.Users.FirstOrDefault()
        ?? throw new InvalidOperationException("No users configured.");

    /// <summary>
    /// Guarantees a usable login exists. Called at startup and before validation.
    /// </summary>
    public void EnsureDefaultAdmin(bool persist = true)
    {
        if (Settings.Users.Count == 0)
        {
            Settings.Users.Add(new UserAccount
            {
                Username = DefaultUsername,
                PasswordHash = PasswordHasher.Hash(DefaultPassword),
                Role = AdminRole
            });
            if (persist) _settingsStore.Save();
        }
    }

    public UserAccount? FindByUsername(string username) =>
        Settings.Users.FirstOrDefault(u =>
            u.Username.Equals(username?.Trim() ?? "", StringComparison.OrdinalIgnoreCase));

    public bool ValidateCredentials(string? username, string? password)
    {
        EnsureDefaultAdmin();
        var user = FindByUsername(username ?? "");
        if (user is null) return false;
        return PasswordHasher.Verify(password ?? "", user.PasswordHash);
    }

    public record CredentialChangeResult(bool Ok, string Message);

    /// <summary>
    /// Changes the admin username and/or password. Requires the current password.
    /// Pass null/empty newUsername or newPassword to leave that part unchanged.
    /// </summary>
    public CredentialChangeResult ChangeAdminCredentials(
        string currentPassword, string? newUsername, string? newPassword)
    {
        EnsureDefaultAdmin(persist: false);

        var admin = AdminUser;

        if (!PasswordHasher.Verify(currentPassword ?? "", admin.PasswordHash))
            return new(false, "Current password is incorrect.");

        newUsername = newUsername?.Trim() ?? "";
        var changeUser = newUsername.Length > 0 && !newUsername.Equals(admin.Username, StringComparison.OrdinalIgnoreCase);
        var changePass = !string.IsNullOrEmpty(newPassword);

        if (!changeUser && !changePass)
            return new(false, "Enter a new username and/or a new password.");

        if (changeUser)
        {
            if (newUsername.Length is < 1 or > 64)
                return new(false, "Username must be 1-64 characters.");
            if (Settings.Users.Any(u => !ReferenceEquals(u, admin) &&
                                        u.Username.Equals(newUsername, StringComparison.OrdinalIgnoreCase)))
                return new(false, "That username is already taken.");
            admin.Username = newUsername;
        }

        if (changePass)
        {
            if (newPassword!.Length < 4)
                return new(false, "New password must be at least 4 characters.");
            if (newPassword.Length > 256)
                return new(false, "New password is too long.");
            admin.PasswordHash = PasswordHasher.Hash(newPassword);
        }

        _settingsStore.Save();
        return new(true, "Login credentials updated.");
    }

    /// <summary>Resets the admin account to Admin/Admin. Used by settings UI and host CLI.</summary>
    public void ResetToDefault()
    {
        Settings.Users.Clear();
        EnsureDefaultAdmin(persist: false);
        _settingsStore.Save();
    }
}
