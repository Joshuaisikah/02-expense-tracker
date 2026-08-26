# Expense Tracker API

An API for tracking expenses grouped into categories. Project 02 on the .NET ladder —
first project with relationships, DTOs, filtering, and pagination.

## Setup

1. `dotnet restore`
2. Start Postgres (see `docker-compose.yml`):
   ```bash
   docker compose up -d
   ```
3. Update `ConnectionStrings:DefaultConnection` in `appsettings.Development.json` if your
   local Postgres port/credentials differ from the compose defaults.
4. Apply migrations:
   ```bash
   dotnet ef database update --project src/ExpenseTracker.Api
   ```
5. Run:
   ```bash
   dotnet run --project src/ExpenseTracker.Api
   ```

## Assumptions and decisions

**Deleting a category with expenses returns `409`, not a cascade.** `Expense.CategoryId`
is configured with `DeleteBehavior.Restrict`. The delete is attempted directly and the
database's own foreign key constraint refuses it (`SqlState 23503`) — no
count-then-delete pre-check, because that would just reintroduce a TOCTOU race between
counting and deleting.

**Category/expense creation catches the write failure rather than pre-checking.**
`CreateExpenseAsync` does not check `categoryId` exists before inserting; it attempts
the insert and catches the foreign-key violation (`23503`) if it fails, translating it
to `400`. Checking existence first and inserting second has a gap — the category can be
deleted in between — so the check would be stale by the time the insert runs. The insert
attempt *is* the check. Same reasoning for the unique category-name constraint (`23505`
→ `409` on duplicate names).

**Postgres error codes are named constants** (`PostgresErrorCodes.ForeignKeyViolation`,
`PostgresErrorCodes.UniqueViolation`), not bare string literals repeated at each call
site. Earlier revisions had four separate copies of the raw SQLSTATE string across two
files; a single find-and-replace once flipped one of them to the wrong code with no
compiler warning, because a `string == "23505"` check type-checks regardless of whether
`"23505"` is the right code for what the method claims to test. Naming the constant
removes the possibility of that particular drift.

**Deleted-out-from-under-you and duplicate-key conflicts return `404`/`409` respectively;
a row that's merely *changed* out from under you is not yet detected.** `PUT` on a
category or expense whose row still exists, but was modified by a concurrent request
between the read and the write, currently succeeds silently for both writers — one
write is lost with no signal to the losing caller. This is a known, deliberately
deferred gap; see below.

**Pagination uses `int?` with computed `EffectivePage`/`EffectivePageSize` properties,**
not plain `int` with a default. Under Minimal API's `[AsParameters]` binding, a
non-nullable value type absent from the query string is a binding *failure*, not a
fallback to the property's default — so `GET /api/expenses` with no query string at all
would 400 if `Page`/`PageSize` were plain `int`. Making them nullable lets the request
bind cleanly when absent; the clamp (`Math.Clamp(value, 1, 100)` for page size, a floor
of `1` for page) runs in a read-only computed property instead, so it applies uniformly
whether a value was supplied or not.

**`pageSize=0` (or negative) is silently clamped to `1`, not rejected with `400`.**
Defensible either way — a caller asking for zero rows is more likely a mistake than a
deliberate choice, which argues for a `400`. Chose the silent clamp for now, consistent
with how the upper bound (`pageSize=500` → clamped to `100`) is handled; revisit if this
API grows real API consumers who'd rather see an explicit rejection.

**A reversed date range (`from > to`) returns an empty `200`, not `400`.** Both `Where`
clauses simply compose to match nothing. No validation currently rejects this
combination outright. Equally defensible to treat it as a `400` (a client sending
`from` after `to` is likely confused about the API, not intentionally asking for
nothing) — deferred rather than decided against.

**`ORDER BY e."Date" DESC, e."Id"`, not `Date` alone.** Without a tie-breaker, rows
sharing a date have no guaranteed stable order between two separate queries — under
concurrent writes, a row could be skipped or duplicated across pages if its relative
position among same-date rows shifts between the query for page 1 and the query for
page 2. `Id` is unique, so appending it as a secondary sort key gives every row a fully
deterministic position regardless of date ties.

**Request DTOs never accept an entity's server-controlled fields.** `CreateExpenseRequest`
takes `CategoryId` (a reference) but never a nested `Category` object, and no request
DTO accepts an `Id`. Binding a request directly to an `Expense`/`Category` entity would
let a client set fields it has no business setting (its own `Id`, an arbitrary nested
category) — a request DTO exists specifically to name the smaller set of fields a
client should legitimately control.

**`decimal`, never `double`, for money; `DateOnly?`, never `DateTime?`, for date-range
filters.** `decimal` avoids the binary floating-point rounding that would otherwise make
`0.1 + 0.2 == 0.3` fail. `DateOnly` matches the migration's actual column type exactly,
so a boundary date like `2026-01-01` compares as expected with no time-of-day or
timezone component to introduce an off-by-one at the edges of a range.

## What I'd do next, and why I didn't already

**Optimistic concurrency (lost update on concurrent same-row updates).** Two concurrent
`PUT` requests renaming the same category both succeed — each `UPDATE ... WHERE Id = @id`
affects exactly one row, so EF Core's existing `DbUpdateConcurrencyException` handling
never fires; that exception only fires when a row has *vanished* (deleted underneath a
writer), not when it's merely been *changed*. The fix is a concurrency token — Postgres's
`xmin` system column, marked with `IsRowVersion()` — which would make every `UPDATE`'s
`WHERE` clause encode "only if this row is still the version I read." This is
deliberately deferred rather than fixed here: project 03 on the ladder (Ticket
Inventory) is built specifically around optimistic concurrency and atomic updates as
its core new concept, with a concurrent-purchase test as the forcing function to prove
whichever approach is chosen actually works. Solving it properly here would pre-empt
that project's entire point.

**No automated tests.** Every acceptance criterion in this session was verified by
running curl against a live, throwaway Postgres container by hand — including two real
concurrency races (40 concurrent deletes on the same row; 30 concurrent update-vs-delete
races on both resources) that would be far more valuable as permanent, repeatable xUnit
tests than as one-off manual runs. Prioritized getting the behavior correct and reviewed
first; the tests that would lock that behavior in place don't exist yet.

**`Range(0.01, double.MaxValue)` on `Amount` uses the `double`-typed overload of
`RangeAttribute`,** so the bound is parsed through binary floating point before being
compared against a `decimal` value — a small inconsistency in a project whose whole
point is `decimal` over `double` for money. The `RangeAttribute(Type, string, string)`
overload, which parses its bounds as the target type directly, would close this; not
yet applied.

**No rate limiting, no auth, no pagination `Link`/`X-Total-Count` headers.** Out of
scope for this project's brief; noted for completeness, not treated as a gap.

**Category/expense insert's FK pre-check trade.** `CreateExpenseAsync` catches the FK
violation at the write rather than pre-checking existence — the correct choice for
avoiding the TOCTOU race, but it does mean every insert with a genuinely invalid
`categoryId` pays for a full round-trip to the database and an exception before
returning `400`, rather than a cheaper `AnyAsync` check first. Accepted as the right
trade for correctness over micro-optimizing the invalid-input path.
