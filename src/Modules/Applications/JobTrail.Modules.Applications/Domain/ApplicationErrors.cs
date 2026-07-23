using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the application aggregate.</summary>
internal static class ApplicationErrors
{
    /// <summary>
    /// The requested pipeline move violates the state machine (a backwards or
    /// same-stage move between active stages, <c>Accepted</c> from anywhere but
    /// <c>Offer</c>, or a move out of one terminal outcome into another). A
    /// validation failure - it surfaces as 422/ProblemDetails at the transition
    /// endpoint.
    /// </summary>
    public static Error IllegalTransition(Stage from, Stage to) =>
        Error.Validation(
            "application.illegal_transition",
            $"An application cannot move from {from} to {to}.");
}
