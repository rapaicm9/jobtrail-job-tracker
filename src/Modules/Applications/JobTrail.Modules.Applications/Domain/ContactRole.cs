namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// The part a contact plays in a search - a recruiter, the hiring manager, an
/// interviewer, a referral, or something else. Optional on a contact, and stored
/// as its name so the column reads for itself. <c>Other</c> is the escape hatch
/// so the small set never forces a bad fit.
/// </summary>
internal enum ContactRole
{
    Recruiter,
    HiringManager,
    Interviewer,
    Referral,
    Other,
}
