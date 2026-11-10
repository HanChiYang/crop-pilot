using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CropPilot.Api.Authentication;

public sealed class AccessTokens(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string Issuer = "CropPilot";
    private const string Audience = "CropPilot.Api";
    private const string TokenType = "at+jwt";
    private readonly SymmetricSecurityKey key = LoadKey(configuration, environment);
    private readonly JsonWebTokenHandler handler = new();

    public string? Issue(Guid accountId, IEnumerable<string> roles, DateTimeOffset sessionExpiresAt)
    {
        var now = DateTimeOffset.UtcNow;
        var expires = now.AddMinutes(5);
        var expiresAt = expires < sessionExpiresAt ? expires : sessionExpiresAt;
        if (expiresAt.ToUnixTimeSeconds() <= now.ToUnixTimeSeconds())
        {
            return null;
        }

        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            TokenType = TokenType,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object> { ["sub"] = accountId.ToString("D"), ["role"] = roles.ToArray() },
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        });
    }

    public void ConfigureValidation(JwtBearerOptions options)
    {
        options.MapInboundClaims = true;
        options.IncludeErrorDetails = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = Issuer,
            ValidAudience = Audience,
            ValidTypes = [TokenType],
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = key,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.Zero
        };
    }

    private static SymmetricSecurityKey LoadKey(IConfiguration configuration, IWebHostEnvironment environment)
    {
        var configured = configuration["Authentication:Jwt:SigningKey"];
        var bytes = configured is null && environment.IsDevelopment()
            ? RandomNumberGenerator.GetBytes(32)
            : Convert.FromBase64String(configured ?? "");
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException("Authentication:Jwt:SigningKey must be a Base64 key of at least 32 bytes.");
        }

        return new SymmetricSecurityKey(bytes);
    }
}
