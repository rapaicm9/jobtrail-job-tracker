using System.Security.Claims;
using JobTrail.Modules.Identity.Authentication;
using JobTrail.SharedKernel;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.JsonWebTokens;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class HttpContextUserContextTests
{
    private static HttpContextUserContext For(ClaimsPrincipal? user)
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = user is null ? null : new DefaultHttpContext { User = user },
        };

        return new HttpContextUserContext(accessor);
    }

    private static ClaimsPrincipal PrincipalWith(string subject) =>
        new(new ClaimsIdentity([new Claim(JwtRegisteredClaimNames.Sub, subject)]));

    [Fact]
    public void It_reads_the_subject_claim_as_the_user_id()
    {
        var id = UserId.New();

        For(PrincipalWith(id.ToString())).UserId.ShouldBe(id);
    }

    [Fact]
    public void No_request_in_flight_means_no_user() =>
        For(user: null).UserId.ShouldBeNull();

    [Fact]
    public void A_principal_without_a_subject_has_no_user() =>
        For(new ClaimsPrincipal(new ClaimsIdentity())).UserId.ShouldBeNull();

    [Fact]
    public void An_unparseable_subject_has_no_user() =>
        For(PrincipalWith("not-a-guid")).UserId.ShouldBeNull();
}
