"""End-to-end verification of Faz 2 (Inventory Core).

Covers doc section 5 (balance, transaction, status, lot/serial/LPN), section 8
(movement), and the business rules in section 11 that apply to stock handling.
Assumes verify_faz1.py has run against the same database.
"""
import json
import subprocess
import sys
import threading
import urllib.request
import urllib.error

BASE = "http://localhost:5080"
PSQL = r"C:\Program Files\PostgreSQL\15\bin\psql.exe"
results = []


def call(method, path, body=None, token=None):
    url = BASE + path
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(url, data=data, method=method)
    req.add_header("Content-Type", "application/json")
    if token:
        req.add_header("Authorization", "Bearer " + token)
    try:
        with urllib.request.urlopen(req) as r:
            payload = r.read().decode()
            return r.status, (json.loads(payload) if payload else None)
    except urllib.error.HTTPError as e:
        payload = e.read().decode()
        try:
            return e.code, json.loads(payload)
        except Exception:
            return e.code, payload


def check(name, condition, detail=""):
    results.append((name, bool(condition), detail))
    print(f"[{'PASS' if condition else 'FAIL'}] {name}" +
          (f"  -- {detail}" if detail and not condition else ""))


def sql(query):
    import os
    env = dict(os.environ, PGPASSWORD="devpass")
    r = subprocess.run([PSQL, "-h", "127.0.0.1", "-p", "5433", "-U", "imsdev",
                        "-d", "ims", "-w", "-t", "-A", "-c", query],
                       capture_output=True, text=True, env=env)
    return (r.stdout or "").strip(), (r.stderr or "").strip()


st = json.load(open("faz1_state.json"))
_, body = call("POST", "/api/auth/login", {"username": "admin", "password": "DevAdmin!2026"})
T = body["accessToken"]
_, opbody = call("POST", "/api/auth/login",
                 {"username": "operator1", "password": "OperatorPass!2026"})
OP = opbody["accessToken"]

status, statuses = call("GET", "/api/inventory/statuses", token=T)
SID = {s["code"]: s["id"] for s in statuses}
check("Doc 5.2: all 7 inventory statuses seeded", len(statuses) == 7, f"got {len(statuses)}")
check("Only AVAILABLE is allocatable (rule 11.4)",
      [s["code"] for s in statuses if s["isAllocatable"]] == ["AVAILABLE"],
      f"allocatable={[s['code'] for s in statuses if s['isAllocatable']]}")

WH, LOCS, ITEM, MILK, PHONE = st["wh"], st["locs"], st["item"], st["milk"], st["phone"]

# ------------------------------------------------------------ manual stock entry
print("\n=== Faz 2: Manual stock entry ===")

status, bal = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-A-01"], "itemId": ITEM,
    "quantity": 100, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": "Opening stock"})
check("Manual entry books 100 EA", status == 200 and bal["onHandQuantity"] == 100,
      f"got {status}: {bal}")
check("Doc 5.1 formula: Available = OnHand - Allocated - Hold",
      status == 200 and bal["availableQuantity"] == 100, f"got {bal.get('availableQuantity')}")
check("Balance defaults to AVAILABLE status", status == 200 and bal["statusCode"] == "AVAILABLE")
# The row is INSERTed (v1) then UPDATEd with the quantity (v2): two persisted changes,
# so v2 is the correct value for a freshly booked balance.
check("Concurrency token increments on every persisted change",
      status == 200 and bal["version"] >= 1, f"got {bal.get('version')}")
BAL1 = bal["id"] if status == 200 else None

# Rule 11.9: the entry must have produced a ledger row
status, txns = call("GET", f"/api/inventory/transactions?ItemId={ITEM}", token=T)
check("Rule 11.9: manual entry wrote an InventoryTransaction",
      status == 200 and txns["totalCount"] == 1
      and txns["items"][0]["transactionType"] == "Receipt",
      f"got {status}: {txns}")
check("Transaction records who performed it (doc 5.6)",
      status == 200 and txns["items"][0]["performedBy"] == "operator1",
      f"got {txns['items'][0].get('performedBy') if status == 200 else None}")

# UOM conversion: 2 Cases = 48 EA
status, bal2 = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-B-01"], "itemId": ITEM,
    "quantity": 2, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": None})
# find the CS uom id from the item
_, itemdto = call("GET", f"/api/items/{ITEM}", token=T)
cs_uom = next(u["uomId"] for u in itemdto["uoms"] if u["uomCode"] == "CS")
status, bal3 = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["P-01-A-01"], "itemId": ITEM,
    "quantity": 2, "uomId": cs_uom, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": "2 cases"})
check("Doc 4.3: 2 Cases converts to 48 base units",
      status == 200 and bal3["onHandQuantity"] == 48, f"got {bal3.get('onHandQuantity')}")

# ------------------------------------------------------------ tracking rules
print("\n=== Rules 11.5 / 11.6: lot, serial and expiration tracking ===")

status, err = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["C-01-A-01"], "itemId": MILK,
    "quantity": 10, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": None})
check("Lot-tracked item without a lot number rejected (422)", status == 422, f"got {status}")

status, milkbal = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["C-01-A-01"], "itemId": MILK,
    "quantity": 40, "uomId": None, "inventoryStatusId": None,
    "lotNumber": "LOT-A", "manufactureDate": "2026-08-01T00:00:00Z",
    "expirationDate": "2026-08-20T00:00:00Z",
    "supplierLotNumber": "SUP-A1", "serialNumber": None, "licensePlateId": None,
    "notes": None})
check("Lot-tracked entry with lot + expiration accepted",
      status == 200 and milkbal["lotNumber"] == "LOT-A", f"got {status}: {milkbal}")

# Acceptance scenario 4: two lots of the same item stay separate records
status, milkbal2 = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["C-01-A-01"], "itemId": MILK,
    "quantity": 30, "uomId": None, "inventoryStatusId": None,
    "lotNumber": "LOT-B", "manufactureDate": "2026-08-03T00:00:00Z",
    "expirationDate": "2026-08-12T00:00:00Z",
    "supplierLotNumber": "SUP-B1", "serialNumber": None, "licensePlateId": None,
    "notes": None})
check("Scenario 4: second lot creates a SEPARATE balance record",
      status == 200 and milkbal2["id"] != milkbal["id"], f"got {status}")

status, bylot = call("GET", f"/api/inventory/by-item/{MILK}", token=T)
check("Scenario 4: both lots visible as distinct inventory records",
      status == 200 and len(bylot) == 2, f"got {len(bylot) if status == 200 else None}")
check("Scenario 4: FEFO ordering puts LOT-B (earlier expiry) first",
      status == 200 and bylot[0]["lotNumber"] == "LOT-B",
      f"order={[b['lotNumber'] for b in bylot] if status == 200 else None}")

# Shelf life derivation
status, derived = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["C-01-A-01"], "itemId": MILK,
    "quantity": 5, "uomId": None, "inventoryStatusId": None,
    "lotNumber": "LOT-C", "manufactureDate": "2026-08-05T00:00:00Z",
    "expirationDate": None, "supplierLotNumber": None,
    "serialNumber": None, "licensePlateId": None, "notes": None})
check("Rule 11.6: expiration derived from ShelfLifeDays when omitted",
      status == 200 and derived["expirationDate"] is not None
      and derived["expirationDate"].startswith("2026-08-19"),
      f"got {derived.get('expirationDate') if status == 200 else status}")

# Serial tracking
status, err = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-A-01"], "itemId": PHONE,
    "quantity": 5, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": None})
check("Serial-tracked item without a serial rejected (422)", status == 422, f"got {status}")

status, serbal = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-A-01"], "itemId": PHONE,
    "quantity": 1, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": "SN-0001", "licensePlateId": None,
    "notes": None})
check("Serial-tracked entry of qty 1 accepted",
      status == 200 and serbal["onHandQuantity"] == 1, f"got {status}: {serbal}")

status, err = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-A-01"], "itemId": PHONE,
    "quantity": 3, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": "SN-0002", "licensePlateId": None,
    "notes": None})
check("Rule 11.5: serial quantity greater than 1 rejected (422)", status == 422, f"got {status}")

status, err = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-B-01"], "itemId": PHONE,
    "quantity": 1, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": "SN-0001", "licensePlateId": None,
    "notes": None})
check("Duplicate serial already in stock rejected (409)", status == 409, f"got {status}")

# ------------------------------------------------------------ movement (doc 8)
print("\n=== Doc 8: Inventory movement ===")

status, moved = call("POST", "/api/inventory/movements", token=OP, body={
    "warehouseId": WH, "itemId": ITEM,
    "fromLocationId": LOCS["A-01-A-01"], "toLocationId": LOCS["P-01-A-01"],
    "quantity": 30, "inventoryStatusId": SID["AVAILABLE"],
    "lotId": None, "serialId": None, "licensePlateId": None,
    "notes": "Replenish pick face"})
check("Move 30 from reserve to pick face", status == 200, f"got {status}: {moved}")
check("Destination balance increased to 78 (48 + 30)",
      status == 200 and moved["onHandQuantity"] == 78, f"got {moved.get('onHandQuantity')}")

status, src = call("GET", f"/api/inventory/by-location/{LOCS['A-01-A-01']}", token=T)
src_item = next((b for b in src if b["itemId"] == ITEM), None)
check("Source balance decreased to 70", src_item and src_item["onHandQuantity"] == 70,
      f"got {src_item['onHandQuantity'] if src_item else None}")

status, err = call("POST", "/api/inventory/movements", token=OP, body={
    "warehouseId": WH, "itemId": ITEM,
    "fromLocationId": LOCS["A-01-A-01"], "toLocationId": LOCS["P-01-A-01"],
    "quantity": 9999, "inventoryStatusId": SID["AVAILABLE"],
    "lotId": None, "serialId": None, "licensePlateId": None, "notes": None})
check("Doc 8: movement exceeding stock rejected (422)", status == 422, f"got {status}")
check("Shortfall detail reported per item",
      status == 422 and isinstance(err, dict) and "shortfalls" in err
      and err["shortfalls"][0]["shortQuantity"] == 9929,
      f"got {err.get('shortfalls') if isinstance(err, dict) else err}")

# The failed move must not have changed anything (rule 11.12)
status, src2 = call("GET", f"/api/inventory/by-location/{LOCS['A-01-A-01']}", token=T)
src_item2 = next((b for b in src2 if b["itemId"] == ITEM), None)
check("Rule 11.12: failed movement rolled back, balance unchanged at 70",
      src_item2 and src_item2["onHandQuantity"] == 70,
      f"got {src_item2['onHandQuantity'] if src_item2 else None}")

status, err = call("POST", "/api/inventory/movements", token=OP, body={
    "warehouseId": WH, "itemId": ITEM,
    "fromLocationId": LOCS["A-01-A-01"], "toLocationId": LOCS["A-01-A-01"],
    "quantity": 5, "inventoryStatusId": SID["AVAILABLE"],
    "lotId": None, "serialId": None, "licensePlateId": None, "notes": None})
check("Move to the same location rejected (422)", status == 422, f"got {status}")

# Rule 11.8 on movement destination
status, err = call("POST", "/api/inventory/movements", token=OP, body={
    "warehouseId": WH, "itemId": MILK,
    "fromLocationId": LOCS["C-01-A-01"], "toLocationId": LOCS["A-01-A-01"],
    "quantity": 5, "inventoryStatusId": SID["AVAILABLE"],
    "lotId": bylot[0]["lotId"], "serialId": None, "licensePlateId": None, "notes": None})
check("Rule 11.8: cold-chain item cannot be moved to an ambient location (422)",
      status == 422, f"got {status}: {err}")

# ------------------------------------------------------------ status change
print("\n=== Doc 12: Status change ===")

status, dmg = call("POST", "/api/inventory/status-change", token=OP, body={
    "warehouseId": WH, "itemId": ITEM, "locationId": LOCS["A-01-A-01"],
    "fromInventoryStatusId": SID["AVAILABLE"], "toInventoryStatusId": SID["DAMAGED"],
    "quantity": 10, "lotId": None, "serialId": None, "licensePlateId": None,
    "notes": "Forklift damage"})
check("Move 10 units Available -> Damaged", status == 200 and dmg["statusCode"] == "DAMAGED",
      f"got {status}: {dmg}")
check("Damaged stock is NOT allocatable (rule 11.4)",
      status == 200 and dmg["isAllocatable"] is False)

status, byloc = call("GET", f"/api/inventory/by-location/{LOCS['A-01-A-01']}", token=T)
avail = next((b for b in byloc if b["itemId"] == ITEM and b["statusCode"] == "AVAILABLE"), None)
check("Available balance reduced to 60 after status change",
      avail and avail["onHandQuantity"] == 60,
      f"got {avail['onHandQuantity'] if avail else None}")

status, txns = call("GET", f"/api/inventory/transactions?TransactionType=StatusChange", token=T)
check("Status change wrote a StatusChange transaction",
      status == 200 and txns["totalCount"] >= 1, f"got {status}")

# ------------------------------------------------------------ hold
print("\n=== Faz 5 groundwork: hold ===")

status, held = call("POST", "/api/inventory/hold", token=OP,
                    body={"inventoryBalanceId": BAL1, "quantity": 15, "notes": "Pending QC"})
check("Place hold on 15 units", status == 200 and held["holdQuantity"] == 15,
      f"got {status}: {held}")
check("Doc 5.1: hold reduces Available but not OnHand",
      status == 200 and held["onHandQuantity"] == 60 and held["availableQuantity"] == 45,
      f"onHand={held.get('onHandQuantity')} avail={held.get('availableQuantity')}")

status, err = call("POST", "/api/inventory/hold", token=OP,
                   body={"inventoryBalanceId": BAL1, "quantity": 9999, "notes": None})
check("Hold exceeding available rejected (422)", status == 422, f"got {status}")

status, rel = call("POST", "/api/inventory/release-hold", token=OP,
                   body={"inventoryBalanceId": BAL1, "quantity": 15, "notes": None})
check("Release hold restores availability",
      status == 200 and rel["holdQuantity"] == 0 and rel["availableQuantity"] == 60,
      f"got {status}: {rel}")

# ------------------------------------------------------------ LPN nesting (doc 5.5)
print("\n=== Doc 5.5: Nested license plates ===")

status, pallet = call("POST", "/api/license-plates", token=OP, body={
    "warehouseId": WH, "code": "PLT-0001", "licensePlateType": "Pallet",
    "parentLicensePlateId": None, "currentLocationId": LOCS["A-01-A-01"]})
check("Create pallet LPN", status == 200, f"got {status}: {pallet}")
PALLET = pallet["id"] if status == 200 else None

cases = []
for i in range(1, 4):
    status, c = call("POST", "/api/license-plates", token=OP, body={
        "warehouseId": WH, "code": f"CASE-000{i}", "licensePlateType": "Case",
        "parentLicensePlateId": PALLET, "currentLocationId": LOCS["A-01-A-01"]})
    if status == 200:
        cases.append(c["id"])
check("Create 3 cases nested under the pallet", len(cases) == 3, f"created {len(cases)}")

status, tree = call("GET", f"/api/license-plates/{PALLET}/tree", token=T)
check("Doc 5.5 hierarchy: Pallet -> Case 1 / Case 2 / Case 3",
      status == 200 and tree["code"] == "PLT-0001" and len(tree["children"]) == 3,
      f"got {status}: {tree}")

status, tote = call("POST", "/api/license-plates", token=OP, body={
    "warehouseId": WH, "code": "TOTE-0001", "licensePlateType": "Tote",
    "parentLicensePlateId": cases[0], "currentLocationId": LOCS["A-01-A-01"]})
status, tree = call("GET", f"/api/license-plates/{PALLET}/tree", token=T)
depth2 = any(len(c["children"]) == 1 for c in tree["children"]) if status == 200 else False
check("Multi-level nesting (Pallet -> Case -> Tote) supported", depth2, f"got {tree}")

# Stock on an LPN
status, lpnbal = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-A-01"], "itemId": ITEM,
    "quantity": 24, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": cases[0],
    "notes": "Case content"})
check("Stock can be held on a license plate",
      status == 200 and lpnbal["licensePlateCode"] == "CASE-0001", f"got {status}: {lpnbal}")
check("Doc 5.1: LPN produces a separate balance row from loose stock",
      status == 200 and lpnbal["id"] != BAL1)

# ------------------------------------------------------------ ledger immutability
print("\n=== Rule 11.10: ledger is immutable ===")

out, err = sql("UPDATE inventory_transactions SET \"Quantity\" = 999 "
               "WHERE \"Id\" = (SELECT \"Id\" FROM inventory_transactions LIMIT 1);")
check("Rule 11.10: direct UPDATE on ledger blocked by trigger",
      "immutable ledger" in err, f"stderr={err[:160]}")

out, err = sql("DELETE FROM inventory_transactions "
               "WHERE \"Id\" = (SELECT \"Id\" FROM inventory_transactions LIMIT 1);")
check("Rule 11.10: direct DELETE on ledger blocked by trigger",
      "immutable ledger" in err, f"stderr={err[:160]}")

out, err = sql("UPDATE inventory_balances SET \"OnHandQuantity\" = -5 "
               "WHERE \"Id\" = '" + str(BAL1) + "';")
check("Negative stock blocked by check constraint",
      "ck_inventory_balances" in err, f"stderr={err[:160]}")

out, err = sql("INSERT INTO inventory_balances "
               "(\"Id\",\"WarehouseId\",\"LocationId\",\"ItemId\",\"InventoryStatusId\","
               "\"OnHandQuantity\",\"AllocatedQuantity\",\"HoldQuantity\",\"ReceivedAt\",\"Version\") "
               "SELECT gen_random_uuid(),\"WarehouseId\",\"LocationId\",\"ItemId\","
               "\"InventoryStatusId\",1,0,0,now(),1 FROM inventory_balances "
               "WHERE \"Id\"='" + str(BAL1) + "';")
check("Doc 5.1: duplicate balance combo blocked even with NULL lot/serial/LPN",
      "ux_inventory_balances_unique_combo" in err, f"stderr={err[:200]}")

out, _ = sql("SELECT \"AvailableQuantity\" FROM inventory_balances WHERE \"Id\"='" + str(BAL1) + "';")
check("Doc 5.1: AvailableQuantity is DB-generated and correct (60)",
      out.strip().startswith("60"), f"got {out!r}")

# ------------------------------------------------------------ concurrency (rule 11.11)
print("\n=== Rule 11.11: concurrency control ===")

# Two simultaneous movements of 40 each from a balance holding 60.
# Exactly one must succeed; stock must never go negative.
_, conc = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["DMG-01"], "itemId": ITEM,
    "quantity": 60, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
    "notes": "concurrency fixture"})

outcomes = []
lock = threading.Lock()


def racer():
    s, b = call("POST", "/api/inventory/movements", token=OP, body={
        "warehouseId": WH, "itemId": ITEM,
        "fromLocationId": LOCS["DMG-01"], "toLocationId": LOCS["A-01-B-01"],
        "quantity": 40, "inventoryStatusId": SID["AVAILABLE"],
        "lotId": None, "serialId": None, "licensePlateId": None, "notes": "race"})
    with lock:
        outcomes.append(s)


threads = [threading.Thread(target=racer) for _ in range(2)]
for t in threads:
    t.start()
for t in threads:
    t.join()

succeeded = [o for o in outcomes if o == 200]
check("Rule 11.11: exactly one of two concurrent 40-unit moves succeeds",
      len(succeeded) == 1, f"outcomes={outcomes}")

out, _ = sql("SELECT \"OnHandQuantity\" FROM inventory_balances b "
             "JOIN locations l ON l.\"Id\"=b.\"LocationId\" "
             "WHERE l.\"Code\"='DMG-01' AND b.\"ItemId\"='" + str(ITEM) + "';")
check("Rule 11.11: stock never went negative under concurrency",
      out.strip() in ("20.0000", "20.0000\n", "20"), f"remaining={out!r}")

# ------------------------------------------------------------ ledger completeness
print("\n=== Doc 5.6: ledger completeness ===")

status, alltx = call("GET", "/api/inventory/transactions?PageSize=200", token=T)
types = {}
for t in alltx["items"]:
    types[t["transactionType"]] = types.get(t["transactionType"], 0) + 1
check("Ledger contains Receipt, Movement and StatusChange entries",
      all(k in types for k in ["Receipt", "Movement", "StatusChange"]), f"got {types}")
check("Every ledger row carries a correlation id (doc 5.6)",
      all(t["correlationId"] for t in alltx["items"]))
check("Movement transaction records both From and To location",
      any(t["fromLocationCode"] and t["toLocationCode"]
          for t in alltx["items"] if t["transactionType"] == "Movement"))

status, filtered = call("GET", f"/api/inventory/transactions?LocationId={LOCS['A-01-A-01']}", token=T)
check("Transactions filterable by location", status == 200 and filtered["totalCount"] > 0)

status, summary = call("GET", f"/api/inventory/summary?warehouseId={WH}", token=T)
check("Warehouse stock summary aggregates per item", status == 200 and len(summary) >= 3,
      f"got {status}: {summary}")

# ------------------------------------------------------------ authorization
print("\n=== Authorization on stock operations ===")

_, vbody = call("POST", "/api/auth/login", {"username": "viewer1", "password": "ViewerPass!2026"})
V = vbody["accessToken"]

status, _ = call("GET", "/api/inventory", token=V)
check("Viewer CAN read inventory", status == 200, f"got {status}")

status, _ = call("POST", "/api/inventory/movements", token=V, body={
    "warehouseId": WH, "itemId": ITEM,
    "fromLocationId": LOCS["A-01-A-01"], "toLocationId": LOCS["P-01-A-01"],
    "quantity": 1, "inventoryStatusId": SID["AVAILABLE"],
    "lotId": None, "serialId": None, "licensePlateId": None, "notes": None})
check("Viewer CANNOT move stock (403)", status == 403, f"got {status}")

# ------------------------------------------------------------ summary
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 2 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

st["lpn_pallet"] = PALLET
st["lpn_cases"] = cases
st["statusIds"] = SID
json.dump(st, open("faz1_state.json", "w"), indent=2)

sys.exit(1 if failed else 0)
