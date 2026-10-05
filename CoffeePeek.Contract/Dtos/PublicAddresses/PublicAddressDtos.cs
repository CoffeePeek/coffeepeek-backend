namespace CoffeePeek.Contract.Dtos.PublicAddresses;

public sealed record PublicAddressMetadata(Guid EntityId, string Slug, string CanonicalPath, long Revision, bool IsAlias = false);
public sealed record PublicAddressEnvelope<T>(T Data, PublicAddressMetadata Address);
public sealed record ChangePublicAddressRequest(string? Slug, long ExpectedRevision, string Reason);
public sealed record PublicAddressBackfillItem(Guid EntityId, string Slug, bool HadCollision, bool AlreadyInitialized);
public sealed record PublicAddressBackfillResult(bool DryRun, PublicAddressBackfillItem[] Items, Guid? NextCursor);
