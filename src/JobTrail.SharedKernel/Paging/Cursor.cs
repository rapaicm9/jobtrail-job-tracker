using System.Buffers.Text;
using System.Text;

namespace JobTrail.SharedKernel.Paging;

/// <summary>
/// A position in an ordered list: the sort value of the last row a client saw,
/// plus that row's id to break ties. Together they are a total order, which is what
/// lets the next page be "everything after this point" rather than "skip N rows" -
/// so rows inserted or removed meanwhile can't shift a page under the client.
/// <para>
/// On the wire it is one opaque base64url string. Opaque is the contract: clients
/// echo it back and never parse it, which leaves the encoding free to change. It is
/// deliberately <b>not</b> signed or encrypted - a cursor only ever carries the
/// caller's own row key, and every paged query is owner-scoped anyway, so a forged
/// one can do nothing but reposition a user inside their own data.
/// </para>
/// </summary>
public readonly record struct Cursor(Guid Id, string SortKey)
{
    private const char Separator = '|';

    /// <summary>A guid in <c>D</c> format, which is fixed-width and never contains the separator.</summary>
    private const int IdLength = 36;

    /// <summary>
    /// The cursor as the opaque string a client receives. The id leads and the sort
    /// key is everything after the separator, so a sort key may hold any character -
    /// which matters, since a sort key can be text the user typed.
    /// </summary>
    public string Encode()
    {
        var payload = $"{Id:D}{Separator}{SortKey}";
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(payload));
    }

    /// <summary>
    /// Reads a cursor a client sent back, or <c>null</c> if it is absent or
    /// malformed. Total by design: the value arrives from the query string, so
    /// every kind of garbage is expected input, not an exceptional case.
    /// </summary>
    public static Cursor? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return null;
        }

        // Checked before decoding, because TryDecodeFromChars throws on a
        // non-base64 character rather than returning false - its "try" only covers
        // a destination too small. The value arrives from a query string, so
        // invalid characters are ordinary input and must not surface as a 500.
        if (!Base64Url.IsValid(value))
        {
            return null;
        }

        var buffer = new byte[Base64Url.GetMaxDecodedLength(value.Length)];
        if (!Base64Url.TryDecodeFromChars(value, buffer, out var written))
        {
            return null;
        }

        var payload = Encoding.UTF8.GetString(buffer, 0, written);
        if (payload.Length < IdLength + 1
            || payload[IdLength] != Separator
            || !Guid.TryParseExact(payload[..IdLength], "D", out var id))
        {
            return null;
        }

        return new Cursor(id, payload[(IdLength + 1)..]);
    }
}
