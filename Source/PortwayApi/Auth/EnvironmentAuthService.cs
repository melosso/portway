using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Serilog;
using PortwayApi.Classes;
using System.Security.Cryptography;

namespace PortwayApi.Auth;

/// <summary>
/// Service to perform environment-specific authentication validation
/// </summary>
public class EnvironmentAuthService
{
    private readonly Serilog.ILogger _logger;

    public EnvironmentAuthService(Serilog.ILogger logger)
    {
        _logger = logger;
    }


    /// <summary>
    /// Validates a request against environment-specific authentication settings
    /// </summary>
    /// <returns>True if authentication succeeded, false otherwise</returns>
    public async Task<bool> ValidateAsync(HttpContext context, AuthenticationSettings settings)
    {
        if (!settings.Enabled || settings.Methods == null || settings.Methods.Count == 0)
            return true;

        foreach (var method in settings.Methods)
        {
            try
            {
                bool success = method.Type.ToLowerInvariant() switch
                {
                    "apikey" => ValidateApiKey(context, method),
                    "basic" => ValidateBasicAuth(context, method),
                    "bearer" => ValidateBearerToken(context, method),
                    "jwt" => await ValidateJwtTokenAsync(context, method),
                    "hmac" => ValidateHmac(context, method),
                    _ => false
                };

                if (success)
                {
                    _logger.Debug("Successfully authenticated using environment method: {Type}", method.Type);
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Error during environment authentication validation ({Type})", method.Type);
            }
        }

        return false;
    }

    private bool ValidateApiKey(HttpContext context, AuthenticationMethod method)
    {
        string? value = method.In.ToLowerInvariant() switch
        {
            "header" => context.Request.Headers[method.Name],
            "query" => context.Request.Query[method.Name],
            "cookie" => context.Request.Cookies[method.Name],
            _ => null
        };

        if (string.IsNullOrEmpty(value) || string.IsNullOrEmpty(method.Value))
            return false;

        return value == method.Value;
    }

    private bool ValidateBasicAuth(HttpContext context, AuthenticationMethod method)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Basic ", StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            var parameter = authHeader.Substring("Basic ".Length).Trim();
            var credentials = Encoding.UTF8.GetString(Convert.FromBase64String(parameter)).Split(':', 2);
            
            if (credentials.Length != 2) return false;

            string username = credentials[0];
            string password = credentials[1];

            return username == method.Name && password == method.Value;
        }
        catch
        {
            return false;
        }
    }

    private bool ValidateBearerToken(HttpContext context, AuthenticationMethod method)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authHeader.Substring("Bearer ".Length).Trim();
        return token == method.Value;
    }

    private async Task<bool> ValidateJwtTokenAsync(HttpContext context, AuthenticationMethod method)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = authHeader.Substring("Bearer ".Length).Trim();
        var handler = new JwtSecurityTokenHandler();

        try
        {
            var validationParameters = new TokenValidationParameters
            {
                ValidateIssuer = !string.IsNullOrEmpty(method.Issuer),
                ValidIssuer = method.Issuer,
                ValidateAudience = !string.IsNullOrEmpty(method.Audience),
                ValidAudience = method.Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromMinutes(5)
            };

            if (!string.IsNullOrEmpty(method.Secret))
            {
                validationParameters.ValidateIssuerSigningKey = true;
                validationParameters.IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(method.Secret));
            }
            else if (!string.IsNullOrEmpty(method.PublicKey))
            {
                validationParameters.ValidateIssuerSigningKey = true;
                // Simple PEM handling for RSA public key
                var publicKey = method.PublicKey
                    .Replace("-----BEGIN PUBLIC KEY-----", "")
                    .Replace("-----END PUBLIC KEY-----", "")
                    .Replace("\n", "")
                    .Replace("\r", "")
                    .Trim();
                
                var rsa = RSA.Create();
                rsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(publicKey), out _);
                validationParameters.IssuerSigningKey = new RsaSecurityKey(rsa);
            }
            else
            {
                // Unsigned JWT? Usually not recommended but maybe allowed if ValidateIssuerSigningKey is false
                validationParameters.ValidateIssuerSigningKey = false;
            }

            var result = await handler.ValidateTokenAsync(token, validationParameters);
            
            if (result.IsValid)
            {
                // Optionally inject claims into the current user
                context.User.AddIdentity(new ClaimsIdentity(result.ClaimsIdentity.Claims, "EnvironmentAuth"));
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.Debug(ex, "JWT validation failed for environment");
        }

        return false;
    }

    private bool ValidateHmac(HttpContext context, AuthenticationMethod method)
    {
        // Simple HMAC implementation: Expects 'X-Signature' and 'X-Timestamp' headers
        // Signature = HMAC(Secret, Method + Path + Timestamp + Body)
        
        string? signature = context.Request.Headers[method.Name.Split('|')[0]]; // Default "X-Signature"
        string? timestamp = context.Request.Headers.TryGetValue("X-Timestamp", out var ts) ? ts.ToString() : null;

        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(method.Secret))
            return false;

        // Verify timestamp is recent (e.g., within 5 minutes)
        if (!long.TryParse(timestamp, out long tsSeconds)) return false;
        var requestTime = DateTimeOffset.FromUnixTimeSeconds(tsSeconds);
        if (Math.Abs((DateTimeOffset.UtcNow - requestTime).TotalMinutes) > 5)
            return false;

        try
        {
            // Read body
            context.Request.EnableBuffering();
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8, true, 1024, true);
            var body = reader.ReadToEndAsync().GetAwaiter().GetResult();
            context.Request.Body.Position = 0;

            var rawData = $"{context.Request.Method}{context.Request.Path}{timestamp}{body}";
            var keyBytes = Encoding.UTF8.GetBytes(method.Secret);
            var dataBytes = Encoding.UTF8.GetBytes(rawData);

            using var hmac = new HMACSHA256(keyBytes);
            var hashBytes = hmac.ComputeHash(dataBytes);
            var expectedSignature = Convert.ToHexString(hashBytes).ToLowerInvariant();

            return signature.ToLowerInvariant() == expectedSignature;
        }
        catch
        {
            return false;
        }
    }
}
