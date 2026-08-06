# Database Reference

40 tables, 198 indexes, 111 foreign keys, 16 check constraints, 2 triggers.
PostgreSQL 15+. Every table is listed below with what it is for, where it comes from in
the source specification, and the columns that carry meaning beyond their name.

**Conventions used everywhere**

| Convention | Detail |
|---|---|
| Primary key | `Id uuid`, assigned by the application before insert |
| Auditing | `CreatedAt`, `CreatedBy`, `UpdatedAt`, `UpdatedBy` stamped centrally by the `DbContext` |
| Quantities | `numeric(18,4)` — four decimal places throughout |
| Weights / dimensions | `numeric(18,3)` |
| Temperatures | `numeric(6,2)` |
| Enums | stored as `int`, transmitted over the API as **names** so clients never depend on ordering |
| Deletes | master data is deactivated (`IsActive = false`), not deleted — it is referenced by the immutable ledger |

---

## Faz 1 — Master data

### `accounts` — doc §3.1
The company or customer using the system; the root of the whole hierarchy and the tenant
boundary. Every query is scoped to the caller's account.

| Column | Notes |
|---|---|
| `Code` | Globally unique |
| `Name`, `IsActive` | |

### `warehouses` — doc §3.2
A physical depot.

| Column | Notes |
|---|---|
| `AccountId` | Owning tenant |
| `Code` | Unique **within an account**, not globally |
| `Address`, `TimeZone` | `TimeZone` is an IANA id, e.g. `Europe/Istanbul` |

### `zones` — doc §3.3
A logical or physical area inside a warehouse. `ZoneType` is what the inbound and outbound
flows key off: receiving only accepts stock into a `Receiving` zone, and picked stock is
staged in `Packing` or `ShippingStaging`.

`ZoneType`: `Receiving, QualityControl, ReserveStorage, Picking, Packing, ShippingStaging, Damaged, Returns, ColdStorage, HazardousMaterial`

| Column | Notes |
|---|---|
| `Priority` | Ordering hint when several zones are candidates |

### `location_profiles` — doc §3.5
A shared rule-set for locations with similar characteristics. This is where the constraints
behind rules §11.7 (capacity) and §11.8 (temperature / hazmat) actually live.

| Column | Notes |
|---|---|
| `LocationType` | `SmallBin, StandardShelf, PalletRack, FloorStorage, ColdStorage, DangerousGoods, PickFace, ReserveStorage` |
| `MaxWeight`, `MaxVolume` | Capacity ceiling inherited by locations that do not override it |
| `AllowedItemCategoryId` | `NULL` means any category |
| `TemperatureMin/Max` | The band the location **operates in**. For an item to be storable here this band must sit *inside* the item's acceptable range — see below |
| `IsMixedItemAllowed` | May two different items share a location? |
| `IsMixedLotAllowed` | May two lots of the same item share a location? |

> **Temperature semantics.** A location may sit anywhere within its own band, so every
> temperature it can reach must be acceptable to the item. A −20…−15 °C freezer therefore
> **cannot** store a 2…6 °C item, even though both are "cold" — the location could freeze it.

### `locations` — doc §3.4 + §10
The smallest physical address, e.g. `A-03-B-02` = Aisle A, Bay 03, Level B, Position 02.

`Aisle` is a column here rather than its own table: §3 draws the hierarchy as Zone → Aisle →
Location, but §3.4 lists `Aisle` as a plain Location field and defines no Aisle entity.

| Column | Notes |
|---|---|
| `Code` | Unique within a warehouse |
| `Aisle`, `Bay`, `Level`, `Position` | The decomposed address |
| `LocationProfileId` | Supplies capacity and temperature rules |
| `PickSequence`, `PutawaySequence` | Traversal order; seeds `PickTask.SequenceNumber` |
| `CoordinateX/Y/Z` | **§10** — unused in v1, required for future routing |
| `MaxWeight`, `MaxVolume` | Overrides the profile when set |
| `IsPickable`, `IsPutawayAllowed` | Enforced on putaway |
| `DistanceToReceiving/Packing/Shipping` | **§10** — travel distance in metres |
| `AccessibilityScore` | **§10** — 0–100, higher is easier to reach |
| `MaxConcurrentWorkers` | **§10** — how many pickers fit at once |

### `item_categories` — *inferred*
Referenced by `items.CategoryId` and `location_profiles.AllowedItemCategoryId` but never
defined in the document. Self-nesting via `ParentCategoryId`; cycles are rejected.

### `units_of_measure` — *inferred*
The UOM master behind `items.BaseUomId` and `item_barcodes.UomId`. The document names these
foreign keys and gives Each / Pack / Case / Pallet as examples but never defines the table.
Seeded as `EA`, `PK`, `CS`, `PL`.

### `items` — doc §4.1 + §10
The core product record. **`Sku` is unique per account**, exactly as §4.1 requires.

| Column | Notes |
|---|---|
| `BaseUomId` | Every inventory quantity in the system is stored in this unit |
| `Weight`, `Length`, `Width`, `Height`, `Volume` | **§10** — volume is auto-derived if omitted |
| `IsLotTracked` | Receiving without a lot is refused |
| `IsSerialTracked` | Balance may never exceed 1 unit (rule §11.5) |
| `IsExpirationTracked` | The lot must carry an expiration date (rule §11.6) |
| `ShelfLifeDays` | Used to derive an expiration date when a receipt omits one |
| `IsFragile`, `IsHazardous` | **§10** — hazmat restricts putaway destinations |
| `IsTemperatureControlled`, `MinimumStorageTemperature`, `MaximumStorageTemperature` | **§10** — drives rule §11.8 |
| `StackableQuantity` | |
| `DefaultPutawayZoneId`, `DefaultPickZoneId` | Hints for the future slotting engine |

Tracking flags and the base UOM are **immutable while the item holds stock** — changing them
would strand existing lot/serial records.

### `attribute_definitions` — doc §4.2
The definition half of the dynamic attribute system. Item properties vary by industry, so
they must not become hard-coded `items` columns.

| Column | Notes |
|---|---|
| `DataType` | `Text, Number, Boolean, Date` — decides which value column is used |
| `IsRequired`, `IsFilterable` | |
| `IsSlottingRelevant` | **§4.2** — marks the attribute as future slotting input (e.g. keep Summer stock near the pick face in season) |
| `IsPickingRelevant` | **§4.2** — future picking input (e.g. don't batch fragile with heavy) |

`DataType` is deliberately immutable after creation: changing it would strand every value
already written to the previous type's column.

### `item_attribute_values` — doc §4.2
One row per item per attribute. Four typed columns (`TextValue`, `NumberValue`,
`BooleanValue`, `DateValue`); **exactly one** is populated, chosen by the definition's
`DataType`. Supplying the wrong one is rejected.

The FK to `attribute_definitions` is not in the document's field list but the structure
cannot work without it.

### `item_uoms` — doc §4.3
Per-item UOM conversions and packaging hierarchy. `ConversionQuantity` is **how many base
units one of this UOM contains**, so all maths normalises to the base UOM.

Doc example: `1 Each = 1`, `1 Pack = 6`, `1 Case = 24`. A check constraint enforces
`ConversionQuantity > 0`. The base UOM row is created automatically with every item and
cannot be removed.

### `item_barcodes` — doc §4.4
An item may carry several barcodes, each tied to a UOM (one scans as an Each, another as a
Case). `Barcode` is **globally unique** so a scan resolves to exactly one item; at most one
barcode per item may be `IsPrimary`.

### `suppliers`, `customers` — *inferred*
Referenced by `inbound_orders.SupplierId` and `orders.CustomerId` but never defined in the
document. Modelled minimally: code, name, contact details.

### `users` — *not in the source document*
Authentication was requested separately. BCrypt hash (work factor 12); the plaintext is
never stored or logged. `SecurityStamp` increments on password change to invalidate tokens
issued earlier.

`Role`: `Admin, WarehouseManager, Operator, Viewer`.

---

## Faz 2 — Inventory core

### `inventory_statuses` — doc §5.2
The seven documented usability states, seeded: `AVAILABLE, QUALITY_HOLD, DAMAGED, EXPIRED,
QUARANTINE, RETURNED, BLOCKED`.

Modelled as a **lookup table, not an enum**, because `inventory_balances` and
`inventory_transactions` reference it by `Id`.

| Column | Notes |
|---|---|
| `IsAllocatable` | **This is what mechanically enforces rule §11.4.** Only `AVAILABLE` has it set, so hold/damaged/expired stock can never back an order — without hard-coding status names in business logic |

### `inventory_balances` — doc §5.1
Current stock at one location, in one status, for one lot/serial/LPN combination. The
single most important table in the system.

| Column | Notes |
|---|---|
| `OnHandQuantity` | Physically present |
| `AllocatedQuantity` | Reserved for orders. **Allocation moves this, never `OnHand`** (rule §11.2) |
| `HoldQuantity` | Blocked from allocation without a status change |
| `AvailableQuantity` | **PostgreSQL STORED GENERATED column**: `OnHand − Allocated − Hold`. Computed by the database so the §5.1 formula can never drift from the stored data |
| `ReceivedAt` | **§10** — backs future FIFO |
| `LastMovementAt` | **§10** — backs slow-mover analysis |
| `Version` | Optimistic concurrency token, incremented on every save |

**Uniqueness (§5.1):** `WarehouseId + LocationId + ItemId + InventoryStatusId + LotId +
SerialId + LicensePlateId`.

The document explicitly flags that PostgreSQL's NULL handling needs consideration here, and
it is right: PostgreSQL treats `NULL` as distinct in unique indexes, so a plain index would
permit **unlimited duplicate rows** whenever lot, serial and LPN are all null — the common
case. The index `ux_inventory_balances_unique_combo` is therefore built over
`COALESCE(col, '000…0'::uuid)` sentinels.

Three check constraints guarantee integrity at rest:
- `ck_inventory_balances_non_negative` — no quantity may be negative
- `ck_inventory_balances_available_non_negative` — reserved + held can never exceed on-hand
- `ck_inventory_balances_serial_single_unit` — a serial-tracked row never exceeds 1 (rule §11.5)

### `inventory_transactions` — doc §5.6
**The immutable ledger.** Every stock change writes exactly one row (rule §11.9), and rows
are never updated or deleted (rule §11.10).

Immutability is enforced in **three independent places**: the entity exposes no mutation
path, the `DbContext` rejects `Modified`/`Deleted` entries, and PostgreSQL triggers
(`trg_inventory_transactions_no_update`, `trg_inventory_transactions_no_delete`) block
writes that arrive by any other route — psql, a future service, a careless migration.

| Column | Notes |
|---|---|
| `TransactionType` | `Receipt, Putaway, Movement, Allocation, Deallocation, Pick, Ship, CountAdjustment, Damage, Return, StatusChange` |
| `FromLocationId` / `ToLocationId` | Direction. Stock entering the system has no *from*; stock leaving has no *to* |
| `FromInventoryStatusId` / `ToInventoryStatusId` | Status transitions |
| `Quantity` | Always **positive**; direction comes from the from/to pair and the type |
| `ReferenceType`, `ReferenceId` | Which business document caused it |
| `CorrelationId` | Groups every row written by one business operation, so a multi-leg operation is traceable as a unit |
| `PerformedBy` | Username from the authenticated principal |

### `lots` — doc §5.3
Batch-tracked stock. `LotNumber` is unique per item. Acceptance scenario 4 depends on two
lots of the same item with different expiry staying **separate inventory records** — they do,
because `LotId` is part of the balance uniqueness tuple.

Indexed on `(ItemId, ExpirationDate)` to support FEFO ordering.

### `serial_numbers` — doc §5.4
Individually tracked stock, unique per item. A serial already in stock cannot be received
again.

### `license_plates` — doc §5.5
A pallet, case, tote or other handling unit. **Nesting is supported** via
`ParentLicensePlateId`, exactly as the document prefers: Pallet → Case 1 / Case 2 / Case 3,
and deeper. An LPN travels with its stock — putaway updates `CurrentLocationId`.

---

## Faz 3 — Inbound

### `inbound_orders` — doc §6.1
An expected goods receipt.
Lifecycle: `Draft → Expected → PartiallyReceived → Received → Completed → Cancelled`.

`OrderNumber` is auto-generated as `IB-yyyyMMdd-nnnn` and is unique per account. An order
that already has received stock cannot be cancelled — that needs an auditable adjustment.

### `inbound_order_details` — doc §6.2
Expected lines. `ReceivedQuantity` accumulates in **base UOM**; `ExpectedQuantity` is in the
line's `UomId`.

### `receipts` / `receipt_lines` — doc §6.3
The physical intake. §6.3 describes the process but lists no fields; the header/line split
mirrors the inbound order structure. `PutawayTask.ReceiptLineId` (§6.4) is the document's
only direct evidence that receipts have lines.

Receiving performs all six documented steps in **one database transaction**: verify the
item, capture quantity, capture lot/serial, create an LPN if needed, add stock to the
receiving location, and write the ledger rows.

| Column | Notes |
|---|---|
| `ReceiptNumber` | `RC-yyyyMMdd-nnnn`, globally unique |
| `ReceivingLocationId` | Must be in a `Receiving` zone |
| `BaseQuantity` | `ReceivedQuantity` normalised to the item's base UOM |
| `PutawayQuantity` | Already covered by putaway tasks, so a second call cannot double-book |

Over-receipt beyond the expected quantity is **refused** — see the assumptions log.

### `putaway_tasks` — doc §6.4
Moves received stock from receiving to storage or picking.

| Column | Notes |
|---|---|
| `SuggestedLocationId` | **Stays `NULL` in v1.** Reserved for the future slotting engine |
| `ActualLocationId` | Where the operator actually put it |
| `RecommendationReason` | **Stays `NULL` in v1** — why the engine suggested its location |

Completion checks rule §11.8 (temperature and hazmat compatibility) plus the profile's
mixing rules before moving anything.

---

## Faz 4 — Outbound

### `orders` — doc §7.1 + §10
A customer order. Lifecycle: `Draft → Created → Released → PartiallyAllocated → Allocated →
Picking → Picked → Packed → Shipped → Cancelled`.

`OrderType`: `Standard, Express, Wholesale, Retail, Transfer, ReturnReplacement`.

| Column | Notes |
|---|---|
| `Priority` | **§10** — lower is more urgent |
| `RequiredShipDate`, `Carrier`, `ServiceLevel` | **§10** — future batching inputs |
| `TotalWeight`, `TotalVolume`, `TotalLineCount`, `TotalQuantity` | **§10** — denormalised, recomputed from lines |

### `order_details` — doc §7.2
Order lines. `AllocatedQuantity`, `PickedQuantity` and `ShippedQuantity` are in base UOM.

| Column | Notes |
|---|---|
| `RequiredLotNumber` | Restricts allocation to that lot |
| `RequiredSerialNumber` | Restricts allocation to that serial |
| `MinimumShelfLifeDays` | Excludes lots expiring too soon — the groundwork for FEFO |

A check constraint enforces the progression
`Shipped ≤ Picked ≤ Allocated ≤ Ordered`, so no code path can invert it.

### `inventory_allocations` — doc §7.3
Which stock records satisfy an order line. Creating one leaves `OnHandQuantity` untouched,
raises `AllocatedQuantity`, and therefore lowers `AvailableQuantity`.

| Column | Notes |
|---|---|
| `InventoryBalanceId` | The exact balance row reserved from. **Repointed to the staging balance when the pick completes**, so shipment consumes stock from where it actually is |
| `AllocationStrategy` | `Manual, Fifo, Fefo, Lifo` — recorded so a future engine can be compared against today's behaviour |

### `pick_tasks` — doc §7.4
One task per allocation, so a picker is always told exactly which stock to take from where.
Lifecycle: `Created → Assigned → InProgress → Completed / ShortPicked / Cancelled`.

| Column | Notes |
|---|---|
| `SequenceNumber` | Seeded from `Location.PickSequence` |
| `PickBatchId` | **Stays `NULL` in v1.** Reserved for the future batching engine |
| `PickedQuantity` | Less than `Quantity` means a short pick |

Completing a pick moves stock to staging **while keeping it reserved** — on-hand and
allocated fall at the source and rise at the destination together. A short pick releases the
unpicked remainder back to available.

### `shipments` / `shipment_lines` — *inferred*
Backs `POST /api/orders/{id}/ship`. Faz 4 lists "Shipment" as a deliverable but defines no
fields. Records exactly what left the building, with carrier and tracking.

Shipping reduces **both** on-hand and allocated (rule §11.3) and writes the order-history
rows below.

---

## Faz 5 — Inventory control

> **Section 9 of the source document is missing** — it jumps from §8 to §10. These three
> tables are reconstructed by analogy with the documented inbound/outbound task patterns.
> See [ASSUMPTIONS.md §E](ASSUMPTIONS.md) for every inference.

### `count_plans`
Defines **what** to count. Releasing it generates the tasks.

| Column | Notes |
|---|---|
| `PlanNumber` | `CP-yyyyMMdd-nnnn` |
| `CountType` | `Cycle, Full, Spot` |
| `SelectionMode` | `ByLocation, ByZone, ByItem, ByWarehouse` |
| `BlockAllocationDuringCount` | Reserved; `false` by default so counting never disrupts outbound |

### `count_tasks`
One task per `inventory_balances` row in scope, matching the §5.1 tuple — so a variance
always resolves to exactly one balance.

| Column | Notes |
|---|---|
| `SystemQuantity` | The **blind** snapshot taken at generation. Not shown to the counter: a count anchored to the expected number is not evidence |
| `CountedQuantity`, `CountedBy`, `CountedAt` | What was found, by whom, when |
| `InventoryAdjustmentId` | Set when a variance raised an adjustment |

Status `VarianceFound` holds the task open until its adjustment is decided.

### `inventory_adjustments`
The approval gate implied by `POST /api/inventory-adjustments/{id}/approve`.
**Stock only changes on approval** — one auditable moment of truth.

| Column | Notes |
|---|---|
| `AdjustmentNumber` | `ADJ-yyyyMMdd-nnnn` |
| `Reason` | `CountVariance, Damage, Expiry, Loss, Found, SystemCorrection, Return` |
| `SystemQuantity` | On-hand **when the adjustment was raised** |
| `CountedQuantity` | The corrected quantity being proposed |
| `QuantityBeforeApproval` | Actual on-hand read **under a row lock at approval** |
| `QuantityAfterApproval` | Actual on-hand immediately after |
| `DriftedBeforeApproval` | `true` when stock moved between raising and approval |
| `RequestedBy` / `ApprovedBy` / `ApprovedAt` / `RejectionReason` | The audit trail |
| `InventoryTransactionId` | The ledger row the approval produced |

> **Why before/after are separate from `SystemQuantity`:** the snapshot may be days old by
> approval time. Recording the locked-read values means the trail states what *actually*
> happened, not what was expected to.

Two controls beyond the documented rules, both flagged as assumptions: **separation of
duties** (the requester cannot approve their own) and **reserved stock is protected** (a
write-off cannot consume allocated or held quantity).

---

## Faz 6 — Algorithm readiness

> Doc §10 is explicit that algorithms are **not** built in v1 and that what matters is
> storing the data they will need. Nothing here makes a decision.

### `algorithm_configurations`
Tunable parameters a future engine will read, so retuning needs no code change.
`ParameterValue` is text, parsed per `ValueType` (`string, int, decimal, bool, json`); a
value that does not parse is rejected. `Version` bumps on update. A `NULL` `WarehouseId`
means the setting applies account-wide.

### `slotting_recommendations`
Where a future engine would suggest an item be stored, and — critically — `WasAccepted`,
`DecidedBy`, `DecidedAt`. That acceptance rate is how recommendation quality gets measured
against reality.

### `picking_plans` / `picking_route_stops`
A batch of pick work plus the route to walk it. Stores `EstimatedTravelDistance` alongside
`ActualTravelDistance` so planned-versus-actual can be compared once an engine exists.
Stop sequence is unique within a plan.

### `order_history`
Written automatically on every shipment: one denormalised row per shipped line. Live order
tables get purged; slotting and batching need a stable demand history.

| Column | Notes |
|---|---|
| `Sku` | Denormalised, so history stays readable if the SKU is later renamed |
| `OrderLineCount` | Basis for co-pick affinity analysis |
| `PickedFromLocationId` | For travel analysis |
| `FulfillmentDurationSeconds` | Order-to-ship duration |

Feeds `GET /api/algorithms/demand-stats`, which derives per-item **lines per day** — the
primary slotting input.
