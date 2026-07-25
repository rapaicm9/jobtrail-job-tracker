using System.Reflection;
using JobTrail.Infrastructure.Outbox;
using JobTrail.Modules.Applications.Contracts;
using JobTrail.SharedKernel.Events;
using Shouldly;

namespace JobTrail.Modules.Applications.Tests;

/// <summary>
/// An event the module publishes but never registers is a row nobody can deliver:
/// the dispatcher cannot turn the name back into a type, so it retries to the
/// attempt limit and parks the row - and nothing says so until a consumer notices
/// a fact it was never told. The compiler cannot catch a missing registration, so
/// this walks the module's contract surface and holds the registration to it.
/// </summary>
public sealed class OutboxEventRegistrationTests
{
    [Fact]
    public void Registers_every_outbox_event_on_the_modules_contract_surface()
    {
        var registry = new OutboxEventRegistry();
        ApplicationsModule.RegisterOutboxEvents(registry);

        foreach (var eventType in OutboxEventTypes())
        {
            registry.IsRegistered(DeclaredNameOf(eventType))
                .ShouldBeTrue(
                    $"{eventType.Name} is an outbox event, but nothing registers it for delivery - "
                    + "every row it writes would be parked undelivered.");
        }
    }

    [Fact]
    public void Finds_the_events_it_claims_to_be_checking()
    {
        // Without this the rule above passes just as happily on an empty sequence,
        // which is precisely what a broken reflection query returns.
        OutboxEventTypes().ShouldNotBeEmpty();
    }

    private static IReadOnlyList<Type> OutboxEventTypes() =>
        [.. typeof(ApplicationSubmitted).Assembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } && type.IsAssignableTo(typeof(IOutboxEvent)))];

    private static string DeclaredNameOf(Type eventType) =>
        (string)eventType
            .GetProperty(nameof(IOutboxEvent.EventType), BindingFlags.Public | BindingFlags.Static)!
            .GetValue(null)!;
}
