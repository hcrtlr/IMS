# Inventory Management System

A scaled-down Warehouse Management System covering the core inventory, inbound and outbound
processes, built so slotting, putaway and picking algorithms can be layered on later.

Implements the specification in *Stajyer Projesi: Inventory Management System*.

**.NET 8 · PostgreSQL 15 · EF Core 8 · Swagger · Docker · xUnit · FluentValidation**

---

## Status

| | |
|---|---|
| Phases | **Faz 1–6 complete** |
| API endpoints | 122 operations across 93 routes — all 27 from doc §12 present |
| Database | 40 tables, 198 indexes, 111 foreign keys, 16 check constraints, 2 triggers |
| Unit tests | **57 passing** |
| Integration verification | **420 checks passing** across 6 suites, from an empty database |
| Acceptance scenarios | **All 4 from doc §14 pass end to end** |
| Compiler warnings | 0 |

### Acceptance scenarios (doc §14)

| # | Scenario | Where it's proven |
|---|---|---|
| 1 | Receiving and putaway, with lot and expiry, visible in transaction history | `verify_faz3.py` |
| 2 | Order allocation, picking, shipment, correct stock decrement | `verify_faz4.py` |
| 3 | Insufficient stock: not fully allocated, never negative, shortfall reported per item | `verify_faz4.py` |
| 4 | Two lots of one item held as separate inventory records, FEFO-ordered | `verify_faz2.py` |

---

## Documentation

| Document | Contents |
|---|---|
| **[USER-GUIDE.md](docs/USER-GUIDE.md)** | End-to-end walkthrough: empty database → shipped order → completed stock count |
| **[DATABASE.md](docs/DATABASE.md)** | Every one of the 40 tables explained, with doc references |
| **[ASSUMPTIONS.md](docs/ASSUMPTIONS.md)** | Every inference made where the specification is silent — **read this first if you are reviewing** |

---

## Running it

### Option A — Docker (matches the specified stack)

```bash
cp .env.example .env
# set POSTGRES_PASSWORD and JWT_SECRET (at least 32 chars: openssl rand -base64 48)
docker compose up --build
```

Then open <http://localhost:8080>. The API applies its own migrations and seeds reference
data on first start.

> Docker is **not installed on this development machine**, so the `Dockerfile` and
> `docker-compose.yml` are written to spec but have not been executed here. Everything else
> below has been run and verified.

### Option B — Local PostgreSQL

**Prerequisites:** .NET SDK (8 or 9 — the projects target `net8.0`), PostgreSQL 15+.

```bash
# 1. Create the database
createdb -U postgres ims

# 2. Configure secrets — never commit these
cd IMS
dotnet user-secrets set "ConnectionStrings:Default" \
  "Host=localhost;Port=5432;Database=ims;Username=postgres;Password=YOUR_PASSWORD" \
  --project src/IMS.Api
dotnet user-secrets set "Jwt:Secret" "$(openssl rand -base64 48)" --project src/IMS.Api

# 3. Run
dotnet run --project src/IMS.Api
```

Migrations apply and reference data seeds automatically on start.

- Demo UI — <http://localhost:5080>
- Swagger — <http://localhost:5080/swagger>
- Health — <http://localhost:5080/health>

### First sign-in

The seeder creates one `Admin` user. Set its password up front:

```bash
dotnet user-secrets set "Seed:AdminPassword" "YourStrongPassword" --project src/IMS.Api
```

If you don't, a **random password is generated and written to the log once** on first start.
No fixed default credential ever ships.

### Tests

```bash
dotnet test
```

---

## Architecture

Layered, as doc §2 requires. Business logic never lives in controllers, and data access is
separated from business rules.

```
IMS.Domain          Entities, enums, domain rules, exceptions.        No dependencies.
      ↑
IMS.Application     Use-case services, DTOs, validation, contracts.   Knows Domain.
      ↑
IMS.Infrastructure  EF Core, PostgreSQL, repositories, JWT, seeding.  Implements Application.
      ↑
IMS.Api             Controllers, middleware, DI, static demo UI.
```

### The parts that matter

**`InventoryLedger` is the only way stock can change.** Doc rule §11.9 — "every stock change
must create an inventory transaction" — is only trustworthy if exactly one code path can
move stock. Inbound, outbound, movement and counting all route through it, and it writes the
ledger row in the same unit of work.

**Concurrency is handled twice over.** The doc's `Version` column (§5.1) is an optimistic
token that detects a conflict at save time — but for allocation that is too late: two callers
could both read "10 available" and both decide to take 8. Candidate balances are therefore
row-locked with `SELECT … FOR UPDATE`, which serialises them at read time so the second
caller correctly sees a shortfall. *Verified: two concurrent 40-unit moves against 60 units —
exactly one succeeds, stock never goes negative.*

**Three of the doc's rules are enforced by PostgreSQL, not application code:**

- `AvailableQuantity` is a **stored generated column** (`OnHand − Allocated − Hold`), so the
  §5.1 formula cannot drift from the stored data.
- The §5.1 uniqueness tuple contains nullable columns, and PostgreSQL treats `NULL` as
  distinct — a plain unique index would allow unlimited duplicate rows whenever lot, serial
  and LPN are null, which is the common case. The index is built over `COALESCE()` sentinels.
  *(The doc flags this concern itself, in §5.1.)*
- **Triggers block `UPDATE` and `DELETE` on `inventory_transactions`** (rule §11.10), so the
  ledger is immutable even to writes that bypass the application entirely.

**Multi-tenancy.** Account-scoped tables filter on `AccountId` directly; warehouse-scoped
tables reach their account through `Warehouse`, and every client-supplied `WarehouseId` is
validated before use. Cross-account access returns **404, not 403** — a 403 would confirm the
id exists elsewhere.

---

## Business rules (doc §11)

All twelve are implemented. Each is asserted against the running system, not just claimed.

| # | Rule | How it is enforced |
|---|---|---|
| 1 | No allocation without sufficient available stock | Checked under a row lock; reports the shortfall |
| 2 | Allocation never changes on-hand | Only `AllocatedQuantity` moves |
| 3 | Shipment reduces on-hand **and** allocated | `ShipAllocated` moves both together |
| 4 | Hold / damaged / expired stock is not allocatable | `InventoryStatus.IsAllocatable`; only `AVAILABLE` has it |
| 5 | Serial-tracked quantity never exceeds 1 | Domain check **plus** a database check constraint |
| 6 | Expiration-tracked items require an expiration date | Enforced at receipt, derived from `ShelfLifeDays` if omitted |
| 7 | Location capacity must not be exceeded | Capacity on location, inherited from its profile |
| 8 | Putaway respects temperature and hazmat rules | Checked before every putaway and movement |
| 9 | Every stock change writes a transaction | `InventoryLedger` is the single gateway |
| 10 | Transactions are never deleted | Entity + `DbContext` + **database triggers** |
| 11 | Concurrency control on simultaneous stock operations | Optimistic `Version` **plus** `SELECT … FOR UPDATE` |
| 12 | Failed operations roll back balance and transactions together | One database transaction per business operation |

---

## Where the specification was silent

Section 9 of the source document **is missing entirely** — it jumps from §8 to §10 — yet
counting and stock adjustment are required by §1, the §12 API list and Faz 5. That module is
reconstructed by analogy with the documented inbound/outbound patterns.

Several entities are referenced by foreign key but never defined: `UnitOfMeasure`,
`ItemCategory`, `Supplier`, `Customer`, `Shipment`. Authentication appears nowhere in the
document at all.

**Every one of these inferences is recorded in [ASSUMPTIONS.md](docs/ASSUMPTIONS.md)**, with
the reasoning and how to reverse it. Nothing was invented that the documentation does not
call for.

Two controls go beyond the documented rules, both flagged as assumptions:

- **Separation of duties** — the person who raises a stock adjustment cannot approve it. An
  approval gate the requester can satisfy themselves is not a control.
- **Reserved stock is protected** — a write-off cannot consume allocated or held quantity.

---

## Security

- **JWT bearer** authentication, HMAC-SHA256; the signing key comes from configuration and
  must be at least 32 characters or the API refuses to start.
- **BCrypt** password hashing, work factor 12. Plaintext is never stored or logged.
- **Four roles** — `Admin`, `WarehouseManager`, `Operator`, `Viewer` — mapped to named
  authorization policies rather than scattered role checks.
- **No secrets in the repository.** `appsettings.json` ships blank values; local development
  uses `dotnet user-secrets`, Docker uses `.env` (gitignored).
- Login timing is equalised against a dummy hash, so response time does not reveal whether a
  username exists.
- SQL parameter logging exists for debugging but is **off by default** —
  `Database:SensitiveDataLogging`.

---

## Project layout

```
IMS/
├── src/
│   ├── IMS.Domain/          entities, enums, domain rules
│   ├── IMS.Application/     use-case services, DTOs, contracts
│   ├── IMS.Infrastructure/  EF Core, migrations, repositories, JWT
│   └── IMS.Api/             controllers, middleware, wwwroot (demo UI)
├── tests/IMS.Tests/         57 unit tests
├── docs/                    USER-GUIDE, DATABASE, ASSUMPTIONS
├── Dockerfile
└── docker-compose.yml
```

---

## Known limitations

- **Docker is untested here** — not installed on this machine. The files are written to spec.
- **No algorithms.** Slotting, putaway suggestion, picking routing and order batching are
  explicitly deferred by doc §10. The data they need is captured in full, and
  `GET /api/algorithms/readiness` reports on it.
- **Packing is a status, not a process.** §7.1 lists `Packed` as an order status but the
  document specifies no packing entity or workflow.
- **No inspection workflow.** §1 mentions inspection in passing; receiving into
  `QUALITY_HOLD` is the documented mechanism and is supported.
- The demo frontend is deliberately minimal, per the brief.
