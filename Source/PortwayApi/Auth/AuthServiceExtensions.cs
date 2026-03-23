using Microsoft.EntityFrameworkCore;

namespace PortwayApi.Auth;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddPortwayAuth(this IServiceCollection services)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "auth.db");
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddSingleton<ITokenVerificationCache, TokenVerificationCache>();
        services.AddScoped<TokenService>();
        services.AddScoped<EnvironmentAuthService>();
        services.AddAuthorization();
        return services;
    }
}
