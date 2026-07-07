namespace PortwayApi.Classes.Configuration;

/// <summary>CORS policy wiring; never AllowAnyOrigin in production, operators configure WebUi:CorsOrigins</summary>
public static class CorsConfigurationExtensions
{
    private const string PolicyName = "AllowConfiguredOrigins";

    public static WebApplicationBuilder AddPortwayCors(this WebApplicationBuilder builder)
    {
        var corsOrigins = builder.Configuration.GetSection("WebUi:CorsOrigins").Get<string[]>() ?? [];
        if (!builder.Environment.IsDevelopment())
        {
            builder.Services.AddCors(options =>
            {
                options.AddPolicy(PolicyName, policy =>
                {
                    if (corsOrigins.Length > 0)
                    {
                        policy.WithOrigins(corsOrigins)
                              .AllowAnyMethod()
                              .AllowAnyHeader()
                              .AllowCredentials();
                    }
                    else
                    {
                        // No origins configured; block all cross-origin requests
                        policy.SetIsOriginAllowed(_ => false);
                    }
                });
            });
        }

        return builder;
    }

    public static WebApplication UsePortwayCors(this WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            app.UseCors(PolicyName);
        }
        else
        {
            // Development only: allow all origins for local testing convenience
            app.UseCors(corsBuilder =>
            {
                corsBuilder.AllowAnyOrigin()
                           .AllowAnyMethod()
                           .AllowAnyHeader();
            });
        }

        return app;
    }
}
