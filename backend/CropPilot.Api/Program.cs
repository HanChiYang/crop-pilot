using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using CropPilot.Api;
using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using CropPilot.Api.Constants;
using CropPilot.Api.Fields;
using CropPilot.Api.Fertilizers;
using CropPilot.Api.Irrigations;
using CropPilot.Api.Plantings;
using CropPilot.Contract.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var isDevelopment = builder.Environment.IsDevelopment();
var cookieSecurePolicy = isDevelopment ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

builder.Services.AddDbContext<CropPilotDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("CropPilot"),
        postgres => postgres.MigrationsHistoryTable("ef_migrations_history"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddScoped<FieldRepository>();
builder.Services.AddScoped<FieldService>();
builder.Services.AddScoped<PlantingRepository>();
builder.Services.AddScoped<PlantingService>();
builder.Services.AddScoped<FertilizerRepository>();
builder.Services.AddScoped<FertilizerService>();
builder.Services.AddScoped<IrrigationRepository>();
builder.Services.AddScoped<IrrigationService>();
builder.Services.AddIdentityCore<MainAccount>()
    .AddUserStore<CropPilotUserStore>()
    .AddClaimsPrincipalFactory<AccountPrincipalFactory>()
    .AddSignInManager();
builder.Services.AddSingleton<ILookupNormalizer, AccountLookupNormalizer>();

var accessTokens = new AccessTokens(builder.Configuration, builder.Environment);
var authentication = builder.Services.AddAuthentication();
authentication.AddJwtBearer(accessTokens.ConfigureValidation);
authentication.AddIdentityCookies();
GoogleSignIn.Configure(authentication, builder.Configuration, isDevelopment);
builder.Services.AddScoped<AuthSessions>();
builder.Services.AddSingleton(accessTokens);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CropPilot.Renewal";
    options.Cookie.Path = "/api/v1/auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
    options.SlidingExpiration = false;
    options.Events.OnValidatePrincipal = async context =>
    {
        var id = AuthSessions.GetId(context.Principal);
        var sessions = context.HttpContext.RequestServices.GetRequiredService<AuthSessions>();
        if (id == Guid.Empty || await sessions.FindActiveAsync(id, context.HttpContext.RequestAborted) is not { } session)
        {
            context.RejectPrincipal();
            return;
        }

        context.HttpContext.Features.Set(session);
    };
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorizationBuilder()
    .SetDefaultPolicy(new AuthorizationPolicyBuilder(JwtBearerDefaults.AuthenticationScheme).RequireAuthenticatedUser().Build())
    .AddPolicy("RenewalCookie", policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser())
    .AddPolicy("CsrfToken", policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .AddRequirements(new CsrfTokenRequirement()));
builder.Services.AddScoped<IAuthorizationHandler, CsrfTokenHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthPolicyResultHandler>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.SuppressReadingTokenFromFormBody = true;
    options.Cookie.Name = "CropPilot.Antiforgery";
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers(options =>
{
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
    options.ModelMetadataDetailsProviders.Add(new FieldSaveValidationMetadata());
}).AddJsonOptions(options =>
{
    var serializer = options.JsonSerializerOptions;
    serializer.Converters.Add(new ContractEnumJsonConverter<CreatePlantingRqDto.StatusEnum>(ErrorMessages.PlantingStatusInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<PlantingStatus>(ErrorMessages.PlantingStatusInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<CreateFertilizerLotRqDto.SourceEnum>(ErrorMessages.FertilizerSourceInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<FertilizerLotRsDto.SourceEnum>(ErrorMessages.FertilizerSourceInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<FertilizerLotRsDto.CurrencyCodeEnum>(ErrorMessages.FertilizerCurrencyInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<MaterialApplicationDetailRsDto.CurrencyCodeEnum>(ErrorMessages.FertilizerCurrencyInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<MaterialApplicationDetailRsDto.MaterialTypeEnum>(ErrorMessages.MaterialTypeInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<MaterialApplicationMethod>(ErrorMessages.MaterialApplicationMethodInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<PesticideStockUnit>(ErrorMessages.PesticideUnitInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<CreatePesticideLotRqDto.SourceEnum>(ErrorMessages.FertilizerSourceInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<PesticideLotRsDto.SourceEnum>(ErrorMessages.FertilizerSourceInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<PesticideLotRsDto.CurrencyCodeEnum>(ErrorMessages.FertilizerCurrencyInvalid));
    serializer.Converters.Add(new ContractEnumJsonConverter<IrrigationMethod>(ErrorMessages.IrrigationMethodInvalid));
    serializer.TypeInfoResolver = serializer.TypeInfoResolver!
        .WithAddedModifier(FieldDecimalJsonConverter.ConfigureFieldSave)
        .WithAddedModifier(FertilizationDetailIdJsonConverter.Configure);
}).ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var (key, entry) in context.ModelState)
        {
            if (entry.Errors.Count > 0)
            {
                var field = key.StartsWith("$.", StringComparison.Ordinal) ? key[2..] : key;
                errors[JsonNamingPolicy.CamelCase.ConvertName(field)] = ["輸入格式不正確。"];
            }
        }

        return new BadRequestObjectResult(new ValidationProblemDetails(errors)
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "請求驗證失敗"
        })
        {
            ContentTypes = { "application/problem+json" }
        };
    };
});

var app = builder.Build();

if (app.Configuration.GetValue<bool>("SeedDevelopmentAccount"))
{
    if (!app.Environment.IsDevelopment())
    {
        throw new InvalidOperationException("Test accounts can only be seeded in Development.");
    }

    await DevelopmentAccount.SeedAsync(app.Services, app.Configuration);
    return;
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!isDevelopment)
{
    app.UseHttpsRedirection();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

/// <summary>提供 API 入口，供整合測試啟動應用程式。</summary>
public partial class Program;
