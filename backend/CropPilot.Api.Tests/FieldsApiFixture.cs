using System.Net.Http.Headers;
using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Testcontainers.PostgreSql;
using Xunit;

namespace CropPilot.Api.Tests;

/// <summary>為田區 API 整合測試提供應用程式、獨立資料庫和已簽章的測試權杖。</summary>
public sealed class FieldsApiFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:15").Build();
    private WebApplicationFactory<Program> app = null!;

    /// <summary>啟動 PostgreSQL 和 API 測試主機，並將 migrations 套用到測試庫。</summary>
    public async Task InitializeAsync()
    {
        await postgres.StartAsync();
        app = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder
            .UseEnvironment("Development")
            .UseSetting("ConnectionStrings:CropPilot", postgres.GetConnectionString()));
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CropPilotDbContext>();
        Assert.Equal(postgres.GetConnectionString(), database.Database.GetConnectionString());
        await database.Database.MigrateAsync();
    }

    /// <summary>關閉 API 測試主機和 PostgreSQL 容器。</summary>
    public async Task DisposeAsync()
    {
        if (app is not null)
        {
            await app.DisposeAsync();
        }

        await postgres.DisposeAsync();
    }

    /// <summary>建立未設定 Bearer 權杖的 API 測試用戶端。</summary>
    internal HttpClient CreateClient() => app.CreateClient();

    /// <summary>簽發指定 <c>sub</c> 的測試權杖，並加入另一個帳號 claim 以驗證 API 的識別來源。</summary>
    internal HttpClient CreateClientWithSubject(string? subject)
    {
        var validation = app.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>()
            .Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;
        var claims = new Dictionary<string, object> { ["role"] = UserRole.User, ["nameid"] = Guid.NewGuid().ToString("D") };
        if (subject is not null)
        {
            claims["sub"] = subject;
        }

        var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = validation.ValidIssuer,
            Audience = validation.ValidAudience,
            TokenType = validation.ValidTypes.Single(),
            Expires = DateTime.UtcNow.AddMinutes(5),
            Claims = claims,
            SigningCredentials = new SigningCredentials(validation.IssuerSigningKey, SecurityAlgorithms.HmacSha256)
        });
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>建立啟用的測試帳號，並以正式簽發程式產生指定角色的權杖。</summary>
    internal async Task<(HttpClient Client, Guid AccountId)> CreateAccountClientAsync(string? role = UserRole.User)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CropPilotDbContext>();
        var account = new MainAccount { Status = MainAccount.Active };
        database.MainAccounts.Add(account);
        await database.SaveChangesAsync();

        var tokens = scope.ServiceProvider.GetRequiredService<AccessTokens>();
        var token = tokens.Issue(account.Id, role is null ? [] : [role], DateTimeOffset.UtcNow.AddHours(1));
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (client, account.Id);
    }

    /// <summary>直接查詢資料庫中屬於指定帳號的田區筆數。</summary>
    internal async Task<int> CountFieldsAsync(Guid accountId)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CropPilotDbContext>();
        return await database.Database.SqlQuery<int>($"""
            SELECT count(*)::integer AS "Value" FROM fields WHERE main_account_id = {accountId}
            """).SingleAsync();
    }

    /// <summary>加入一筆已結束的栽培紀錄，供查詢和刪除限制的測試使用。</summary>
    internal async Task<Guid> AddCompletedPlantingAsync(Guid fieldId, string cropName = "已結束作物",
        DateOnly? actualPlantingDate = null)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<CropPilotDbContext>();
        var plantingId = Guid.NewGuid();
        await database.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO plantings (id, field_id, crop_name, status, actual_planting_date, area_square_meters)
            VALUES ({plantingId}, {fieldId}, {cropName}, 'COMPLETED', {actualPlantingDate ?? new DateOnly(2026, 11, 1)}, 25)
            """);
        return plantingId;
    }
}
