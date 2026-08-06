# User Guide — Beginning to End

This walks the system from an empty database to a shipped order and a completed stock count.
Follow it in order; each step builds on the previous one.

Two ways to work through it:

- **Demo UI** — <http://localhost:5080> — every step below has a screen
- **Swagger** — <http://localhost:5080/swagger> — every endpoint, with schemas

Both do exactly the same thing; the UI just calls the API.

---

## 0. Sign in

Everything except `/health` requires a token.

```
POST /api/auth/login
{ "username": "admin", "password": "<your seeded password>" }
```

The response carries `accessToken`. Send it on every later call:

```
Authorization: Bearer <accessToken>
```

In Swagger, click **Authorize** and paste the token **without** the `Bearer ` prefix.

### Roles

| Role | Can do |
|---|---|
| **Admin** | Everything, including user administration |
| **WarehouseManager** | Master data, all operations, **approve adjustments** |
| **Operator** | Floor work: receive, putaway, pick, move, count. *Cannot* approve adjustments |
| **Viewer** | Read-only |

The seeded `admin` is the only user initially. Create the others:

```
POST /api/auth/users
{ "username": "operator1", "email": "op1@ims.local", "fullName": "Floor Operator",
  "password": "...", "role": "Operator", "defaultWarehouseId": null }
```

> You will need **two** people to complete section 6: an adjustment cannot be approved by
> the person who raised it. Create a `WarehouseManager` as well as an `Operator`.

---

## 1. Build the warehouse (Faz 1)

Order matters — each step references the previous.

### 1.1 Warehouse

```
POST /api/warehouses
{ "code": "WH01", "name": "Istanbul Main Depot",
  "address": "Tuzla OSB", "timeZone": "Europe/Istanbul" }
```

### 1.2 Zones

Create at least a **Receiving** zone (inbound refuses to land stock anywhere else) and a
storage zone. A **Packing** or **ShippingStaging** zone is needed for picking.

```
POST /api/zones
{ "warehouseId": "…", "code": "RCV", "name": "Receiving Dock",
  "zoneType": "Receiving", "priority": 1 }
```

Repeat for `RSV` (`ReserveStorage`), `PCK` (`Picking`), `PACK` (`Packing`),
`SHIP` (`ShippingStaging`), and `COLD` (`ColdStorage`) if you handle chilled goods.

### 1.3 Location profiles

A profile carries the capacity and temperature rules that locations inherit.

```
POST /api/location-profiles
{ "code": "PALLET-STD", "name": "Standard Pallet Rack", "locationType": "PalletRack",
  "maxWeight": 1200, "maxVolume": 2.5, "allowedItemCategoryId": null,
  "temperatureMin": null, "temperatureMax": null,
  "isMixedItemAllowed": true, "isMixedLotAllowed": true }
```

For chilled storage set the band to what the location **operates at**:

```json
{ "code": "COLD-SHELF", "locationType": "ColdStorage",
  "temperatureMin": 2, "temperatureMax": 5, ... }
```

> **Get this the right way round.** The location's band must fit *inside* the item's
> acceptable range. A −20…−15 °C freezer will be refused for a 2…6 °C item, because the
> location could freeze it. This trips people up.

### 1.4 Locations

```
POST /api/locations
{ "warehouseId": "…", "zoneId": "<RCV zone>", "code": "RECV-01",
  "locationType": "FloorStorage", "locationProfileId": null,
  "pickSequence": 1, "putawaySequence": 1,
  "coordinateX": 0, "coordinateY": 0, "coordinateZ": 0,
  "isPickable": false, "isPutawayAllowed": true,
  "distanceToReceiving": 0, "distanceToPacking": 20, "distanceToShipping": 25,
  "accessibilityScore": 90, "maxConcurrentWorkers": 2 }
```

The coordinate, distance, sequence and accessibility fields are unused today. Fill them in
anyway — §10 requires them, and `GET /api/algorithms/readiness` reports on whether you did.

### 1.5 Items

```
POST /api/items
{ "sku": "TSHIRT-001", "name": "Cotton T-Shirt Black XL",
  "baseUomId": "<EA>", "weight": 0.2,
  "length": 0.3, "width": 0.25, "height": 0.02,
  "isLotTracked": false, "isSerialTracked": false, "isExpirationTracked": false,
  "isFragile": false, "isHazardous": false, "isTemperatureControlled": false,
  "stackableQuantity": 20 }
```

Look up UOM ids from `GET /api/units-of-measure` (`EA`, `PK`, `CS`, `PL` are seeded).

**Tracking flags decide what receiving demands later:**

| Flag | Consequence |
|---|---|
| `isLotTracked` | Every receipt must supply a lot number |
| `isSerialTracked` | Every receipt must supply a serial; the balance can never exceed 1 |
| `isExpirationTracked` | The lot must end up with an expiration date — supply one, or set `shelfLifeDays` so the system derives it |
| `isTemperatureControlled` | Must declare `minimumStorageTemperature` and/or `maximumStorageTemperature`; putaway is then restricted |
| `isHazardous` | Putaway restricted to `DangerousGoods` locations or a `HazardousMaterial` zone |

These flags become **immutable once the item holds stock**.

A chilled, lot- and expiry-tracked example:

```json
{ "sku": "MILK-1L", "name": "Full Fat Milk 1L", "baseUomId": "<EA>",
  "isLotTracked": true, "isExpirationTracked": true, "shelfLifeDays": 14,
  "isTemperatureControlled": true,
  "minimumStorageTemperature": 2, "maximumStorageTemperature": 6 }
```

### 1.6 Packaging and barcodes

```
POST /api/items/{id}/uoms
{ "uomId": "<CS>", "conversionQuantity": 24, "isReceivingUom": true,
  "isPickingUom": false, "isShippingUom": true }
```

`conversionQuantity` is **how many base units** one of this UOM contains. Receive 10 Cases
and 240 units land in stock.

```
POST /api/items/{id}/barcodes
{ "uomId": "<EA>", "barcode": "8691234567890",
  "barcodeType": "Ean13", "isPrimary": true }
```

Scan resolution: `GET /api/items/by-barcode/8691234567890`.

### 1.7 Dynamic attributes

Industry-specific properties, without adding columns:

```
POST /api/attribute-definitions
{ "code": "SEASON", "name": "Season", "dataType": "Text",
  "isRequired": false, "isFilterable": true,
  "isSlottingRelevant": true, "isPickingRelevant": false }

PUT /api/items/{id}/attributes
{ "attributeDefinitionId": "…", "textValue": "Summer" }
```

`isSlottingRelevant` / `isPickingRelevant` mark which attributes a future algorithm should
consider — e.g. keep Summer stock near the pick face in season.

### 1.8 Trading partners

```
POST /api/suppliers   { "code": "SUP01", "name": "Anadolu Tekstil A.S." }
POST /api/customers   { "code": "CUST01", "name": "Migros Ticaret" }
```

---

## 2. Get stock in — the fast way (Faz 2)

For opening balances or testing, book stock straight into a location:

```
POST /api/inventory/manual-entry
{ "warehouseId": "…", "locationId": "<A-01-A-01>", "itemId": "…",
  "quantity": 100, "lotNumber": null, "expirationDate": null, "notes": "Opening stock" }
```

For a lot-tracked item supply `lotNumber`; the lot record is created automatically. If the
item is expiration-tracked and you omit `expirationDate`, it is derived from `shelfLifeDays`.

Check the result:

```
GET /api/inventory?WarehouseId=…
GET /api/inventory/by-item/{itemId}      # ordered FEFO then FIFO
GET /api/inventory/by-location/{id}
GET /api/inventory/summary?warehouseId=… # aggregated per item
```

Every balance shows `onHandQuantity`, `allocatedQuantity`, `holdQuantity` and
`availableQuantity`. The last is computed by PostgreSQL as `OnHand − Allocated − Hold` and
cannot drift.

### Moving stock

```
POST /api/inventory/movements
{ "warehouseId": "…", "itemId": "…",
  "fromLocationId": "…", "toLocationId": "…",
  "quantity": 30, "inventoryStatusId": "<AVAILABLE>" }
```

Source decrement, destination increment and the ledger entry all happen in **one database
transaction**. Insufficient stock aborts the whole thing.

### Changing status

```
POST /api/inventory/status-change
{ "warehouseId": "…", "itemId": "…", "locationId": "…",
  "fromInventoryStatusId": "<AVAILABLE>", "toInventoryStatusId": "<DAMAGED>",
  "quantity": 10, "notes": "Forklift damage" }
```

Damaged stock is still physically present but is **no longer allocatable** — that is rule
§11.4, enforced by the `IsAllocatable` flag on the status.

### Holding stock

```
POST /api/inventory/hold           { "inventoryBalanceId": "…", "quantity": 15 }
POST /api/inventory/release-hold   { "inventoryBalanceId": "…", "quantity": 15 }
```

A hold blocks allocation **without** changing status or on-hand.

---

## 3. Get stock in properly — inbound (Faz 3)

### 3.1 Create the order

```
POST /api/inbound-orders
{ "warehouseId": "…", "supplierId": "…", "expectedArrivalDate": "2026-08-10T08:00:00Z",
  "lines": [ { "itemId": "…", "expectedQuantity": 120, "uomId": "<EA>",
               "expectedLotNumber": "LOT-A", "expectedExpirationDate": "2026-08-25T00:00:00Z" } ] }
```

Auto-numbered `IB-yyyyMMdd-nnnn`, created in `Draft`.

### 3.2 Confirm it

```
POST /api/inbound-orders/{id}/confirm      →  Expected
```

Receiving against a `Draft` order is refused — this keeps half-entered orders off the
receiving screen.

### 3.3 Receive

```
POST /api/inbound-orders/{id}/receive
{ "receivingLocationId": "<RECV-01>", "notes": "Truck TR-01",
  "lines": [ { "inboundOrderDetailId": "…", "receivedQuantity": 120,
               "lotNumber": "LOT-A", "expirationDate": "2026-08-25T00:00:00Z",
               "supplierLotNumber": "SUP-A1", "inventoryStatusId": null } ] }
```

This performs all six §6.3 steps in one transaction: verify, capture quantity, capture
lot/serial, create an LPN if needed, land the stock, write the ledger.

- The location **must** be in a `Receiving` zone.
- Receiving more than expected is **refused**.
- Receive part of a line and the order becomes `PartiallyReceived`; finish it and it becomes `Received`.
- To hold goods for inspection, pass `inventoryStatusId` = `QUALITY_HOLD`. The stock exists but cannot be allocated.

### 3.4 Put it away

```
POST /api/receipts/{receiptId}/create-putaway     # empty body = every outstanding line
```

Then, per task:

```
POST /api/putaway-tasks/{id}/complete
{ "actualLocationId": "<A-01-A-01>", "quantity": null }
```

`quantity: null` means the whole task. A smaller number leaves the remainder pending so
another task can be raised.

Putaway enforces rule §11.8 before moving anything: hazardous goods need a
`DangerousGoods` location or `HazardousMaterial` zone; temperature-controlled goods need a
profile whose band fits inside the item's requirement; the destination must have
`isPutawayAllowed`.

> In v1 **you** choose the destination. `suggestedLocationId` and `recommendationReason`
> stay `null` — they are reserved for the future slotting engine (§6.4).

### 3.5 Close it

```
POST /api/inbound-orders/{id}/complete     →  Completed
```

---

## 4. Get stock out — outbound (Faz 4)

The full chain is **Create → Confirm → Release → Allocate → Create pick tasks → Complete
picks → Ship**.

### 4.1 Create

```
POST /api/orders
{ "warehouseId": "…", "customerId": "…",
  "requiredShipDate": "2026-08-20T12:00:00Z",
  "carrier": "Yurtici", "serviceLevel": "Standard", "priority": 100,
  "orderType": "Standard",
  "lines": [ { "itemId": "…", "orderedQuantity": 50, "uomId": "<EA>",
               "requiredLotNumber": null, "requiredSerialNumber": null,
               "minimumShelfLifeDays": null } ] }
```

Auto-numbered `SO-yyyyMMdd-nnnn`. Optional line constraints:

| Field | Effect |
|---|---|
| `requiredLotNumber` | Only that lot may be allocated |
| `requiredSerialNumber` | Only that serial |
| `minimumShelfLifeDays` | Lots expiring sooner are excluded |

### 4.2 Confirm and release

```
POST /api/orders/{id}/confirm    →  Created
POST /api/orders/{id}/release    →  Released
```

Only a released order can be allocated.

### 4.3 Allocate

```
POST /api/orders/{id}/allocate        # empty body allocates every open line
```

Allocation **reserves** stock: `allocatedQuantity` rises, `onHandQuantity` does **not**
change, `availableQuantity` falls. Only `AVAILABLE` stock is a candidate, ordered
**soonest-expiring first, then oldest-received** (FEFO then FIFO).

The response tells you what happened:

```json
{ "status": "Allocated", "isFullyAllocated": true,
  "allocations": [ … ], "shortfalls": [] }
```

**If there isn't enough stock** the call still succeeds, but honestly:

```json
{ "status": "PartiallyAllocated", "isFullyAllocated": false,
  "shortfalls": [ { "sku": "TSHIRT-001", "requestedQuantity": 1064,
                    "allocatedQuantity": 564, "shortQuantity": 500 } ] }
```

The order does **not** reach `Allocated`, stock never goes negative, and you are told
exactly which item is short and by how much. Lines that *could* be covered still are.

To refuse partial allocation outright, send `{ "allowPartial": false }` — you get HTTP 422
with the same shortfall detail and nothing is reserved.

To release reservations: `POST /api/orders/{id}/deallocate`.

### 4.4 Create pick tasks

```
POST /api/orders/{id}/create-pick-tasks
{ "destinationLocationId": "<PACK-01>", "assignTo": "operator1" }
```

One task per allocation, sequenced by the source location's `pickSequence`.

### 4.5 Pick

```
POST /api/pick-tasks/{id}/complete
{ "pickedQuantity": null, "notes": null }
```

`null` picks the full quantity. Picked stock moves to the destination **while staying
reserved** — allocated and on-hand fall at the source and rise at staging together, so it is
never offered to another order, and total on-hand is unchanged until shipment.

**Short pick:** pass a smaller `pickedQuantity`. The task closes as `ShortPicked` and the
unpicked remainder is **released back to available stock**, rather than sitting reserved for
a pick that already failed.

### 4.6 Ship

```
POST /api/orders/{id}/ship
{ "carrier": "Yurtici", "serviceLevel": "Standard", "trackingNumber": "TRK-0001" }
```

This is where stock actually leaves: on-hand **and** allocated both fall (rule §11.3). The
order becomes `Shipped`, a `SH-yyyyMMdd-nnnn` shipment is created, and one `order_history`
row is written per line for future demand analysis.

A short-picked order still ships what was actually picked.

---

## 5. Check the ledger

```
GET /api/inventory/transactions?WarehouseId=…&PageSize=100
```

Filter by `TransactionType`, `ItemId`, `LocationId`, `ReferenceId`, `CorrelationId` or a
date range.

Every stock change in the walkthrough above appears here — `Receipt`, `Putaway`,
`Allocation`, `Pick`, `Ship` — each naming the actor and the document that caused it.
`CorrelationId` groups every row written by one operation, so a multi-line receipt is
traceable as a unit.

**The ledger cannot be altered.** Updates and deletes are blocked by database triggers, not
just application code. Try it in psql and PostgreSQL will refuse.

---

## 6. Count the stock (Faz 5)

> Reconstructed: §9 is missing from the source document. See
> [ASSUMPTIONS.md §E](ASSUMPTIONS.md).

### 6.1 Create and release a plan

```
POST /api/count-plans
{ "warehouseId": "…", "name": "Weekly cycle count", "countType": "Cycle",
  "selectionMode": "ByLocation", "locationIds": ["<A-01-A-01>"] }

POST /api/count-plans/{id}/release
["<A-01-A-01>"]
```

Releasing generates **one task per stock record** in scope. `selectionMode` may be
`ByLocation`, `ByZone`, `ByItem` or `ByWarehouse`.

### 6.2 Count

```
POST /api/count-tasks/{id}/complete
{ "countedQuantity": 53, "notes": "Two cases missing" }
```

Counting is **blind** — the demo UI shows `hidden` for the system quantity until you submit.

- **Count matches** → task closes as `Completed`. Nothing else happens.
- **Count differs** → task becomes `VarianceFound` and an adjustment is raised for approval.

Stock does **not** change here.

### 6.3 Approve or reject

```
POST /api/inventory-adjustments/{id}/approve      { "notes": "Verified with supervisor" }
POST /api/inventory-adjustments/{id}/reject       { "rejectionReason": "Recount required" }
```

**Approval is the only place a count changes stock.** Requires `Admin` or
`WarehouseManager`, and **the approver must be a different person from the requester** — try
to approve your own and you get a 422.

Rejection changes no stock and records the reason.

### 6.4 Standalone adjustments

For a damage write-off outside a count:

```
POST /api/inventory-adjustments
{ "warehouseId": "…", "locationId": "…", "itemId": "…",
  "countedQuantity": 22, "reason": "Damage", "notes": "3 units crushed by forklift" }
```

`countedQuantity` is the **corrected total**, not the delta. Then approve it as above.

A write-off can never consume stock that is allocated or held — it only touches free stock.

### 6.5 The audit trail

```
GET /api/inventory-adjustments/{id}/audit
```

Returns the whole chronological history:

| Event | Records |
|---|---|
| `CountPlanCreated` | who created the plan, when |
| `CountPlanReleased` | when tasks were generated |
| `CountTaskCreated` | the blind system snapshot |
| `Counted` | who counted, what they found, the variance |
| `AdjustmentRaised` | who raised it, what it proposes |
| `Approved` / `Rejected` | who decided, when, and **on-hand before → after** |

The before/after values are read from the balance **under a row lock at approval**, not
copied from the snapshot taken when the adjustment was raised. If stock moved in between,
`driftedBeforeApproval` flags it rather than hiding it.

---

## 7. Algorithm readiness (Faz 6)

No algorithm runs in v1 — §10 defers them. What exists is the data they will need.

```
GET /api/algorithms/readiness?warehouseId=…
```

Audits all 30 §10 data points across Location, Item, Order and Inventory and reports which
are actually populated. Use it to find gaps in your master data.

```
GET /api/algorithms/demand-stats?warehouseId=…
```

Per-item velocity derived from shipped history — **lines per day**, total shipped, distinct
orders. This is the primary slotting input.

```
GET /api/algorithms/order-history?WarehouseId=…
```

Raw shipped-line history, written automatically on every shipment.

When you do build an engine, it can record its output and be measured:

```
PUT  /api/algorithms/configurations                        # tunable parameters
POST /api/algorithms/slotting-recommendations              # what it suggests
POST /api/algorithms/slotting-recommendations/{id}/decide  # whether it was accepted
POST /api/algorithms/picking-plans                         # routes, estimated vs actual
```

---

## Common errors

All errors are RFC 7807 JSON with a stable `errorCode` and a `traceId`.

| HTTP | `errorCode` | Means |
|---|---|---|
| 400 | `VALIDATION_FAILED` | Malformed request; see `errors` |
| 401 | `AUTHENTICATION_FAILED` | Missing, expired or invalid token |
| 403 | — | Authenticated, but your role is not permitted |
| 404 | `NOT_FOUND` | Unknown id, **or** it belongs to another account |
| 409 | `DUPLICATE_ENTITY` | Unique constraint, e.g. SKU already exists in this account |
| 409 | `INVALID_STATE_TRANSITION` | e.g. shipping an already-shipped order |
| 409 | `CONCURRENCY_CONFLICT` | Someone else changed the same stock. **Retry** |
| 422 | `INSUFFICIENT_STOCK` | Carries a `shortfalls` array: item, requested, available, short |
| 422 | `BUSINESS_RULE_VIOLATION` | A §11 rule; carries `ruleNumber` when it maps to one |

Cross-account access returns **404, not 403**, on purpose — a 403 would confirm the id
exists somewhere else.

---

## Quick reference

| I want to… | Call |
|---|---|
| See all stock | `GET /api/inventory` |
| See stock for one item | `GET /api/inventory/by-item/{id}` |
| Find somewhere to put an item | `GET /api/locations/available?WarehouseId=…&ItemId=…` |
| Book opening stock | `POST /api/inventory/manual-entry` |
| Move stock | `POST /api/inventory/movements` |
| Quarantine stock | `POST /api/inventory/status-change` |
| Receive a delivery | `POST /api/inbound-orders/{id}/receive` |
| Fulfil an order | allocate → create-pick-tasks → complete → ship |
| Find out why stock changed | `GET /api/inventory/transactions?ItemId=…` |
| Correct a discrepancy | `POST /api/inventory-adjustments` then approve |
| Prove who changed what | `GET /api/inventory-adjustments/{id}/audit` |
