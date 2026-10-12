# Applying moderated menu changes

ShopChangeRequestApprovedV1 retains its wire identity. Its Menu branch delegates to
the Menu module, validates all catalog rows and retained photo IDs before mutation,
and commits the menu and AppliedShopChangeRequest marker in one Shops transaction.
Permanent validation failure produces ShopChangeRequestApplyFailedV1; replay of an
applied request does not write rows again.

ShopChangeMenuDto.ExpectedInputRevision remains nullable for old JSON decoding.
New Moderation submission/update requires a nonnegative observed revision from
ShopMenuDto.InputRevision. Missing revision returns 400 before changing the draft.
A missing published menu has revision zero. Apply rejects missing, negative or stale
revision; an old stored/queued request without a captured revision is not allowed to
overwrite a newer menu and requires manual reconciliation. EF concurrency also
rejects a race between the apply read and save.

Accepted external snapshots advance InputRevision even when empty or parsed-only.
Published ApplyShopMenuSnapshotEvent also carries optional ExpectedInputRevision for
wire decoding; new producers require it, and the consumer quarantines unsafe legacy
or stale snapshots. Paid OCR output uses the separately locked persisted job lifecycle.
No consumer invents the current revision for an old request.

Null/absent item and photo collections retain their established empty interpretation
after revision validation. Newly uploaded photos use a Media ownership claim before
Shops mutation, preserve Media photo IDs and emit durable ShopPhotosAttachedEvent
after commit. Client-supplied metadata is not trusted for a new upload. Lost replies,
uncertain commits and finalization retries preserve attachment identity; abandoned
reservations follow the operator procedure.

Evidence: producer null/-1/0 tests, ShopChangeRequestMenuLiveTests,
ShopChangeRequestOutboxLiveTests, direct snapshot revision/replay cases and physical
Media recovery. Production legacy envelopes still require inventory/draining before
cutover. See [compatibility](../shops-contract-compatibility.md),
[photo claims](../runbooks/shops-photo-claims.md) and [release packet](../shops-release-readiness.md).
