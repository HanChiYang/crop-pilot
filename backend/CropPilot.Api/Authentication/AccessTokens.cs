using System.Security.Cryptography;
using CropPilot.Api.Constants;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CropPilot.Api.Authentication;

/// <summary>簽發 CropPilot 存取權杖，並設定 Bearer 驗證規則。</summary>
public sealed class AccessTokens(IConfiguration configuration, IWebHostEnvironment environment)
{
    private const string Issuer = "CropPilot";
    private const string Audience = "CropPilot.Api";
    private const string TokenType = "at+jwt";
    private readonly SymmetricSecurityKey key = LoadKey(configuration, environment);
    private readonly JsonWebTokenHandler handler = new();

    /// <summary>簽發最長有效五分鐘的權杖，效期不超過工作階段的到期時間。</summary>
    /// <returns>已簽章的權杖。工作階段已到期時回傳 <see langword="null"/>。</returns>
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

    /// <summary>設定 Bearer 權杖的簽章、發行者、受眾、效期及帳號識別驗證。</summary>
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
        options.Events = new JwtBearerEvents { OnTokenValidated = ValidateAccountSubject };
    }

    /// <summary>從已簽章的 <c>sub</c> 取得主帳號 UUID 並存入目前請求。格式不符時拒絕驗證。</summary>
    private static Task ValidateAccountSubject(TokenValidatedContext context)
    {
        if (context.SecurityToken is JsonWebToken token &&
            token.TryGetPayloadValue<string>("sub", out var subject) &&
            subject is { Length: 36 } &&
            Guid.TryParseExact(subject, "D", out var accountId) && accountId != Guid.Empty)
        {
            context.HttpContext.Features.Set(new AuthenticatedAccount(accountId));
        }
        else
        {
            context.Fail(ErrorMessages.InvalidAccessTokenSubject);
        }

        return Task.CompletedTask;
    }

    /// <summary>載入設定的簽章金鑰。本機開發未設定金鑰時產生暫用金鑰。</summary>
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
