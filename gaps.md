# gaps.md — Expense Tracker API (Project 02)

One line per thing I didn't know. Rule 6.

- **DTOs vs entities.** Returning an entity directly from an endpoint isn't just a
  purity issue — an entity with navigation properties (`Expense.Category`,
  `Category.Expenses`) forms a cycle, and `System.Text.Json`'s default behavior on a
  cycle is to throw, not silently truncate. `ReferenceHandler.Preserve` "fixes" the
  crash but leaks the object graph's shape into the wire format via `$id`/`$ref`
  markers — the real fix is projecting into a flat DTO with `.Select(...)` so the cycle
  never exists in what's being serialized.

- **Over-posting.** A request DTO isn't optional ceremony — binding a `POST` body
  directly to an entity type lets a client set every property the entity has,
  including server-controlled ones (`Id`) or nested objects that should never be
  writable through that endpoint (`{"category":{"name":"..."}}`).

- **`record` vs `class`.** Records default to `init`-only properties and value
  equality — right for DTOs (built once, compared by content, never mutated after
  construction), wrong for EF Core entities (need ongoing mutation for change
  tracking, and identity matters more than value equality). A class with a
  custom-validating setter (like a clamped `PageSize`) also doesn't fit a record's
  positional shape well.

- **`Math.Min` isn't a floor.** Capping only the top of a range (`Math.Min(value, 100)`)
  still lets zero and negative values through. Both `Skip()`/`Take()` translate to SQL
  `OFFSET`/`LIMIT`, and Postgres rejects negative values for both — an unvalidated
  page/pageSize doesn't just misbehave, it throws, and an unhandled database exception
  is an unhandled `500`.

- **Silent query-parameter binding mismatches are the dangerous kind.** A property
  named `FromDate` never binds from `?from=...` — ASP.NET Core's binder is
  case-insensitive but not synonym-aware. This doesn't error; it just leaves the
  property `null` and the filter silently does nothing. A test that only checks "some
  rows came back" can pass while never exercising the actual filter logic.

- **`[Required]` on a non-nullable value type is a no-op.** `decimal`, `DateOnly`,
  `int` can never be `null`, so `[Required]` on any of them always passes — verified
  directly: `amount: -500` returned `201`. What "required" can mean for those types is
  a range or a business rule, not a presence check.

- **Minimal API validation is opt-in in .NET 10.** Data Annotations on a DTO do
  nothing at all unless `builder.Services.AddValidation()` is registered. Attributes
  compiling cleanly is not evidence they run.

- **`IEnumerable<T>` only promises "you can loop over this once."** It says nothing
  about whether the data is already materialized. An EF Core query stored as
  `IEnumerable<T>` can re-run against the database on every enumeration, or throw far
  from the actual mistake if it outlives the `DbContext` it depends on. Materialize
  with `.ToListAsync()` before the data leaves the query layer.

- **TOCTOU (check-then-act) vs letting the constraint decide.** Checking a category
  exists, then inserting, has a gap where the category can be deleted in between —
  same shape as the URL shortener's short-code collision check. The fix isn't a
  smarter check; it's removing the check and catching the constraint violation
  (`23503`) at the point of the actual write, which has no such gap because the
  database evaluates the constraint and performs the write in one atomic step.

- **SQLSTATE codes are easy to swap and nothing catches it.** `23503`
  (foreign_key_violation) and `23505` (unique_violation) are two rows apart in
  Postgres's own error code table. A single find-and-replace flipped every occurrence
  of one to the other, including the one that was already correct — `IsForeignKeyViolation`
  silently started testing the unique-violation code, and the C# compiler has no way to
  know a method's name and the string literal inside it have drifted apart. Fixed by
  extracting both codes into named constants (`PostgresErrorCodes.ForeignKeyViolation`
  / `.UniqueViolation`) so the meaning lives in one place, not four copy-pasted string
  literals across two files.

- **`DbUpdateConcurrencyException` only fires when a row disappears, not when it
  changes.** `UPDATE ... WHERE Id = @id` affects exactly 1 row whether the row is
  untouched or has been modified by someone else since you read it — EF Core has no
  way to notice a *change*, only a *vanished row* (0 rows affected). Two concurrent
  renames of the same category both return `200`; one write is silently lost, with the
  loser told they succeeded. The existing delete-vs-delete and delete-vs-update races
  are a genuinely different failure mode from this one, and fixing the first taught
  nothing about the second.

- **Why `ExecuteUpdateAsync`'s atomicity fixes a counter increment but not a rename.**
  `ClickCount = ClickCount + 1` is atomic-safe because the new value only depends on
  the old value at the instant the database applies it — no read result needs to
  survive to inform the write. A rename's new value comes entirely from the client
  request, with no relation to what was read — atomicity removes the read-then-write
  gap *within one request*, but the actual conflict here is *between two independent
  requests*, which atomicity alone can't detect. It would take a *conditional* atomic
  update (`WHERE Id = @id AND Name = @originalNameRead`) or a concurrency token
  (`xmin`, `IsRowVersion()`) to actually detect the conflict — deferred to project 03,
  which is built around exactly this.

- **`FindAsync` already tracks; `.Attach()` after it is a no-op.** Carried an
  `.Attach()` over from a method whose read used `.AsNoTracking()` (where `Attach` is
  load-bearing) into a method whose read used plain `FindAsync` (which auto-tracks).
  Harmless, but implied a false thing about the entity's state — worth knowing *why*
  two visually similar lines differ before assuming they should look the same.

- **When something is broken at the entry point, fix the entry point first.** Spent
  four revisions reasoning carefully about `DeleteCategoryAsync`'s exception handling
  while `Program.cs` had a typo (`map.Delete` instead of `app.MapDelete`, called before
  `app` even existed) that made the endpoint completely unreachable. Every one of those
  four revisions was correctness reasoning about a code path that had never once
  executed. Correctness you can't observe running is indistinguishable from a guess.

- **`[AsParameters]` and Minimal API's default binding precedence.** A non-primitive
  parameter type binds from the request body by default unless told otherwise — this
  is why a plain `GET /api/expenses?categoryId=3` silently failed until `ExpenseFilter`
  was marked `[AsParameters]`, telling the binder to bind each property independently
  from the query string instead of trying to deserialize the whole object from an
  (absent) body.

- **Optionality has to live in the type, not the initializer, under `[AsParameters]`.**
  A property like `public int Page { get; set; } = 1` never gets a chance to fall back
  to `1` — a non-nullable value type absent from the query string is a *binding
  failure* (`400`), not a skip-and-use-default. The property has to be `int?` for
  absence to bind cleanly at all; the actual default/clamp then has to move to a
  computed property read at the point of use, so both the "supplied" and "absent"
  paths go through the same clamp logic.

- **A comment describing unwritten or already-fixed behavior is worse than no comment.**
  Left a `// Handle concurrency exception if needed` comment sitting in the wrong catch
  clause after a swap, and a `capped at 500` comment sitting next to code that
  correctly capped at 100. Neither was caught by the compiler — comments aren't code,
  and a stale one actively misleads the next reader (including future me) rather than
  just failing to help.
