using JobTrail.Modules.Identity.Contracts;
using JobTrail.Modules.Identity.Features.DeleteAccount;
using JobTrail.Modules.Identity.Tests.Fakes;
using JobTrail.SharedKernel;
using Shouldly;

namespace JobTrail.Modules.Identity.Tests;

public sealed class DeleteAccountHandlerTests
{
    private readonly RecordingEventBus _eventBus = new();
    private readonly DeleteAccountHandler _handler;

    public DeleteAccountHandlerTests() => _handler = new DeleteAccountHandler(_eventBus);

    [Fact]
    public async Task It_publishes_an_erasure_request_for_the_caller()
    {
        var userId = UserId.New();

        await _handler.HandleAsync(userId, CancellationToken.None);

        var published = _eventBus.Published.ShouldHaveSingleItem().ShouldBeOfType<UserDataDeletionRequested>();
        published.UserId.ShouldBe(userId);
    }
}
