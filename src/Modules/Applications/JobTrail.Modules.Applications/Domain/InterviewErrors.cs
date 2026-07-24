using JobTrail.SharedKernel;

namespace JobTrail.Modules.Applications.Domain;

/// <summary>Failures raised by the interview slices.</summary>
internal static class InterviewErrors
{
    /// <summary>
    /// No interview with this id exists under the caller's application. An
    /// interview on another user's application - or under a different application -
    /// is reported the same way, a 404, so nothing about it is observable.
    /// </summary>
    public static Error NotFound(Guid id) =>
        Error.NotFound("interview.not_found", $"No interview with id {id} exists.");
}
