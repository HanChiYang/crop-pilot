using System.Text.Json;
using CropPilot.Api.Authentication;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding.Metadata;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<CropPilotDbContext>(options => options
    .UseNpgsql(builder.Configuration.GetConnectionString("CropPilot"),
        postgres => postgres.MigrationsHistoryTable("ef_migrations_history"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddIdentityCore<IdentityUser<Guid>>(options => options.User.RequireUniqueEmail = true)
    .AddUserStore<CropPilotUserStore>()
    .AddSignInManager();

var authentication = builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme);
authentication.AddIdentityCookies();
GoogleSignIn.Configure(authentication, builder.Configuration);
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.Name = "CropPilot.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
});

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("SessionCookie", policy => policy
        .AddAuthenticationSchemes(IdentityConstants.ApplicationScheme)
        .RequireAuthenticatedUser())
    .AddPolicy("CsrfToken", policy => policy.AddRequirements(new CsrfTokenRequirement()));
builder.Services.AddScoped<IAuthorizationHandler, CsrfTokenHandler>();
builder.Services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthPolicyResultHandler>();
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-XSRF-TOKEN";
    options.SuppressReadingTokenFromFormBody = true;
    options.Cookie.Name = "CropPilot.Antiforgery";
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
});
builder.Services.AddProblemDetails();
builder.Services.AddControllers(options =>
{
    options.ModelMetadataDetailsProviders.Add(new SystemTextJsonValidationMetadataProvider());
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

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
