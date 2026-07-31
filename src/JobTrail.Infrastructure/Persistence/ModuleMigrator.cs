using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace JobTrail.Infrastructure.Persistence;

/// <summary>
/// Applies the migrations of one module's <typeparamref name="TContext"/>. The
/// context stays internal to its module; this generic wrapper is what makes the
/// module's store migratable from outside without exposing it.
/// </summary>
internal sealed class ModuleMigrator<TContext>(IServiceScopeFactory scopes) : IModuleMigrator
    where TContext : DbContext
{
    public string Store => typeof(TContext).Name;

    public async Task MigrateAsync(CancellationToken cancellationToken)
    {
        // A scope of its own: the context is scoped, and the thing driving this is
        // not a request.
        await using var scope = scopes.CreateAsyncScope();

        await scope.ServiceProvider.GetRequiredService<TContext>()
            .Database.MigrateAsync(cancellationToken);
    }
}

/// <summary>
/// Composition surface for the migration fan-out.
/// </summary>
public static class ModuleMigratorRegistration
{
    /// <summary>
    /// Registers this module's store as one of the ones the migration one-shot
    /// brings up to date. Called by the module that owns the store, beside the
    /// registration of the context itself - the two belong together, so a module
    /// cannot come into existence unmigrated.
    /// </summary>
    public static IServiceCollection AddModuleMigrator<TContext>(this IServiceCollection services)
        where TContext : DbContext =>
        services.AddSingleton<IModuleMigrator, ModuleMigrator<TContext>>();
}
