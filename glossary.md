# Glossary — Inventory Management System (Intern Project)

Decoder ring for terms found in `Stajyer Projesi: Inventory Management System.docx`.

## Acronyms & General Terms
| Term | Meaning |
|------|---------|
| WMS | Warehouse Management System |
| SKU | Stock Keeping Unit (unique per Account) |
| UOM | Unit of Measure (Each / Pack / Case / Pallet) |
| LPN | License Plate (Number) — pallet/case/tote container ID, can nest |
| FEFO | First Expired, First Out |
| FIFO | First In, First Out |
| CRUD | Create, Read, Update, Delete |
| EF Core | Entity Framework Core (ORM) |
| Faz | "Phase" (Turkish) |
| Stajyer Projesi | "Intern Project" (Turkish) |

## Warehouse Hierarchy
Account → Warehouse → Zone → Aisle → Location
- **Account**: the company/customer using the system
- **Warehouse**: physical depot
- **Zone**: logical/physical area (Receiving, QC, Reserve Storage, Picking, Packing, Shipping Staging, Damaged, Returns, Cold Storage, Hazmat)
- **Location**: smallest physical address, e.g. `A-03-B-02` = Aisle A, Bay 03, Level B, Position 02
- **Location Profile**: shared rule-set for similar locations (LocationType: Small Bin, Standard Shelf, Pallet Rack, Floor Storage, Cold Storage, Dangerous Goods, Pick Face, Reserve Storage)

## Item Master Domain
- **Item Master**: core product record (physical, operational, logistics attributes)
- **Attribute Definition / Attribute Value**: dynamic attribute system so item properties aren't hardcoded columns (e.g. Color, Size, Brand, Material, Season, HazardClass)
- **Item UOM**: per-item unit-of-measure conversion + packaging hierarchy
- **Item Barcode**: an item can have multiple barcodes, tied to a UOM

## Inventory Domain
- **Inventory Balance**: current on-hand quantity of an item at a location/status/lot/serial/LPN combo
  - `AvailableQuantity = OnHandQuantity − AllocatedQuantity − HoldQuantity`
- **Inventory Transaction**: immutable ledger of every stock change (Receipt, Putaway, Movement, Allocation, Deallocation, Pick, Ship, CountAdjustment, Damage, Return, StatusChange) — never edited or deleted
- **Inventory Status**: usability state (Available, QualityHold, Damaged, Expired, Quarantine, Returned, Blocked)
- **Lot**: batch-tracked stock (manufacture/expiration dates)
- **Serial Number**: individually tracked stock (quantity should be 1)
- **License Plate / Container**: pallet/case/tote, can be nested

## Inbound Domain
- **Inbound Order** (Draft → Expected → PartiallyReceived → Received → Completed → Cancelled)
- **Inbound Order Detail**: expected line items
- **Receipt**: physical intake — verify, quantity, lot/serial, LPN, moves stock to receiving location, creates Inventory Transaction
- **Putaway Task**: moves received stock from receiving to storage/picking location; target location is manual for now, later algorithm-suggested

## Outbound Domain
- **Order Master** (Draft → Created → Released → PartiallyAllocated → Allocated → Picking → Picked → Packed → Shipped → Cancelled); OrderType: Standard, Express, Wholesale, Retail, Transfer, ReturnReplacement
- **Order Detail**: order line items
- **Inventory Allocation**: reserves stock for an order line without changing OnHandQuantity (only AllocatedQuantity)
- **Pick Task** (Created → Assigned → InProgress → Completed → ShortPicked → Cancelled)

## Key Business Rules (from doc §11)
1. No allocation without sufficient available stock
2. Allocation never changes on-hand quantity
3. Shipment reduces both on-hand and allocated quantity
4. Hold/Damaged/Expired stock can't be allocated to normal orders
5. Serial-tracked stock quantity must not exceed 1
6. Expiration-tracked items require an expiration date
7. Location capacity must not be exceeded
8. Putaway must respect temperature/hazmat compatibility
9. Every stock change must produce an Inventory Transaction
10. Inventory Transactions are never deleted
11. Concurrency control required for simultaneous stock operations
12. Failed operations roll back stock balance + transactions together

## Development Phases (Faz 1–6)
1. Master Data — Account, Warehouse, Zone, Location, Location Profile, Item Master, Item Attribute, UOM/barcode
2. Inventory Core — Balance, Transaction, Status, Lot/Serial/LPN, manual entry, movement
3. Inbound — Inbound Order, Receipt, receiving location, Putaway Task
4. Outbound — Order Master/Detail, Allocation, Pick Task, Shipment
5. Inventory Control — Adjustments, hold/damaged handling, transaction reporting
6. Algorithm Readiness — coordinates, algorithm config tables, historical data, slotting/picking plan records
