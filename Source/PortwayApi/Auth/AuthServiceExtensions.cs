using Microsoft.EntityFrameworkCore;

namespace PortwayApi.Auth;

public static class AuthServiceExtensions
{
    public static IServiceCollection AddPortwayAuth(this IServiceCollection services)
    {
        var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "auth.db");
        services.AddDbContext<AuthDbContext>(options => options.UseSqlite($"Data Source={dbPath}"));
        services.AddScoped<AdminUserService>();
        services.AddSingleton<Services.Avatars.AvatarService>();
        services.AddHostedService<Services.Avatars.AvatarWarmupService>();
        services.AddSingleton<ITokenVerificationCache, TokenVerificationCache>();
        services.AddScoped<TokenService>();
        services.AddSingleton<AuthFailureAuditQueue>();
        services.AddHostedService(sp => sp.GetRequiredService<AuthFailureAuditQueue>());
        services.AddScoped<EnvironmentAuthService>();
        services.AddAuthorization();
        return services;
    }
}
