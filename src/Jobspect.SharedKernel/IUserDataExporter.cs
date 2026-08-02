using System.Text.Json.Nodes;

namespace Jobspect.SharedKernel;

/// <summary>
/// One module's contribution to an account's data export. The read-side twin of
/// the erasure fan-out: erasure announces an event and every module deletes what
/// it holds, and this asks every module for what it holds - the difference being
/// that an export has to answer within the request, so it is a query rather than
/// an event.
/// <para>
/// It lives here, in the kernel, so that the module composing the export needs no
/// reference to the modules contributing to it. Each implementation stays
/// <c>internal</c> and is registered by its own module; the composer receives them
/// as <c>IEnumerable&lt;IUserDataExporter&gt;</c> and never learns who they are.
/// A module that gains user data later joins the export by registering one.
/// </para>
/// <para>
/// An implementation owes two things. It must scope every read to the account
/// asked about - ownership belongs inside the query here as everywhere else - and
/// it must return only what the user themselves put in. Credentials, tokens and a
/// module's own delivery bookkeeping are not the user's data and have no business
/// in a document they can download.
/// </para>
/// </summary>
public interface IUserDataExporter
{
    /// <summary>
    /// The key this contribution appears under in the exported document. Stable,
    /// lower-case and its module's name - it is part of the file's contract with
    /// whoever reads it later.
    /// </summary>
    string Section { get; }

    /// <summary>
    /// Everything this module holds for the account, as the JSON it will be
    /// written as. Returns an empty object or empty collections for an account
    /// that has nothing here - an absent section and an empty one would say the
    /// same thing to a reader, and the empty one says it without ambiguity.
    /// </summary>
    Task<JsonNode> ExportAsync(UserId userId, CancellationToken cancellationToken);
}
