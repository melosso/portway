namespace PortwayApi.Startup;

using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using PortwayApi.Auth;

/// <summary>
/// `portway accounts ...`: recovering a console account without the console.
/// Whoever has the shell outranks whoever merely has a sign-in.
/// </summary>
public static class AccountsCli
{
    /// <summary>What to type to run this build, which differs between a published apphost and `dotnet Portway.dll`</summary>
    public static string Invocation()
    {
        var dll = System.Reflection.Assembly.GetEntryAssembly()?.Location ?? "";
        var exe = Environment.ProcessPath;

        if (string.IsNullOrEmpty(exe)) return "portway";
        if (dll.Length == 0 || string.Equals(Path.GetFileNameWithoutExtension(exe),
                                             Path.GetFileNameWithoutExtension(dll), StringComparison.OrdinalIgnoreCase))
            return Quote(exe);
        return $"dotnet {Quote(dll)}";
    }

    private static string Quote(string path) => path.Contains(' ') ? $"\"{path}\"" : path;

    private static string DatabasePath => Path.Combine(Directory.GetCurrentDirectory(), "auth.db");

    /// <summary>
    /// Run from the wrong directory, EnsureCreated would make an empty auth.db there and every
    /// command would then truthfully report that the account does not exist. Refusing is clearer.
    /// </summary>
    private static bool MissingDatabase()
    {
        if (File.Exists(DatabasePath)) return false;

        Console.Error.WriteLine($"No Portway database at \"{DatabasePath}\".");
        Console.Error.WriteLine("Run this from the directory Portway runs in.");
        return true;
    }

    public static async Task<int> RunAsync(string[] args)
    {
        if (MissingDatabase()) return 1;

        var options = new DbContextOptionsBuilder<AuthDbContext>()
            .UseSqlite(new SqliteConnectionStringBuilder { DataSource = DatabasePath }.ToString())
            .Options;

        await using var db = new AuthDbContext(options);

        try
        {
            db.EnsureTablesCreated();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Could not open \"{DatabasePath}\": {ex.Message}");
            return 1;
        }

        var rest = args.Skip(1).ToArray();
        return rest switch
        {
            ["list"] => await ListAsync(db),
            ["password", var user, var password] => await SetPasswordAsync(db, user, password),
            ["create", var user, var password] => await CreateAsync(db, user, password, AdminUserRoles.Administrator),
            ["create", var user, var password, var role] => await CreateAsync(db, user, password, role),
            ["promote", var user] => await SetRoleAsync(db, user, AdminUserRoles.Administrator),
            ["demote", var user] => await SetRoleAsync(db, user, AdminUserRoles.Viewer),
            ["enable", var user] => await SetActiveAsync(db, user, true),
            ["disable", var user] => await SetActiveAsync(db, user, false),
            ["delete", var user] => await DeleteAsync(db, user),
            ["providers"] => await ListProvidersAsync(db),
            ["providers", "enable", var slug] => await SetProviderAsync(db, slug, true),
            ["providers", "disable", var slug] => await SetProviderAsync(db, slug, false),
            ["providers", "delete", var slug] => await DeleteProviderAsync(db, slug),
            _ => Usage(rest.Length == 0 ? 0 : 1),
        };
    }

    private static async Task<AdminUser?> FindAsync(AuthDbContext db, string username)
    {
        var account = await db.AdminUsers.FirstOrDefaultAsync(u => u.Username == username);
        if (account is null) Console.Error.WriteLine($"No account named \"{username}\".");
        return account;
    }

    private static async Task<int> ListAsync(AuthDbContext db)
    {
        var accounts = await db.AdminUsers.OrderBy(u => u.Username).ToListAsync();
        if (accounts.Count == 0)
        {
            Console.WriteLine("No accounts. Portway is open to anyone who can reach the console.");
            Console.WriteLine($"Create one with:  {Invocation()} accounts create <username> <password>");
            return 0;
        }

        Console.WriteLine($"{"USERNAME",-24} {"ROLE",-16} {"STATE",-10} {"LAST SIGN-IN",-20}");
        foreach (var a in accounts)
        {
            var last = a.LastLoginAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm") ?? "never";
            Console.WriteLine($"{a.Username,-24} {a.Role,-16} {(a.IsActive ? "active" : "inactive"),-10} {last,-20}");
        }
        return 0;
    }

    private static async Task<int> SetPasswordAsync(AuthDbContext db, string username, string password)
    {
        if (await FindAsync(db, username) is not { } account) return 1;

        if (AdminUserService.ValidatePassword(password) is { } problem)
        {
            Console.Error.WriteLine(problem);
            return 1;
        }

        account.PasswordHash = AdminUserService.HashPassword(password);
        await db.SaveChangesAsync();

        Console.WriteLine($"Password set for {account.Username}.");
        return 0;
    }

    private static async Task<int> CreateAsync(AuthDbContext db, string username, string password, string role)
    {
        if (AdminUserService.ValidateUsername(username) is { } nameProblem)
        {
            Console.Error.WriteLine(nameProblem);
            return 1;
        }
        if (AdminUserService.ValidatePassword(password) is { } passProblem)
        {
            Console.Error.WriteLine(passProblem);
            return 1;
        }
        if (!AdminUserRoles.IsKnown(role))
        {
            Console.Error.WriteLine($"Unknown role \"{role}\". Use administrator or viewer.");
            return 1;
        }
        if (await db.AdminUsers.AnyAsync(u => u.Username == username))
        {
            Console.Error.WriteLine($"\"{username}\" is already taken.");
            return 1;
        }

        db.AdminUsers.Add(new AdminUser
        {
            Username = username,
            PasswordHash = AdminUserService.HashPassword(password),
            Role = role,
            Provider = AdminUserProviders.Local,
        });
        await db.SaveChangesAsync();

        Console.WriteLine($"Created {username} as {role}.");
        return 0;
    }

    private static async Task<int> SetRoleAsync(AuthDbContext db, string username, string role)
    {
        if (await FindAsync(db, username) is not { } account) return 1;

        if (account.Role == role)
        {
            Console.WriteLine($"{account.Username} is already {role}.");
            return 0;
        }
        if (role != AdminUserRoles.Administrator && await IsLastAdministrator(db, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Promote another account first.");
            return 1;
        }

        account.Role = role;
        await db.SaveChangesAsync();

        Console.WriteLine($"{account.Username} is now {role}.");
        return 0;
    }

    private static async Task<int> SetActiveAsync(AuthDbContext db, string username, bool active)
    {
        if (await FindAsync(db, username) is not { } account) return 1;

        if (account.IsActive == active)
        {
            Console.WriteLine($"{account.Username} is already {(active ? "active" : "inactive")}.");
            return 0;
        }
        if (!active && await IsLastAdministrator(db, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Promote another account first.");
            return 1;
        }

        account.IsActive = active;
        await db.SaveChangesAsync();

        Console.WriteLine($"{account.Username} is now {(active ? "active" : "inactive")}.");
        return 0;
    }

    private static async Task<int> DeleteAsync(AuthDbContext db, string username)
    {
        if (await FindAsync(db, username) is not { } account) return 1;

        if (await IsLastAdministrator(db, account))
        {
            Console.Error.WriteLine($"{account.Username} is the last active administrator. Create another one first.");
            return 1;
        }

        db.AdminUsers.Remove(account);
        await db.SaveChangesAsync();

        Console.WriteLine($"Deleted {account.Username}.");
        return 0;
    }

    private static async Task<int> ListProvidersAsync(AuthDbContext db)
    {
        var providers = await db.OidcProviders.OrderBy(p => p.Slug).ToListAsync();
        if (providers.Count == 0)
        {
            Console.WriteLine("No sign-in providers. Accounts sign in with a password.");
            return 0;
        }

        Console.WriteLine($"{"KEY",-20} {"NAME",-24} {"STATE",-10} {"ISSUER",-40}");
        foreach (var p in providers)
            Console.WriteLine($"{p.Slug,-20} {p.Name,-24} {(p.IsEnabled ? "enabled" : "disabled"),-10} {p.Authority,-40}");
        return 0;
    }

    private static async Task<int> SetProviderAsync(AuthDbContext db, string slug, bool enabled)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug);
        if (provider is null)
        {
            Console.Error.WriteLine($"No provider with the key \"{slug}\".");
            return 1;
        }

        provider.IsEnabled = enabled;
        await db.SaveChangesAsync();

        Console.WriteLine($"{provider.Slug} is now {(enabled ? "enabled" : "disabled")}.");
        return 0;
    }

    private static async Task<int> DeleteProviderAsync(AuthDbContext db, string slug)
    {
        var provider = await db.OidcProviders.FirstOrDefaultAsync(p => p.Slug == slug);
        if (provider is null)
        {
            Console.Error.WriteLine($"No provider with the key \"{slug}\".");
            return 1;
        }

        // Leaving a binding to a deleted provider strands the account and blocks any other provider from adopting it
        var bound = await db.AdminUsers.Where(u => u.Provider == provider.Slug).ToListAsync();
        foreach (var account in bound)
        {
            account.Provider = AdminUserProviders.Local;
            account.ExternalId = null;
        }
        var stranded = bound.Count(u => u.PasswordHash.Length == 0 && u.IsActive);

        db.OidcProviders.Remove(provider);
        await db.SaveChangesAsync();

        Console.WriteLine($"Deleted {provider.Slug}. {bound.Count} account(s) unbound.");
        if (stranded > 0)
            Console.WriteLine($"{stranded} of them had no password and now have no way in. Set one with:  {Invocation()} accounts password <username> <password>");
        return 0;
    }

    private static async Task<bool> IsLastAdministrator(AuthDbContext db, AdminUser account)
    {
        if (account.Role != AdminUserRoles.Administrator || !account.IsActive) return false;
        return !await db.AdminUsers.AnyAsync(u =>
            u.Id != account.Id && u.IsActive && u.Role == AdminUserRoles.Administrator);
    }

    private static int Usage(int exitCode)
    {
        var me = Invocation();
        Console.WriteLine("accounts commands:");
        Console.WriteLine($"  {me} accounts list");
        Console.WriteLine($"  {me} accounts create <username> <password> [administrator|viewer]");
        Console.WriteLine($"  {me} accounts password <username> <password>");
        Console.WriteLine($"  {me} accounts promote <username>");
        Console.WriteLine($"  {me} accounts demote <username>");
        Console.WriteLine($"  {me} accounts enable <username>");
        Console.WriteLine($"  {me} accounts disable <username>");
        Console.WriteLine($"  {me} accounts delete <username>");
        Console.WriteLine($"  {me} accounts providers");
        Console.WriteLine($"  {me} accounts providers enable|disable|delete <key>");
        Console.WriteLine();
        Console.WriteLine("Run from the directory Portway runs in, so auth.db is found.");
        return exitCode;
    }
}
