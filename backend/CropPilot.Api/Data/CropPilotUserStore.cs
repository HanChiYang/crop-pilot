using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace CropPilot.Api.Data;

internal sealed class CropPilotUserStore(CropPilotDbContext context, IdentityErrorDescriber describer)
    : UserOnlyStore<IdentityUser<Guid>, CropPilotDbContext, Guid>(context, describer)
{
    protected override Task<IdentityUserToken<Guid>?> FindTokenAsync(
        IdentityUser<Guid> user, string loginProvider, string name, CancellationToken cancellationToken)
    {
        // Identity looks up tokens by this tuple, while the database uses an independent id.
        var tracked = UserTokens.Local.FindEntry(
            [nameof(IdentityUserToken<Guid>.UserId), nameof(IdentityUserToken<Guid>.LoginProvider), nameof(IdentityUserToken<Guid>.Name)],
            [user.Id, loginProvider, name]);

        return tracked is not null
            ? Task.FromResult<IdentityUserToken<Guid>?>(tracked.Entity)
            : UserTokens.SingleOrDefaultAsync(
                token => token.UserId == user.Id && token.LoginProvider == loginProvider && token.Name == name,
                cancellationToken);
    }
}
