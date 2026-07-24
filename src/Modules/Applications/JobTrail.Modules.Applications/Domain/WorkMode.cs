namespace JobTrail.Modules.Applications.Domain;

/// <summary>
/// How a role is worked. A built-in field and one of the axes Pro analytics
/// breaks applications down by. Optional on an application - not every posting
/// states it - and stored as its name so the column reads for itself.
/// </summary>
internal enum WorkMode
{
    Onsite,
    Hybrid,
    Remote,
}
