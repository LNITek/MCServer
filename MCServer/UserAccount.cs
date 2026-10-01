namespace MCServer;

/// <summary>
/// A single login account. Stored as a list in <see cref="AppSettings"/>
/// so future multi-user support only needs UI/API work — no migration.
/// For now the app enforces a single "Admin" user.
/// </summary>
public sealed class UserAccount
{
    public string Username { get; set; } = "Admin";

    /// <summary>PBKDF2 hash produced by <see cref="Services.PasswordHasher"/>.</summary>
    public string PasswordHash { get; set; } = "";

    /// <summary>Role name. Currently always "Admin"; kept for future multi-user support.</summary>
    public string Role { get; set; } = "Admin";
}
