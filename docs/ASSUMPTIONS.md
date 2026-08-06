# Assumptions Log

Every design decision in this system traces back to `Inventory Management System.pdf`
(the source specification). Where the document is silent, ambiguous, or self-contradictory,
the decision taken is recorded here with its justification.

**Rule followed throughout:** no feature exists that the documentation does not call for.
Where something had to be invented to make a documented feature work, it is listed below.

---

## A. Confirmed with the project owner

These four were raised before implementation and answered directly.

| # | Question | Decision |
|---|----------|----------|
| A1 | **Section 9 is missing from the source PDF** (it jumps §8 → §10), yet cycle counting and stock adjustment are required by §1, the §12 API list and Faz 5. | Design `CountPlan`, `CountTask` and `InventoryAdjustment` by analogy with the documented inbound/outbound task patterns. Every inferred field is flagged below. |
| A2 | The spec contains **no authentication or authorization requirements at all**. | JWT bearer tokens, role-based authorization (Admin / WarehouseManager / Operator / Viewer), and every query scoped to the caller's `AccountId`. |
| A3 | Frontend technology. | Static HTML + vanilla JS served from `wwwroot`. No framework, no build step — it exists only to demonstrate the backend. |
| A4 | Faz 6 names four tables but specifies no fields; §10 does list concrete data-capture fields. | Implement all §10 fields concretely. Create the Faz 6 tables with a reasonable schema, populated but **not read by any algorithm** — §10 explicitly defers algorithms. |

---

## B. Entities referenced by the document but never defined

The spec names these foreign keys without ever specifying the table behind them.
Each is modelled minimally — just enough for referential integrity.

| Entity | Referenced by | What was assumed |
|--------|---------------|------------------|
| `UnitOfMeasure` | `ItemMaster.BaseUomId` (§4.1), `ItemBarcode.UomId` (§4.4), order/inbound detail lines | A UOM master table. §4.3 gives Each / Pack / Case / Pallet as examples, which are seeded as `EA` / `PK` / `CS` / `PL`. |
| `ItemCategory` | `ItemMaster.CategoryId` (§4.1), `LocationProfile.AllowedItemCategory` (§3.5) | Code + name, optional self-nesting for a category tree. `AllowedItemCategory` is modelled as an FK rather than free text. |
| `Supplier` | `InboundOrder.SupplierId` (§6.1) | Code, name, and contact fields. |
| `Customer` | `OrderMaster.CustomerId` (§7.1) | Code, name, contact and shipping address. |
| `Shipment` / `ShipmentLine` | `POST /api/orders/{id}/ship` (§12), Faz 4 "Shipment" | Header + lines recording exactly what left the building, with carrier and tracking. |
| `ApplicationUser` | Nothing — required by A2 | Username, email, BCrypt hash, role, account, security stamp. |

---

## C. Fields added to documented entities

| Entity | Field added | Why |
|--------|-------------|-----|
| `ItemAttributeValue` | `AttributeDefinitionId` | §4.2's field list omits the FK to the definition, but the dynamic attribute system cannot function without it. |
| `ItemUom` | `UomId` | §4.3's field list omits it; the row is meaningless without knowing which UOM it converts. |
| `LocationProfile` | `AccountId` | §3.5 omits it; needed to keep profiles tenant-isolated per §3. |
| `InboundOrder` | `AccountId` | §6.1 omits it; §3 recommends `AccountId` on every table. |
| `InboundOrderDetail` | `LineNumber` | Needed for stable ordering and display. |
| `Receipt` / `ReceiptLine` | Full field set | §6.3 describes the receiving *process* but lists no fields. `PutawayTask.ReceiptLineId` (§6.4) is the only evidence receipts have lines. |
| `InventoryTransaction` | `Notes` | Carries the adjustment reason / short-pick explanation. |
| `InventoryBalance` | — | All §5.1 fields implemented as written. |
| `OrderMaster` | `TotalLineCount`, `TotalQuantity`, `ReleasedAt`, `ShippedAt` | §10 requires "siparişin toplam satır, adet, ağırlık ve hacim bilgisi" for future batching; §7.1 lists only weight and volume. |
| `Location` | `DistanceToReceiving/Packing/Shipping`, `AccessibilityScore`, `MaxConcurrentWorkers` | Listed in §10 but omitted from §3.4's field list. |
| `InventoryAllocation` | `PickedQuantity` | Needed to track partial picking against an allocation. |
| `PickTask` | `PickedQuantity`, `Notes` | `ShortPicked` status (§7.4) is meaningless without recording how much was actually picked. |

---

## D. Status lifecycles the document names but does not enumerate

The spec gives explicit state lists for `InboundOrder` (§6.1), `OrderMaster` (§7.1) and
`PickTask` (§7.4). These three entities have a `Status` field with no states listed:

| Entity | States assumed | Modelled on |
|--------|----------------|-------------|
| `PutawayTask` (§6.4) | Created → Assigned → InProgress → Completed / Cancelled | The documented `PickTask` lifecycle, minus ShortPicked. |
| `OrderDetail` (§7.2) | Open → PartiallyAllocated → Allocated → Picking → Picked → Shipped / Cancelled | A line-level mirror of the documented order lifecycle. |
| `InventoryAllocation` (§7.3) | Allocated → Picked → Shipped / Cancelled | The allocation's own progression through the outbound flow. |

`Lot.Status` (§5.3), `SerialNumber.Status` (§5.4) and `LicensePlate.Status` (§5.5) are
likewise named without values; sensible warehouse-standard sets were used.

---

## E. Section 9 reconstruction (counting and adjustment)

Section 9 is absent from the source document. The following was designed by analogy.
**This is the largest single body of inference in the project.**

**Evidence the module is in scope:**
- §1: "Sayım ve stok düzeltme işlemlerini desteklemeli."
- §12: `POST /api/count-plans`, `POST /api/count-tasks/{id}/complete`, `POST /api/inventory-adjustments/{id}/approve`
- Faz 5: "Inventory adjustment", "Hold ve damaged işlemleri", "Inventory transaction raporu"

**What was inferred:**

| Decision | Reasoning |
|----------|-----------|
| A plan/task split (`CountPlan` → `CountTask`) | Mirrors the documented `InboundOrder` → `PutawayTask` and `OrderMaster` → `PickTask` patterns. The §12 endpoints name both a plan and a task, confirming two levels. |
| One `CountTask` per location + item + status + lot/serial/LPN | Matches the `InventoryBalance` uniqueness tuple (§5.1), so a variance pins to exactly one balance row. |
| `CountType`: Cycle / Full / Spot | Standard WMS counting modes; §1 says "sayım" without qualifying it. |
| `CountSelectionMode`: ByLocation / ByZone / ByItem / ByWarehouse | How a plan chooses its scope. Not documented. |
| Adjustment has a **Pending → Approved** gate | Directly implied by the endpoint name `/inventory-adjustments/{id}/approve`. |
| **Stock is only changed on approval**, not when the adjustment is raised | Rule §11.9 requires every stock change to write a transaction; deferring the change to approval keeps a single, auditable moment of truth. |
| `AdjustmentReason` enum | Faz 5 pairs adjustments with "hold ve damaged işlemleri", implying a reason is recorded. |
| A variance count raises an adjustment rather than moving stock directly | Keeps the immutable-ledger guarantee (§11.10) and the approval gate consistent. |
| **Blind counting**: the counter is not shown `SystemQuantity` | A count anchored to the expected number is not evidence. The snapshot is stored on the task for comparison, but is not meant to be surfaced to the person counting. |
| A **no-variance** count closes the task immediately; a variance holds it open until the adjustment is decided | The task's purpose is to resolve a discrepancy, so it is not finished while one is outstanding. |
| **Separation of duties**: the person who raised an adjustment cannot approve it | Not in the document. An approval gate that the requester can satisfy themselves is not a control. Role policy already prevents an Operator approving; this additionally stops an Admin or Manager self-approving. Relax by removing the check in `CountingService.ApproveAdjustmentAsync` if a single-operator warehouse is intended. |
| A `ByLocation` plan's location list is supplied at **release** time, not stored on the plan | Avoids a `CountPlanLocation` join table the document never mentions, and avoids server-side state between the two calls. |
| Locations in scope holding **no stock** generate no task | Counting nothing produces no evidence either way. A "should be empty and is" check would need a documented expectation the spec does not define. |
| A count task targets **one `InventoryBalance` row**, not a location | Matches the §5.1 uniqueness tuple, so a variance always resolves to exactly one balance and one lot/serial/LPN. |

### §9 audit trail

The workflow records, and the `GET /api/inventory-adjustments/{id}/audit` endpoint returns:

| Recorded | Where |
|----------|-------|
| Who created the count plan, and when | `CountPlan.CreatedBy` / `CreatedAt` |
| Who released it, and when | `CountPlan.UpdatedBy` / `ReleasedAt` |
| The blind system snapshot per task | `CountTask.SystemQuantity` |
| Who counted, what they found, and when | `CountTask.CountedBy` / `CountedQuantity` / `CountedAt` |
| Who raised the adjustment, and when | `InventoryAdjustment.RequestedBy` / `CreatedAt` |
| Who approved or rejected it, and when | `InventoryAdjustment.ApprovedBy` / `ApprovedAt` |
| Why it was rejected | `InventoryAdjustment.RejectionReason` |
| **Inventory before the change** | `QuantityBeforeApproval` — read under a row lock at approval, **not** the snapshot from raise time |
| **Inventory after the change** | `QuantityAfterApproval` |
| Whether stock moved between raising and approval | `DriftedBeforeApproval` |
| The ledger row the change produced | `InventoryTransactionId` → immutable `InventoryTransaction` (§11.10) |

**Why before/after are separate from `SystemQuantity`:** `SystemQuantity` is a snapshot taken when the adjustment was raised, which may be minutes or days before approval. Stock can legitimately move in between. The approver signs off on a *target* quantity, so that target is what gets applied, the true before/after values are recorded from the locked row, and any drift is flagged rather than silently absorbed.

---

## F. Interpretation decisions on documented requirements

| Topic | Decision | Reasoning |
|-------|----------|-----------|
| **Aisle** | A column on `Location`, not its own table | §3 draws the hierarchy as Zone → Aisle → Location, but §3.4 lists `Aisle` as a plain `Location` field and defines no Aisle entity. The field list wins. |
| **`InventoryStatus`** | A lookup **table**, not an enum | `InventoryBalance.InventoryStatusId` and `InventoryTransaction.From/ToInventoryStatusId` (§5.1, §5.6) reference it by Id. The seven §5.2 statuses are seeded. |
| **`IsAllocatable` flag on status** | Added | This is what mechanically enforces rule §11.4 ("hold, damaged or expired stock must not be allocated to normal orders") rather than hard-coding status names in business logic. |
| **`AvailableQuantity`** | A PostgreSQL **stored generated column** | §5.1 gives it as a formula. Making the database compute it means the stored value can never drift from `OnHand − Allocated − Hold`. |
| **§5.1 NULL uniqueness** | Unique index built over `COALESCE(col, '000…0')` | §5.1 explicitly flags this: *"Null değerler için PostgreSQL unique index davranışı ayrıca değerlendirilmelidir."* PostgreSQL treats NULLs as distinct, so a plain unique index would permit unlimited duplicate rows whenever lot/serial/LPN are all null — the common case. |
| **Concurrency (§11.11)** | Optimistic `Version` token **plus** `SELECT … FOR UPDATE` row locks | The `Version` column (§5.1) alone only detects a conflict at save time; for allocation two callers could both read "10 available" and both take 8. Row locks serialise them at read time so the second correctly sees a shortfall. |
| **Immutable ledger (§11.10)** | Enforced in three places | The entity exposes no mutation path, the `DbContext` rejects Modified/Deleted entries, and PostgreSQL triggers block UPDATE/DELETE from any other route. |
| **Quantity normalisation** | All stock quantities stored in the item's **base UOM** | §4.3 defines conversions but the spec never says which unit balances are held in. Normalising avoids ambiguity when an item is received in Cases and picked in Eaches. |
| **Allocation source ordering** | FEFO-then-FIFO (soonest expiry, then oldest receipt) | §10 defers algorithms, so this is *deterministic ordering*, not a strategy engine. `AllocationStrategy` is recorded on every allocation so a real algorithm can be introduced later without schema change. |
| **Deactivate rather than delete** | Master data is soft-deactivated | Warehouses, locations and items are referenced by immutable transactions (§11.10); a hard delete would break the audit trail. |
| **`ShelfLifeDays` required when expiration-tracked** | Enforced | Rule §11.6 makes expiration dates mandatory for such items; a shelf life gives the system a way to derive one when a receipt does not supply it. |
| **Expiration implies lot tracking** | Enforced | §5.3 places `ExpirationDate` on `Lot`, so there is nowhere else to store it. |

---

## G. Deliberately NOT built

| Not built | Why |
|-----------|-----|
| Slotting, putaway, picking-routing, order-batching algorithms | §10 states these are explicitly out of scope for v1: *"İlk sürümde algoritmalar geliştirilmeyecek olsa bile aşağıdaki veriler saklanmalıdır."* The **data** they need is captured in full. |
| Packing as a distinct process | §7.1 lists `Packed` as an order status, but no packing entity, workflow or endpoint is specified. Implemented as a status transition only. |
| Inspection / QC workflow | §1 mentions inspection as part of a general enterprise WMS inbound flow, but no entity, field or endpoint is specified. Stock can be received into `QualityHold` status, which is the documented mechanism. |
| Wave / task management, labour tracking, replenishment | Never mentioned in the document. |
| Multi-warehouse stock transfer orders | `OrderType.Transfer` exists per §7.1, but no transfer-specific workflow is described. |

---

## H. Environment deviations

| Spec asks for | Reality | Handling |
|---------------|---------|----------|
| .NET 8 | Only .NET **SDK 9** installed locally; .NET 8 **runtime** present | All projects target `net8.0`, which SDK 9 builds correctly. Verified. |
| Docker | **Not installed** on this machine | `Dockerfile` and `docker-compose.yml` are written to spec but could not be executed here. |
| PostgreSQL | v15 installed, service **stopped** and requires elevation to start | The service must be started manually; see `README.md`. |
