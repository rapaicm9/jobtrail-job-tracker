namespace JobTrail.SharedKernel.Paging;

/// <summary>
/// One page of a list endpoint's results. Every paged list in the system returns
/// this shape, so a client learns to read it once: the rows, and the cursor to ask
/// for what follows. A null <see cref="NextCursor"/> means the end of the feed -
/// that, and only that, is how a client knows to stop.
/// <para>
/// There is deliberately no total count: a keyset page is cheap precisely because
/// it never counts the rows behind it, and a count would put that cost back on
/// every request. There is no separate "has more" flag either - the cursor already
/// answers it, and two ways to say the same thing eventually disagree.
/// </para>
/// </summary>
public sealed record PagedResponse<T>(IReadOnlyList<T> Items, string? NextCursor);
