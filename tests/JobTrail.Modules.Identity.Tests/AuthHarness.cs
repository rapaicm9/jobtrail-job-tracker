using JobTrail.Modules.Identity.Authentication;
using JobTrail.Modules.Identity.Domain;
using JobTrail.Modules.Identity.Tests.Fakes;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace JobTrail.Modules.Identity.Tests;

/// <summary>
/// Builds the collaborators the slice handlers take: a real
/// <see cref="UserManager{TUser}"/> (real hashing, real validators, the
/// module's exact password policy) over the in-memory store, and the token
/// service stack from the token-model tests.
/// </summary>
internal static class AuthHarness
{
    /// <summary>Mirror of the policy configured in IdentityModule - keep in step.</summary>
    public static UserManager<ApplicationUser> CreateUserManager(FakeUserStore store)
    {
        var options = new IdentityOptions
        {
            User = { RequireUniqueEmail = true },
            Password =
            {
                RequiredLength = 8,
                RequireUppercase = true,
                RequireLowercase = true,
                RequireDigit = true,
                RequireNonAlphanumeric = true,
            },
        };

        return new UserManager<ApplicationUser>(
            store,
            Options.Create(options),
            new PasswordHasher<ApplicationUser>(),
            [new UserValidator<ApplicationUser>()],
            [new PasswordValidator<ApplicationUser>()],
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            services: null!,
            NullLogger<UserManager<ApplicationUser>>.Instance);
    }

    public static TokenService CreateTokenService(
        FakeRefreshTokenStore refreshTokens,
        FakeUserTokenVersionReader versions,
        JwtOptions options,
        TestTimeProvider clock)
    {
        var issuer = new AccessTokenIssuer(new EcdsaSigningKeyProvider(options.Wrap()), options.Wrap(), clock);
        var refreshService = new RefreshTokenService(refreshTokens, options.Wrap(), clock);
        return new TokenService(issuer, refreshService, versions);
    }
}
