using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the application aggregate.</summary>
internal static class ApplicationErrors
{
    /// <summary>
    /// The requested pipeline move violates the state machine (a backwards or
    /// same-stage move between active stages, or <c>Accepted</c> from anywhere
    /// but <c>Offer</c> - including out of a terminal outcome). A validation
    /// failure - it surfaces as 422/ProblemDetails at the transition endpoint.
    /// </summary>
    public static Error IllegalTransition(Stage from, Stage to) =>
        Error.Validation(
            "application.illegal_transition",
            $"An application cannot move from {from} to {to}.");

    /// <summary>
    /// No application with this id is owned by the caller. A resource owned by
    /// another user is reported the same way - a 404, never a 403, so ownership
    /// stays unobservable.
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("application.not_found", $"No application with id {id} exists.");

    /// <summary>
    /// The application references a company the caller does not own (or that does
    /// not exist). A bad reference in the request body is a validation failure - a
    /// 422, not a 404 about the company.
    /// </summary>
    public static Error UnknownCompany(Guid companyId) =>
        Error.Validation("application.unknown_company", $"No company with id {companyId} exists.");

    /// <summary>
    /// The account has no default campaign to place the application in. Every
    /// account is provisioned one at registration, so this is an invariant breach,
    /// not a client error.
    /// </summary>
    public static readonly Error NoDefaultCampaign =
        Error.Failure("application.no_default_campaign", "This account has no default campaign.");

    /// <summary>
    /// An offer-decision deadline was set on an application that has no offer yet.
    /// The deadline only means something once the application is at <c>Offer</c>, so
    /// setting it earlier is a validation failure - a 422.
    /// </summary>
    public static readonly Error OfferDeadlineRequiresOffer = Error.Validation(
        "application.offer_deadline_requires_offer",
        "An offer-decision deadline can only be set once the application has reached the Offer stage.");
}
