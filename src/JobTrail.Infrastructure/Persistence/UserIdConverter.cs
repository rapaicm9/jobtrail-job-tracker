using JobTrail.SharedKernel;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace JobTrail.Infrastructure.Persistence;

/// <summary>
/// Maps the strongly-typed <see cref="UserId"/> to the <c>uuid</c> column it is
/// stored as. Owner columns in every module but Identity are non-FK references
/// to an account - no cross-schema foreign key - so the value travels as a bare
/// UUID while the domain keeps the type that stops it being confused for any
/// other id. Applied module-wide through <c>ConfigureConventions</c>.
/// </summary>
public sealed class UserIdConverter() : ValueConverter<UserId, Guid>(
    id => id.Value,
    value => UserId.From(value));
