using Asp.Versioning.ApiExplorer;
using Scalar.AspNetCore;

namespace Jobspect.Api.OpenApi;

/// <summary>
/// The API reference UI, over the document this host already serves.
/// </summary>
/// <remarks>
/// <para>
/// Development only, for the same reason the document is: it is a window onto
/// whatever the host maps, developer shortcuts included, and there is nobody to
/// read it in production.
/// </para>
/// <para>
/// The page is entirely local: the package serves its own bundle from this
/// host, and the one thing it would otherwise fetch from a CDN is turned off
/// below. That keeps the dev loop working offline and lets the reference's
/// content-security policy name no host but this one.
/// </para>
/// </remarks>
internal static class ScalarConfiguration
{
    public static IEndpointConventionBuilder MapApiReference(this IEndpointRouteBuilder endpoints) =>
        endpoints
            .MapScalarApiReference((options, context) =>
            {
                options.Title = "Jobspect API";

                // Inter and JetBrains Mono ship from a CDN and are the only
                // thing on the page that leaves this origin. System fonts cost
                // a dev tool nothing and buy back both the offline loop and a
                // policy that names no external host.
                options.DefaultFonts = false;

                // Taken from the same constant the document is served under, so
                // the two cannot drift into the UI reporting an empty API.
                options.OpenApiRoutePattern = OpenApiConfiguration.JsonRoute;

                // Every version the explorer found, rather than a hard-coded
                // "v1" - a second version should appear in the picker because it
                // exists, not because someone remembered to add a line here.
                var versions = context.RequestServices.GetRequiredService<IApiVersionDescriptionProvider>();
                options.AddDocuments(versions.ApiVersionDescriptions.Select(version => version.GroupName));

                // Preselect the scheme the document declares, so the auth panel
                // opens on a token box. Deliberately no token: prefilling one
                // would mean a credential in the source, and the whole point of
                // the dev loop is that minting a real one now works.
                options.AddPreferredSecuritySchemes("bearer");

                // A pasted token survives a refresh. Access tokens last ten
                // minutes, so without this the page costs a re-login as often as
                // it costs a reload.
                options.EnablePersistentAuthentication();
            })
            // Which is what widens the CSP for this response, and only this one.
            .WithMetadata(new RendersApiReference());
}
