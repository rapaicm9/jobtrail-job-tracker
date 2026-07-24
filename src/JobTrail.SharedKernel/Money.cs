namespace JobTrail.SharedKernel;

/// <summary>
/// A monetary amount paired with the currency it is denominated in. Multi-currency
/// by design - the currency travels with the amount and there is no FX conversion
/// in v1, so two <see cref="Money"/> values are only ever compared or combined when
/// their <see cref="Currency"/> matches. The code is an ISO 4217 alphabetic code
/// (e.g. <c>EUR</c>); shape validation lives with the request that supplies it, not
/// here - this stays a plain carrier.
/// </summary>
public readonly record struct Money(decimal Amount, string Currency);
