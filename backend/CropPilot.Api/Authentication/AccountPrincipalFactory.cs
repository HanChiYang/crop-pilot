using System.Security.Claims;
using CropPilot.Api.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace CropPilot.Api.Authentication;

internal sealed class AccountPrincipalFactory(
    UserManager<MainAccount> users,
    IOptions<IdentityOptions> options,
    CropPilotDbContext database) : UserClaimsPrincipalFactory<MainAccount>(users, options)
{
    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(MainAccount user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        var roles = await database.UserRoles.Where(role => role.MainAccountId == user.Id)
            .Select(role => role.Role).ToListAsync();
        foreach (var role in roles)
        {
            identity.AddClaim(new Claim(identity.RoleClaimType, role));
        }

        return identity;
    }
}

internal sealed class AccountLookupNormalizer : ILookupNormalizer
{
    public string? NormalizeName(string? name) => name;
    public string? NormalizeEmail(string? email) => email?.Trim().ToLowerInvariant();
}
