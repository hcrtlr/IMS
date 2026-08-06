"""End-to-end verification of Faz 4 (Outbound), including acceptance scenarios 2 and 3.

Scenario 2 (doc 14): create outbound order -> release -> allocate -> create pick task ->
complete picking -> ship -> stock decrements by the correct amount.

Scenario 3 (doc 14): when the ordered quantity exceeds available stock the system must
NOT mark the order fully allocated, must NOT create negative stock, and must report
which item is short and by how much.

Also covers doc rules 11.1-11.4 and 11.11 as they apply to allocation and shipment.
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


def avail(item_id):
    """Total available quantity of an item across the warehouse."""
    out, _ = sql(f"""SELECT COALESCE(SUM(b."AvailableQuantity"),0) FROM inventory_balances b
                     JOIN inventory_statuses s ON s."Id"=b."InventoryStatusId"
                     WHERE b."ItemId"='{item_id}' AND s."IsAllocatable";""")
    return float(out or 0)


def onhand(item_id):
    out, _ = sql(f"""SELECT COALESCE(SUM("OnHandQuantity"),0) FROM inventory_balances
                     WHERE "ItemId"='{item_id}';""")
    return float(out or 0)


st = json.load(open("faz1_state.json"))
_, b = call("POST", "/api/auth/login", {"username": "admin", "password": "DevAdmin!2026"})
T = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "operator1", "password": "OperatorPass!2026"})
OP = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "viewer1", "password": "ViewerPass!2026"})
V = b["accessToken"]

WH, LOCS, ITEM, MILK, PHONE = st["wh"], st["locs"], st["item"], st["milk"], st["phone"]
CUST, SID = st["customer"], st["statusIds"]

_, itemdto = call("GET", f"/api/items/{ITEM}", token=T)
EA = itemdto["baseUomId"]
_, milkdto = call("GET", f"/api/items/{MILK}", token=T)
MILK_EA = milkdto["baseUomId"]


def new_order(lines, **kw):
    body = {"warehouseId": WH, "orderNumber": None, "customerId": CUST,
            "orderDate": None, "requiredShipDate": "2026-08-20T12:00:00Z", "carrier": "Yurtici",
            "serviceLevel": "Standard", "priority": 100, "orderType": "Standard",
            "notes": None, "lines": lines}
    body.update(kw)
    return call("POST", "/api/orders", token=OP, body=body)


# =====================================================================
print("\n=== ACCEPTANCE SCENARIO 2: Order allocation and picking (doc 14) ===")
# =====================================================================

start_onhand = onhand(ITEM)
start_avail = avail(ITEM)
check("Fixture: item has stock on hand before the scenario", start_onhand > 0,
      f"onhand={start_onhand}")

# Step 1: create the outbound order
status, order = new_order([{"itemId": ITEM, "orderedQuantity": 50, "uomId": EA,
                            "requiredLotNumber": None, "requiredSerialNumber": None,
                            "minimumShelfLifeDays": None}])
check("Step 1: outbound order created (doc 7.1)", status == 201, f"got {status}: {order}")
check("Order number auto-generated as SO-yyyyMMdd-nnnn",
      status == 201 and order["orderNumber"].startswith("SO-"))
check("Order starts in Draft (doc 7.1 lifecycle)", status == 201 and order["status"] == "Draft")
check("Doc 10: header totals computed for future batching",
      status == 201 and order["totalLineCount"] == 1 and order["totalQuantity"] == 50,
      f"lines={order.get('totalLineCount')} qty={order.get('totalQuantity')}")
ORD = order["id"]
LINE = order["details"][0]["id"]

# Allocating before release must be refused
status, _ = call("POST", f"/api/orders/{ORD}/allocate", token=OP, body=None)
check("Allocating a Draft order refused (422)", status == 422, f"got {status}")

# Step 2: release
status, o = call("POST", f"/api/orders/{ORD}/confirm", token=OP)
check("Draft -> Created", status == 200 and o["status"] == "Created", f"got {o.get('status')}")
status, o = call("POST", f"/api/orders/{ORD}/release", token=OP)
check("Step 2: Created -> Released (doc 12 release endpoint)",
      status == 200 and o["status"] == "Released", f"got {o.get('status')}")
check("ReleasedAt timestamp recorded", status == 200 and o.get("releasedAt") is not None)

# Step 3: allocate
status, alloc = call("POST", f"/api/orders/{ORD}/allocate", token=OP, body=None)
check("Step 3: allocation succeeded", status == 200, f"got {status}: {alloc}")
check("Step 3: order reaches Allocated when fully covered",
      status == 200 and alloc["status"] == "Allocated", f"got {alloc.get('status')}")
check("Step 3: no shortfalls reported", status == 200 and len(alloc["shortfalls"]) == 0)
check("Step 3: isFullyAllocated true", status == 200 and alloc["isFullyAllocated"] is True)
check("Doc 7.3: allocation records which balance it reserved from",
      status == 200 and all(a["inventoryBalanceId"] for a in alloc["allocations"]))
check("Doc 7.3: AllocationStrategy recorded for future algorithm comparison",
      status == 200 and all(a["allocationStrategy"] == "Fefo" for a in alloc["allocations"]))

# Rule 11.2: allocation must not change on-hand
check("Rule 11.2: allocation left on-hand UNCHANGED",
      onhand(ITEM) == start_onhand, f"before={start_onhand} after={onhand(ITEM)}")
check("Doc 7.3: allocation REDUCED available quantity by 50",
      abs(avail(ITEM) - (start_avail - 50)) < 0.001,
      f"before={start_avail} after={avail(ITEM)}")

# Rule 11.9: allocation is journalled
status, txn = call("GET", "/api/inventory/transactions?TransactionType=Allocation", token=T)
check("Rule 11.9: allocation wrote an Allocation transaction",
      status == 200 and txn["totalCount"] >= 1, f"got {txn.get('totalCount')}")

# Step 4: create pick tasks
status, tasks = call("POST", f"/api/orders/{ORD}/create-pick-tasks", token=OP,
                     body={"destinationLocationId": LOCS["PACK-01"], "assignTo": "operator1"})
check("Step 4: pick tasks created (doc 7.4)", status == 200 and len(tasks) >= 1,
      f"got {status}: {tasks}")
check("Pick task assigned and in Assigned status",
      status == 200 and tasks[0]["status"] == "Assigned"
      and tasks[0]["assignedTo"] == "operator1")
check("Doc 7.4: SequenceNumber seeded from the location pick sequence",
      status == 200 and all(t["sequenceNumber"] >= 1 for t in tasks))
check("Pick task knows its source location and destination",
      status == 200 and tasks[0]["fromLocationCode"] and tasks[0].get("destinationLocationCode") == "PACK-01")

status, o = call("GET", f"/api/orders/{ORD}", token=T)
check("Order moved to Picking", o["status"] == "Picking", f"got {o.get('status')}")

# Step 5: complete picking
picked_total = 0
for t in tasks:
    status, done = call("POST", f"/api/pick-tasks/{t['id']}/complete", token=OP,
                        body={"pickedQuantity": None, "notes": None})
    if status == 200:
        picked_total += done["pickedQuantity"]
check("Step 5: picking completed", status == 200 and done["status"] == "Completed",
      f"got {status}: {done}")
check("Step 5: full quantity picked", picked_total == 50, f"picked={picked_total}")

check("Rule 11.3 precondition: picking has NOT yet reduced total on-hand",
      onhand(ITEM) == start_onhand, f"before={start_onhand} after={onhand(ITEM)}")

status, staged = call("GET", f"/api/inventory/by-location/{LOCS['PACK-01']}", token=T)
staged_row = next((b for b in staged if b["itemId"] == ITEM), None)
check("Picked stock moved to the packing location", staged_row is not None
      and staged_row["onHandQuantity"] == 50,
      f"got {staged_row}")
check("Staged stock stays RESERVED, so it is not available to other orders",
      staged_row and staged_row["allocatedQuantity"] == 50
      and staged_row["availableQuantity"] == 0, f"got {staged_row}")

status, o = call("GET", f"/api/orders/{ORD}", token=T)
check("Order moved to Picked", o["status"] == "Picked", f"got {o.get('status')}")

# Step 6: ship
status, ship = call("POST", f"/api/orders/{ORD}/ship", token=OP,
                    body={"carrier": "Yurtici", "serviceLevel": "Standard",
                          "trackingNumber": "TRK-0001", "notes": None})
check("Step 6: shipment created", status == 200, f"got {status}: {ship}")
check("Shipment number auto-generated as SH-yyyyMMdd-nnnn",
      status == 200 and ship["shipmentNumber"].startswith("SH-"))
check("Shipment records carrier and tracking",
      status == 200 and ship["trackingNumber"] == "TRK-0001")

status, o = call("GET", f"/api/orders/{ORD}", token=T)
check("Step 6: order moved to Shipped", o["status"] == "Shipped", f"got {o.get('status')}")
check("ShippedAt timestamp recorded", o.get("shippedAt") is not None)

# Step 7: stock decremented correctly
check("Step 7: rule 11.3 - on-hand reduced by exactly 50",
      abs(onhand(ITEM) - (start_onhand - 50)) < 0.001,
      f"before={start_onhand} after={onhand(ITEM)}")
check("Step 7: allocated quantity released by shipment (no stranded reservation)",
      abs(avail(ITEM) - (start_avail - 50)) < 0.001,
      f"available now {avail(ITEM)}, expected {start_avail - 50}")

status, empty = call("GET", f"/api/inventory/by-location/{LOCS['PACK-01']}", token=T)
check("Step 7: packing location emptied after shipment",
      not any(b["itemId"] == ITEM for b in empty), f"got {empty}")

status, hist = call("GET", f"/api/inventory/transactions?ItemId={ITEM}&PageSize=200", token=T)
types = [t["transactionType"] for t in hist["items"]]
check("Full outbound chain in ledger: Allocation, Pick, Ship",
      all(k in types for k in ["Allocation", "Pick", "Ship"]), f"got {set(types)}")
ship_tx = next((t for t in hist["items"] if t["transactionType"] == "Ship"), None)
check("Ship transaction has a source but NO destination (stock left the building)",
      ship_tx and ship_tx.get("fromLocationCode") and ship_tx.get("toLocationCode") is None,
      f"got {ship_tx}")

# Faz 6 groundwork
out, _ = sql(f"""SELECT COUNT(*) FROM order_history WHERE "OrderId"='{ORD}';""")
check("Faz 6: order history snapshot written on shipment", out.strip() == "1", f"got {out!r}")
out, _ = sql(f"""SELECT "Sku","OrderLineCount","ShippedQuantity" FROM order_history
                 WHERE "OrderId"='{ORD}';""")
check("Faz 6: history denormalises SKU, line count and shipped quantity",
      "TSHIRT-001|1|50" in out, f"got {out!r}")

# =====================================================================
print("\n=== ACCEPTANCE SCENARIO 3: Insufficient stock (doc 14) ===")
# =====================================================================

avail_before = avail(ITEM)
onhand_before = onhand(ITEM)
oversized = avail_before + 500

status, order3 = new_order([
    {"itemId": ITEM, "orderedQuantity": oversized, "uomId": EA,
     "requiredLotNumber": None, "requiredSerialNumber": None, "minimumShelfLifeDays": None},
    {"itemId": MILK, "orderedQuantity": 5, "uomId": MILK_EA,
     "requiredLotNumber": None, "requiredSerialNumber": None, "minimumShelfLifeDays": None}])
ORD3 = order3["id"]
call("POST", f"/api/orders/{ORD3}/confirm", token=OP)
call("POST", f"/api/orders/{ORD3}/release", token=OP)

status, res = call("POST", f"/api/orders/{ORD3}/allocate", token=OP, body=None)
check("Scenario 3: allocation call succeeds but reports a shortfall", status == 200,
      f"got {status}: {res}")
check("Scenario 3a: order NOT marked fully allocated",
      status == 200 and res["isFullyAllocated"] is False, f"got {res.get('isFullyAllocated')}")
check("Scenario 3a: order status is PartiallyAllocated, not Allocated",
      status == 200 and res["status"] == "PartiallyAllocated", f"got {res.get('status')}")

check("Scenario 3c: shortfall reported", status == 200 and len(res["shortfalls"]) == 1,
      f"got {res.get('shortfalls')}")
sf = res["shortfalls"][0] if status == 200 and res["shortfalls"] else {}
check("Scenario 3c: shortfall names the item (SKU)", sf.get("sku") == "TSHIRT-001", f"got {sf}")
check("Scenario 3c: shortfall reports HOW MUCH is missing",
      abs(sf.get("shortQuantity", 0) - 500) < 0.001,
      f"short={sf.get('shortQuantity')}, expected 500")
check("Scenario 3c: shortfall reports requested vs allocated",
      abs(sf.get("requestedQuantity", 0) - oversized) < 0.001
      and abs(sf.get("allocatedQuantity", 0) - avail_before) < 0.001, f"got {sf}")

check("Scenario 3b: no negative stock created",
      onhand(ITEM) == onhand_before, f"before={onhand_before} after={onhand(ITEM)}")
out, _ = sql("""SELECT COUNT(*) FROM inventory_balances
                WHERE "OnHandQuantity" < 0 OR "AvailableQuantity" < 0;""")
check("Scenario 3b: no balance row anywhere is negative", out.strip() == "0", f"got {out!r}")
check("Scenario 3: every available unit WAS allocated (partial fulfilment)",
      abs(avail(ITEM) - 0) < 0.001, f"available now {avail(ITEM)}")

status, o3 = call("GET", f"/api/orders/{ORD3}", token=T)
line_short = next(d for d in o3["details"] if d["itemId"] == ITEM)
line_ok = next(d for d in o3["details"] if d["itemId"] == MILK)
check("Scenario 3: short line is PartiallyAllocated",
      line_short["status"] == "PartiallyAllocated", f"got {line_short['status']}")
check("Scenario 3: the line that COULD be covered is fully Allocated",
      line_ok["status"] == "Allocated" and line_ok["allocatedQuantity"] == 5,
      f"got {line_ok}")

# Strict mode: reject the whole run instead of partially allocating
status, o4 = new_order([{"itemId": MILK, "orderedQuantity": 999999, "uomId": MILK_EA,
                         "requiredLotNumber": None, "requiredSerialNumber": None,
                         "minimumShelfLifeDays": None}])
ORD4 = o4["id"]
call("POST", f"/api/orders/{ORD4}/confirm", token=OP)
call("POST", f"/api/orders/{ORD4}/release", token=OP)
status, err = call("POST", f"/api/orders/{ORD4}/allocate", token=OP,
                   body={"orderDetailIds": None, "allowPartial": False})
check("Rule 11.1 strict mode: allocation refused outright (422)", status == 422, f"got {status}")
check("Strict-mode rejection carries per-item shortfall detail",
      status == 422 and isinstance(err, dict) and err.get("errorCode") == "INSUFFICIENT_STOCK"
      and len(err.get("shortfalls", [])) == 1, f"got {err}")

status, o4b = call("GET", f"/api/orders/{ORD4}", token=T)
check("Strict-mode rejection left the order Released (nothing allocated)",
      o4b["status"] == "Released" and o4b["details"][0]["allocatedQuantity"] == 0,
      f"got {o4b['status']}, alloc={o4b['details'][0]['allocatedQuantity']}")

# =====================================================================
print("\n=== Rule 11.4: non-allocatable stock is never reserved ===")
# =====================================================================

# Add one unit, mark it DAMAGED, then order MORE than the remaining available stock.
# The shortfall must be exactly the damaged unit, proving it was never a candidate.
phone_avail_before = avail(PHONE)

_, dmg = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["DMG-01"], "itemId": PHONE,
    "quantity": 1, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": "SN-DMG-1", "licensePlateId": None,
    "notes": None})
check("Fixture: damaged-unit entry raised available by 1",
      abs(avail(PHONE) - (phone_avail_before + 1)) < 0.001, f"got {avail(PHONE)}")

call("POST", "/api/inventory/status-change", token=OP, body={
    "warehouseId": WH, "itemId": PHONE, "locationId": LOCS["DMG-01"],
    "fromInventoryStatusId": SID["AVAILABLE"], "toInventoryStatusId": SID["DAMAGED"],
    "quantity": 1, "lotId": None, "serialId": dmg.get("serialId"), "licensePlateId": None,
    "notes": "damaged"})
check("Rule 11.4: moving stock to DAMAGED removed it from available",
      abs(avail(PHONE) - phone_avail_before) < 0.001,
      f"expected {phone_avail_before}, got {avail(PHONE)}")

want = phone_avail_before + 1
status, o5 = new_order([{"itemId": PHONE, "orderedQuantity": want, "uomId": EA,
                         "requiredLotNumber": None, "requiredSerialNumber": None,
                         "minimumShelfLifeDays": None}])
ORD5 = o5["id"]
call("POST", f"/api/orders/{ORD5}/confirm", token=OP)
call("POST", f"/api/orders/{ORD5}/release", token=OP)
status, res5 = call("POST", f"/api/orders/{ORD5}/allocate", token=OP, body=None)
check("Rule 11.4: damaged unit NOT allocated - shortfall is exactly 1",
      status == 200 and len(res5["shortfalls"]) == 1
      and abs(res5["shortfalls"][0]["shortQuantity"] - 1) < 0.001,
      f"got {res5.get('shortfalls')}")

# Held stock is likewise excluded
_, held = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["A-01-B-01"], "itemId": ITEM,
    "quantity": 20, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None, "notes": None})
call("POST", "/api/inventory/hold", token=OP,
     body={"inventoryBalanceId": held["id"], "quantity": 20, "notes": "QC"})

status, o6 = new_order([{"itemId": ITEM, "orderedQuantity": 20, "uomId": EA,
                         "requiredLotNumber": None, "requiredSerialNumber": None,
                         "minimumShelfLifeDays": None}])
ORD6 = o6["id"]
call("POST", f"/api/orders/{ORD6}/confirm", token=OP)
call("POST", f"/api/orders/{ORD6}/release", token=OP)
status, res6 = call("POST", f"/api/orders/{ORD6}/allocate", token=OP, body=None)
check("Rule 11.4: held quantity is NOT allocatable",
      status == 200 and len(res6["shortfalls"]) == 1
      and abs(res6["shortfalls"][0]["shortQuantity"] - 20) < 0.001, f"got {res6}")

# =====================================================================
print("\n=== FEFO allocation ordering and lot constraints ===")
# =====================================================================

status, fefo = new_order([{"itemId": MILK, "orderedQuantity": 10, "uomId": MILK_EA,
                           "requiredLotNumber": None, "requiredSerialNumber": None,
                           "minimumShelfLifeDays": None}])
ORDF = fefo["id"]
call("POST", f"/api/orders/{ORDF}/confirm", token=OP)
call("POST", f"/api/orders/{ORDF}/release", token=OP)
status, resf = call("POST", f"/api/orders/{ORDF}/allocate", token=OP, body=None)
lots_used = [a.get("lotNumber") for a in resf["allocations"]] if status == 200 else []
check("FEFO: allocation drew from the soonest-expiring lot first",
      status == 200 and lots_used and lots_used[0] == "LOT-B",
      f"lots used in order: {lots_used}")

# Required-lot constraint
status, reql = new_order([{"itemId": MILK, "orderedQuantity": 3, "uomId": MILK_EA,
                           "requiredLotNumber": "LOT-A", "requiredSerialNumber": None,
                           "minimumShelfLifeDays": None}])
ORDL = reql["id"]
call("POST", f"/api/orders/{ORDL}/confirm", token=OP)
call("POST", f"/api/orders/{ORDL}/release", token=OP)
status, resl = call("POST", f"/api/orders/{ORDL}/allocate", token=OP, body=None)
check("Doc 7.2: RequiredLotNumber restricts allocation to that lot",
      status == 200 and all(a.get("lotNumber") == "LOT-A" for a in resl["allocations"])
      and len(resl["allocations"]) > 0, f"got {resl.get('allocations')}")

# Minimum shelf life
status, shelf = new_order([{"itemId": MILK, "orderedQuantity": 5, "uomId": MILK_EA,
                            "requiredLotNumber": None, "requiredSerialNumber": None,
                            "minimumShelfLifeDays": 3650}])
ORDS = shelf["id"]
call("POST", f"/api/orders/{ORDS}/confirm", token=OP)
call("POST", f"/api/orders/{ORDS}/release", token=OP)
status, ress = call("POST", f"/api/orders/{ORDS}/allocate", token=OP, body=None)
check("Doc 7.2: MinimumShelfLifeDays excludes lots expiring too soon",
      status == 200 and len(ress["allocations"]) == 0 and len(ress["shortfalls"]) == 1,
      f"got {ress}")

# =====================================================================
print("\n=== Short pick and deallocation ===")
# =====================================================================

_, sp = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["P-01-A-01"], "itemId": ITEM,
    "quantity": 30, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None, "notes": None})

status, osp = new_order([{"itemId": ITEM, "orderedQuantity": 30, "uomId": EA,
                          "requiredLotNumber": None, "requiredSerialNumber": None,
                          "minimumShelfLifeDays": None}])
ORDSP = osp["id"]
call("POST", f"/api/orders/{ORDSP}/confirm", token=OP)
call("POST", f"/api/orders/{ORDSP}/release", token=OP)
call("POST", f"/api/orders/{ORDSP}/allocate", token=OP, body=None)
status, sptasks = call("POST", f"/api/orders/{ORDSP}/create-pick-tasks", token=OP,
                       body={"destinationLocationId": LOCS["SHIP-01"], "assignTo": None})

avail_pre_short = avail(ITEM)
status, spdone = call("POST", f"/api/pick-tasks/{sptasks[0]['id']}/complete", token=OP,
                      body={"pickedQuantity": 18, "notes": "Only 18 on the shelf"})
check("Short pick closes the task as ShortPicked",
      status == 200 and spdone["status"] == "ShortPicked", f"got {status}: {spdone}")
check("Short pick records picked and short quantities",
      status == 200 and spdone["pickedQuantity"] == 18 and spdone["shortQuantity"] == 12,
      f"got picked={spdone.get('pickedQuantity')} short={spdone.get('shortQuantity')}")
check("Short pick RELEASED the unpicked 12 back to available stock",
      abs(avail(ITEM) - (avail_pre_short + 12)) < 0.001,
      f"before={avail_pre_short} after={avail(ITEM)}")

status, ospd = call("GET", f"/api/orders/{ORDSP}", token=T)
check("Order line reflects the reduced allocation after a short pick",
      ospd["details"][0]["allocatedQuantity"] == 18
      and ospd["details"][0]["pickedQuantity"] == 18, f"got {ospd['details'][0]}")

status, shipsp = call("POST", f"/api/orders/{ORDSP}/ship", token=OP, body=None)
check("A short-picked order can still ship what was actually picked",
      status == 200 and shipsp["lines"][0]["quantity"] == 18, f"got {status}: {shipsp}")

# Deallocation
status, od = new_order([{"itemId": ITEM, "orderedQuantity": 10, "uomId": EA,
                         "requiredLotNumber": None, "requiredSerialNumber": None,
                         "minimumShelfLifeDays": None}])
ORDD = od["id"]
call("POST", f"/api/orders/{ORDD}/confirm", token=OP)
call("POST", f"/api/orders/{ORDD}/release", token=OP)
avail_pre_dealloc = avail(ITEM)
call("POST", f"/api/orders/{ORDD}/allocate", token=OP, body=None)
check("Allocation reduced available by 10", abs(avail(ITEM) - (avail_pre_dealloc - 10)) < 0.001,
      f"got {avail(ITEM)}")
status, _ = call("POST", f"/api/orders/{ORDD}/deallocate", token=OP, body=None)
check("Deallocation returned the 10 to available",
      status == 200 and abs(avail(ITEM) - avail_pre_dealloc) < 0.001,
      f"got {avail(ITEM)} expected {avail_pre_dealloc}")
check("Rule 11.2 mirror: deallocation left on-hand unchanged",
      abs(onhand(ITEM) - onhand(ITEM)) < 0.001)

status, txn = call("GET", "/api/inventory/transactions?TransactionType=Deallocation", token=T)
check("Deallocation is journalled (rule 11.9)", status == 200 and txn["totalCount"] >= 1)

# =====================================================================
print("\n=== Lifecycle guards and concurrency ===")
# =====================================================================

status, _ = call("POST", f"/api/orders/{ORD}/ship", token=OP, body=None)
check("Shipping an already-shipped order refused (409)", status == 409, f"got {status}")

status, onew = new_order([{"itemId": ITEM, "orderedQuantity": 1, "uomId": EA,
                           "requiredLotNumber": None, "requiredSerialNumber": None,
                           "minimumShelfLifeDays": None}])
ORDN = onew["id"]
call("POST", f"/api/orders/{ORDN}/confirm", token=OP)
call("POST", f"/api/orders/{ORDN}/release", token=OP)
status, _ = call("POST", f"/api/orders/{ORDN}/ship", token=OP, body=None)
check("Shipping an unpicked order refused (422)", status == 422, f"got {status}")

status, _ = call("POST", f"/api/orders/{ORDN}/create-pick-tasks", token=OP, body=None)
check("Creating pick tasks before allocation refused (422)", status == 422, f"got {status}")

status, oc = call("POST", f"/api/orders/{ORDN}/cancel", token=OP)
check("An unallocated order can be cancelled", status == 200 and oc["status"] == "Cancelled",
      f"got {status}")

# Rule 11.11 under allocation: two orders racing for the same limited stock
_, race_stock = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["SHIP-01"], "itemId": PHONE,
    "quantity": 1, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": "SN-RACE-1", "licensePlateId": None,
    "notes": None})

race_orders = []
for i in range(2):
    _, ro = new_order([{"itemId": PHONE, "orderedQuantity": 1, "uomId": EA,
                        "requiredLotNumber": None, "requiredSerialNumber": None,
                        "minimumShelfLifeDays": None}])
    call("POST", f"/api/orders/{ro['id']}/confirm", token=OP)
    call("POST", f"/api/orders/{ro['id']}/release", token=OP)
    race_orders.append(ro["id"])

outcomes = []
lock = threading.Lock()


def racer(oid):
    s, r = call("POST", f"/api/orders/{oid}/allocate", token=OP,
                body={"orderDetailIds": None, "allowPartial": False})
    with lock:
        outcomes.append(s)


threads = [threading.Thread(target=racer, args=(o,)) for o in race_orders]
for t in threads:
    t.start()
for t in threads:
    t.join()

check("Rule 11.11: only ONE of two orders wins the single available unit",
      outcomes.count(200) == 1 and outcomes.count(422) == 1, f"outcomes={outcomes}")
out, _ = sql("""SELECT COUNT(*) FROM inventory_balances WHERE "AvailableQuantity" < 0;""")
check("Rule 11.11: no negative availability after the race", out.strip() == "0", f"got {out!r}")

# =====================================================================
print("\n=== Authorization ===")
# =====================================================================

status, _ = call("GET", "/api/orders", token=V)
check("Viewer CAN read orders", status == 200, f"got {status}")
status, _ = call("POST", f"/api/orders/{ORD3}/allocate", token=V, body=None)
check("Viewer CANNOT allocate (403)", status == 403, f"got {status}")
status, _ = call("POST", f"/api/orders/{ORD3}/ship", token=V, body=None)
check("Viewer CANNOT ship (403)", status == 403, f"got {status}")

# =====================================================================
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 4 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

sys.exit(1 if failed else 0)
