# Review reports

User goal: authenticated users submit complaint text on published reviews; admins inspect complaints and dismiss them or remove the review. Email replies are a future option.

## Implementation

1. Shops domain: ReviewReport with required trimmed text (max 2000), ReviewId, ReportedByUserId, Pending/Dismissed/ReviewDeleted status, resolving admin and UTC timestamp. Repeat same decision idempotently; reject a conflicting decision. Review.RemoveByAdmin plus durable removal flag and guarded Restore prevent moderation reapproval restoring a removed review. Preserve author deletion behavior.
2. Shops persistence/application: report repository, DbSet/configuration/DI; report and review xmin concurrency. Validate review existence/visibility before submission; bounded paginated admin list and detail with review content and reporter identity. Resolve and soft-delete atomically with one UoW save; reuse cache invalidation after deletion. Generate AddReviewReports EF migration and Wolverine handler code.
3. HTTP: POST /api/CoffeeShopReviews/{reviewId}/reports {text}; Admin-only GET /api/admin/review-reports and /{reportId}; PUT /{reportId}/resolution {deleteReview}. Take user/admin identity exclusively from IUserContext, use request DTOs. Add gateway Admin route and docs/api/review-reports.md.

## Verification

Regression checks for text validation, missing/hidden review, unchanged dismissed review, durable admin deletion/reapproval, decisions and cache invalidation. Verify model constraints, endpoint/auth/binding/gateway routes and pre-generated handlers. Run affected test projects, CI build and has-pending-model-changes. No new dependencies, deployment or database changes. Preserve prior auto-tag fix.
