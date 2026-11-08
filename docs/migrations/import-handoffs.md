# Import draft and handoff storage

Migration `20261103001539_ImportReviewDraftsAndDurableHandoffs` introduces the `imports`
schema. It adds tables and constraints without changing existing recipe or household data.
Apply it through the normal migration command before starting an updated API instance.

`Drafts` contains private source content and review JSON with a fixed 24-hour expiry.
Its household query filter fails closed without an explicit household scope. Application
services must additionally check `RequestedBy` before reading or writing a draft. A bound
handoff foreign key includes household, user and handoff ID, so a draft cannot reference
another user's receipt. Unknown measurement standards are stored as NULL; saving a recipe
requires explicit review and confirmation of AU or US measures.

`Handoffs` contains no raw recipe text or credentials. Its composite primary key is
`(HouseholdId, UserId, HandoffId)`. Capture and registration instants fix the original
seven-day expiry. Committed receipt fields must be present together; open and expired
receipts cannot contain partial outcomes. Original recipe identity is historical and has
no live recipe foreign key. Recipe deletion must preserve the receipt and cause the API
to report `committed_recipe_deleted`, never create a replacement recipe.

Retain committed receipts and expired-key tombstones for the installation lifetime.
Temporary draft and ordinary creation-retry cleanup must never delete these receipts.
Back up both tables with the rest of PostgreSQL. Do not restore recipes without their
associated durable receipts: that would break protection against delayed duplicate commits.

Before production migration, take and verify a database backup. Downgrading this migration
drops import review content and durable outcomes; use a matching full database backup for
recovery rather than dropping these tables on an installation that has accepted handoffs.
Authenticated receipt/draft endpoints and bounded cleanup now use this storage. Website/AI
extraction, ordinary import commits and both client review flows remain release prerequisites.
The maintenance worker visits bounded batches at startup and every five minutes; large backlogs
may require multiple passes. It skips busy households and retains all durable receipt identities.
