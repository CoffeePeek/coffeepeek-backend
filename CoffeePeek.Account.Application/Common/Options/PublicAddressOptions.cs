namespace CoffeePeek.Account.Application.Common.Options;

/// <summary>
/// Feature flag for readable public profile addresses. Bound from section <c>PublicAddresses</c> of AccountService only
/// (environment variable <c>PublicAddresses__Enabled</c>, independent of ShopsService). Defaults to <c>false</c>; the
/// enablement gate is documented in <c>docs/api/public-addresses.md</c>. Retirement on account deletion runs regardless.
/// </summary>
public sealed class PublicAddressOptions
{
    public bool Enabled { get; set; }
}
