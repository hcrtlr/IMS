# Inventory Management System (IMS) — Intern Project

**Source**: `Stajyer Projesi: Inventory Management System.docx` / `.pdf` (identical content, same source doc)

## Goal
Not a full enterprise WMS — a small, extensible Inventory Management System covering the
core inventory/inbound/outbound processes inside a WMS, built so slotting/putaway/picking
algorithms can be layered on later.

## Scope (must support)
- Warehouse structure (Account → Warehouse → Zone → Aisle → Location)
- Products + product attributes (dynamic attribute system)
- Stock location & status tracking
- Stock in/out/transfer movement logging
- Inbound order receiving
- Outbound order creation
- Stock reservation (allocation) + picking tasks
- Cycle counts & stock adjustments
- Data groundwork for future slotting/putaway/picking algorithms

## Tech Stack
.NET 8 Web API · PostgreSQL · Entity Framework Core · Swagger/OpenAPI · Docker ·
xUnit or NUnit · FluentValidation · Git

## Architecture
Layered (not required to be pure Clean Architecture):
- **Domain** — entities, enums, value objects, domain rules
- **Application** — use cases, commands, queries, services
- **Infrastructure** — PostgreSQL, EF Core, repositories, external service implementations
- **API** — controllers, request/response models

Requirement: business logic must not live in controllers; data access and business rules
must be separated; code must be testable.

## Delivery Phases
See `memory/glossary.md` → Development Phases, and `TASKS.md` for the live checklist.

## Acceptance Scenarios (doc §14)
1. **Receiving & Putaway** — create item → inbound order → receive to receiving location →
   capture lot/expiration → create putaway task → move to storage → verify transaction history
2. **Order Allocation & Picking** — create outbound order → release → allocate stock →
   create pick task → complete picking → ship → verify correct stock decrement
3. **Insufficient Stock** — order exceeding available stock must not become fully allocated,
   must not go negative, and must report the shortfall per item
4. **FEFO Readiness** — two lots of the same item with different expiration dates must be
   stored as separate inventory records even before a FEFO algorithm exists

## API Surface (doc §12)
Master Data: `/api/items`, `/api/warehouses`, `/api/zones`, `/api/locations` (+ `/available`)
Inventory: `/api/inventory`, `/api/inventory/movements`, `/api/inventory/status-change`, `/api/inventory/transactions`
Inbound: `/api/inbound-orders`, `.../receive`, `/api/receipts/{id}/create-putaway`, `/api/putaway-tasks/{id}/complete`
Outbound: `/api/orders`, `.../release`, `.../allocate`, `.../create-pick-tasks`, `/api/pick-tasks/{id}/complete`, `.../ship`
Count: `/api/count-plans`, `/api/count-tasks/{id}/complete`, `/api/inventory-adjustments/{id}/approve`
