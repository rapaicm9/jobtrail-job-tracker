using JobTrail.SharedKernel.Paging;
using Shouldly;

namespace JobTrail.SharedKernel.Tests;

/// <summary>
/// The cursor codec. Decoding takes whatever a client put in the query string, so
/// the contract that matters is total: every malformed input is a null, never an
/// exception, and anything encoded here reads back exactly - a sort key that
/// survives the trip only approximately would skip or repeat rows at a page edge.
/// </summary>
public sealed class CursorTests
{
    [Theory]
    [InlineData("2026-07-24")]
    [InlineData("638601984000000000")]
    [InlineData("Alex Kim")]
    public void Round_trips_a_sort_key(string sortKey)
    {
        var cursor = new Cursor(Guid.CreateVersion7(), sortKey);

        Cursor.Decode(cursor.Encode()).ShouldBe(cursor);
    }

    [Theory]
    [InlineData("Nakamura | Sons")]      // the separator itself
    [InlineData("=========")]            // base64 padding characters
    [InlineData("Ünïcodé ✈ 東京")]        // multi-byte
    [InlineData("")]                     // no sort key at all
    [InlineData("  spaced  ")]           // whitespace is significant
    public void Round_trips_a_sort_key_that_could_break_the_encoding(string sortKey)
    {
        var cursor = new Cursor(Guid.CreateVersion7(), sortKey);

        var decoded = Cursor.Decode(cursor.Encode()).ShouldNotBeNull();
        decoded.SortKey.ShouldBe(sortKey);
        decoded.Id.ShouldBe(cursor.Id);
    }

    [Fact]
    public void Encodes_to_a_url_safe_string()
    {
        // Cursors travel in a query string, so they must survive it unescaped.
        var encoded = new Cursor(Guid.CreateVersion7(), "Ünïcodé ✈ 東京 | ?&=#").Encode();

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
        Uri.EscapeDataString(encoded).ShouldBe(encoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a cursor")]
    [InlineData("!!!!")]
    [InlineData("YWJj")]                                   // decodes, but is not a payload
    [InlineData("MDE5M2UxMjM0NTY3ODlhYmNkZWY")]            // decodes, no separator
    public void Refuses_anything_that_is_not_a_cursor(string? value) =>
        Cursor.Decode(value).ShouldBeNull();

    [Fact]
    public void Refuses_a_payload_whose_id_is_not_a_guid() =>
        Cursor.Decode(Encode("this-is-not-a-guid-but-is-36-chars-x|2026-07-24")).ShouldBeNull();

    [Fact]
    public void Refuses_a_payload_that_stops_before_the_separator() =>
        Cursor.Decode(Encode(Guid.CreateVersion7().ToString("D"))).ShouldBeNull();

    [Fact]
    public void Refuses_a_payload_whose_separator_is_misplaced() =>
        Cursor.Decode(Encode($"{Guid.CreateVersion7():D}X|2026-07-24")).ShouldBeNull();

    private static string Encode(string payload) =>
        System.Buffers.Text.Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(payload));
}
