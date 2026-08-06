# Integration verification suites

420 checks run against a **live API and a real PostgreSQL database**, in phase order. Where
the xUnit tests in `tests/IMS.Tests` pin down domain rules in isolation, these prove the
system actually behaves correctly end to end — including the things only a real database can
demonstrate: generated columns, unique indexes over `COALESCE()`, immutability triggers, and
row-locking under genuine concurrency.

| Suite | Checks | Covers |
|---|---|---|
| `verify_faz1.py` | 64 | Doc §3 hierarchy, §4 item master, dynamic attributes, UOM/barcodes, auth and RBAC |
| `verify_faz2.py` | 55 | Doc §5 balances and ledger, §8 movement, lot/serial/nested LPN, **scenario 4** |
| `verify_faz3.py` | 57 | Doc §6 inbound, receipt, putaway — **scenario 1** |
| `verify_faz4.py` | 81 | Doc §7 outbound, allocation, picking, shipment — **scenarios 2 and 3** |
| `verify_faz5.py` | 89 | Faz 5 counting and adjustment, and the full audit trail |
| `verify_faz6.py` | 74 | Faz 6 — all 30 §10 data points, algorithm tables |

They are **ordered and cumulative**: each builds on data the previous one created. Run them
in sequence against a freshly migrated database.

## Running everything from scratch

`run_all.sh` is the complete audit. It destroys the database, wipes all build output,
rebuilds from source, applies migrations to an empty database, runs the unit tests, starts
the API, runs all six suites, and finishes with a database-level integrity check.

```bash
bash tests/verification/run_all.sh
```

Adjust the connection details at the top of the script for your environment.

## What these catch that unit tests cannot

Several defects found during development were only visible against a real database:

- an EF projection written as a method call instead of an expression tree, which silently
  returned entities with unloaded navigations
- new child entities attached through a navigation property on an already-saved parent,
  which EF classified as `Modified` and turned into `UPDATE`s against non-existent rows
- untyped `NULL` parameters in raw SQL, which PostgreSQL rejects with `42P08`
- pruning a drained balance while an allocation still referenced it, severing a required
  foreign key mid-shipment

Each is now asserted against.
