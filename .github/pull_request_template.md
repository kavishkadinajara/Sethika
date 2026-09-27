<!--
Title must follow Conventional Commits, e.g.
  feat(access): cache effective permissions in Redis
  fix(booking): stop double-booking a full slot
Types: feat fix docs style refactor perf test build ci chore revert

CI blocks the PR if the title is wrong, the sections below are missing, or a
<!-- REPLACE ME --> placeholder is left in.
-->

## What

<!-- REPLACE ME --> One paragraph on what this changes. Not a commit log.

## Why

<!-- REPLACE ME --> The problem being solved. Link the issue: Closes #

## How to verify

<!-- REPLACE ME --> Exact steps a reviewer can follow.

```
dotnet build Sethika.sln
dotnet test Sethika.sln
cd web && npm run lint && npm run build
```

## Database

- [ ] No schema change
- [ ] Schema changed **only** by editing `.sql` files in `src/Sethika.Database`
- [ ] Matching `IEntityTypeConfiguration` updated
- [ ] Seed changes are idempotent (MERGE) and contain no business or test data
- [ ] `SqlPackage /Action:Script` against a seeded database shows no unintended drift

> EF Core migrations are never used. `src/Sethika.Database` is the schema source of truth.

## Security

- [ ] Every new endpoint carries `[HasPermission("CODE")]`
- [ ] Tenant-owned tables are covered by `sec.TenantIsolationPolicy`
- [ ] No secret in tracked files - connection strings and keys live in User Secrets
- [ ] No permission decision re-implemented in C#; `auth.vw_EffectivePermissions` remains the source
- [ ] UI guards are presentation only and the API check exists independently

## Tests

- [ ] Unit tests for logic that is genuinely C#
- [ ] Integration tests for anything enforced by the database (RLS, deny-wins, module gating)
- [ ] New tests fail when the behaviour they guard is broken (checked, not assumed)

## Risk

<!-- REPLACE ME --> What could this break, and how would we notice?
