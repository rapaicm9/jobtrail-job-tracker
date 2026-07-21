namespace JobTrail.IntegrationTests.Infrastructure;

/// <summary>
/// Waits for an eventually-consistent condition to hold. Erasure fans out
/// through the event bus and completes shortly after the request returns, so a
/// test observes the result by polling rather than assuming it is done. The
/// timeout is a failure detector, not a synchronisation device - the condition
/// normally holds within a few iterations.
/// </summary>
internal static class Poll
{
    public static async Task UntilAsync(
        Func<Task<bool>> condition,
        string because,
        CancellationToken cancellationToken,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));

        while (DateTime.UtcNow < deadline)
        {
            if (await condition())
            {
                return;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(50), cancellationToken);
        }

        Assert.Fail($"the condition did not hold within the timeout: {because}");
    }
}
