"""End-to-end verification of Faz 3 (Inbound) including acceptance scenario 1.

Doc section 6: inbound order, receipt, receiving location, putaway task + completion.
Scenario 1 (doc 14): create item -> inbound order -> receive to receiving location ->
capture lot and expiration -> create putaway task -> move to storage -> verify the
whole chain appears in transaction history.
"""
import json
import sys
import urllib.request
import urllib.error

BASE = "http://localhost:5080"
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


st = json.load(open("faz1_state.json"))
_, b = call("POST", "/api/auth/login", {"username": "admin", "password": "DevAdmin!2026"})
T = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "operator1", "password": "OperatorPass!2026"})
OP = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "viewer1", "password": "ViewerPass!2026"})
V = b["accessToken"]

WH, LOCS, MILK, ITEM = st["wh"], st["locs"], st["milk"], st["item"]
SID = st["statusIds"]
SUP = st["supplier"]

_, itemdto = call("GET", f"/api/items/{ITEM}", token=T)
EA = next(u["uomId"] for u in itemdto["uoms"] if u["uomCode"] == "EA")
CS = next(u["uomId"] for u in itemdto["uoms"] if u["uomCode"] == "CS")
_, milkdto = call("GET", f"/api/items/{MILK}", token=T)
MILK_EA = milkdto["baseUomId"]

# =====================================================================
print("\n=== ACCEPTANCE SCENARIO 1: Receiving and Putaway (doc 14) ===")
# =====================================================================

# Step 1-2: item already exists (Faz 1); create the inbound order
status, order = call("POST", "/api/inbound-orders", token=OP, body={
    "warehouseId": WH, "orderNumber": None, "supplierId": SUP,
    "expectedArrivalDate": "2026-08-10T08:00:00Z",
    "notes": "Scenario 1",
    "lines": [
        {"itemId": MILK, "expectedQuantity": 120, "uomId": MILK_EA,
         "expectedLotNumber": "LOT-S1", "expectedExpirationDate": "2026-08-25T00:00:00Z"},
        {"itemId": ITEM, "expectedQuantity": 10, "uomId": CS,
         "expectedLotNumber": None, "expectedExpirationDate": None}]})
check("Step 2: inbound order created (doc 6.1)", status == 201, f"got {status}: {order}")
check("Order number auto-generated as IB-yyyyMMdd-nnnn",
      status == 201 and order["orderNumber"].startswith("IB-"),
      f"got {order.get('orderNumber') if status == 201 else None}")
check("Order starts in Draft (doc 6.1 lifecycle)",
      status == 201 and order["status"] == "Draft")
ORD = order["id"]
L_MILK = order["details"][0]["id"]
L_ITEM = order["details"][1]["id"]

# Receiving before confirmation must be refused
status, _ = call("POST", f"/api/inbound-orders/{ORD}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L_MILK, "receivedQuantity": 10,
               "receivedUomId": None, "lotNumber": "X", "manufactureDate": None,
               "expirationDate": None, "supplierLotNumber": None,
               "serialNumber": None, "licensePlateId": None, "inventoryStatusId": None}]})
check("Receiving a Draft order refused (422)", status == 422, f"got {status}")

status, order = call("POST", f"/api/inbound-orders/{ORD}/confirm", token=OP)
check("Draft -> Expected transition", status == 200 and order["status"] == "Expected",
      f"got {status}: {order.get('status') if status == 200 else None}")

# Receiving into a non-receiving zone must be refused
status, _ = call("POST", f"/api/inbound-orders/{ORD}/receive", token=OP, body={
    "receivingLocationId": LOCS["A-01-A-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L_ITEM, "receivedQuantity": 1,
               "receivedUomId": CS, "lotNumber": None, "manufactureDate": None,
               "expirationDate": None, "supplierLotNumber": None,
               "serialNumber": None, "licensePlateId": None, "inventoryStatusId": None}]})
check("Doc 6.3: receiving outside a Receiving zone refused (422)", status == 422, f"got {status}")

# Step 3-4: receive to the receiving location, capturing lot + expiration
status, receipt = call("POST", f"/api/inbound-orders/{ORD}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": "Truck TR-01",
    "lines": [
        {"inboundOrderDetailId": L_MILK, "receivedQuantity": 120, "receivedUomId": None,
         "lotNumber": "LOT-S1", "manufactureDate": "2026-08-08T00:00:00Z",
         "expirationDate": "2026-08-25T00:00:00Z", "supplierLotNumber": "SUP-S1",
         "serialNumber": None, "licensePlateId": None, "inventoryStatusId": None},
        {"inboundOrderDetailId": L_ITEM, "receivedQuantity": 10, "receivedUomId": CS,
         "lotNumber": None, "manufactureDate": None, "expirationDate": None,
         "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
         "inventoryStatusId": None}]})
check("Step 3: receipt created (doc 6.3)", status == 200, f"got {status}: {receipt}")
check("Receipt number auto-generated as RC-yyyyMMdd-nnnn",
      status == 200 and receipt["receiptNumber"].startswith("RC-"))
RCPT = receipt["id"] if status == 200 else None
check("Step 4: lot and expiration captured on the receipt line",
      status == 200 and receipt["lines"][0].get("lotNumber") == "LOT-S1"
      and receipt["lines"][0].get("expirationDate").startswith("2026-08-25"),
      f"got {receipt['lines'][0] if status == 200 else None}")
check("Doc 4.3: 10 Cases normalised to 240 base units",
      status == 200 and any(l["baseQuantity"] == 240 for l in receipt["lines"]),
      f"got {[l['baseQuantity'] for l in receipt['lines']] if status == 200 else None}")

status, o = call("GET", f"/api/inbound-orders/{ORD}", token=T)
check("Doc 6.1: order moved to Received when all lines complete",
      status == 200 and o["status"] == "Received", f"got {o.get('status')}")
check("ReceivedQuantity tracked per line",
      status == 200 and o["details"][0]["receivedQuantity"] == 120
      and o["details"][0]["isFullyReceived"] is True)

# Stock must now be sitting in the receiving location
status, recv_stock = call("GET", f"/api/inventory/by-location/{LOCS['RECV-01']}", token=T)
check("Step 3: stock landed in the receiving location",
      status == 200 and len(recv_stock) == 2, f"got {recv_stock}")
milk_in_recv = next((b for b in recv_stock if b["itemId"] == MILK), None)
check("Received milk quantity is 120 in receiving",
      milk_in_recv and milk_in_recv["onHandQuantity"] == 120,
      f"got {milk_in_recv['onHandQuantity'] if milk_in_recv else None}")

# Over-receipt refused
status, _ = call("POST", f"/api/inbound-orders/{ORD}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L_MILK, "receivedQuantity": 5,
               "receivedUomId": None, "lotNumber": "LOT-S1", "manufactureDate": None,
               "expirationDate": "2026-08-25T00:00:00Z", "supplierLotNumber": None,
               "serialNumber": None, "licensePlateId": None, "inventoryStatusId": None}]})
check("Over-receipt beyond expected quantity refused (422)", status == 422, f"got {status}")

# Step 5: create putaway tasks
status, tasks = call("POST", f"/api/receipts/{RCPT}/create-putaway", token=OP, body=None)
check("Step 5: putaway tasks created (doc 6.4)", status == 200 and len(tasks) == 2,
      f"got {status}: {tasks}")
check("Putaway task starts in Created status",
      status == 200 and all(t["status"] == "Created" for t in tasks))
check("Doc 6.4: SuggestedLocationId null in v1 (manual putaway)",
      status == 200 and all(t.get("suggestedLocationId") is None for t in tasks))
check("Doc 6.4: RecommendationReason null until an algorithm exists",
      status == 200 and all(t.get("recommendationReason") is None for t in tasks))
check("Putaway task sources from the receiving location",
      status == 200 and all(t.get("fromLocationCode") == "RECV-01" for t in tasks))

t_milk = next(t for t in tasks if t["itemId"] == MILK)
t_item = next(t for t in tasks if t["itemId"] == ITEM)
check("Putaway task carries the lot forward",
      t_milk.get("lotNumber") == "LOT-S1", f"got {t_milk.get('lotNumber')}")

# Second create-putaway must not double-book
status, _ = call("POST", f"/api/receipts/{RCPT}/create-putaway", token=OP, body=None)
check("Duplicate create-putaway refused once fully covered (422)", status == 422, f"got {status}")

# Rule 11.8 on putaway destination
status, err = call("POST", f"/api/putaway-tasks/{t_milk['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["A-01-A-01"], "quantity": None, "notes": None})
check("Rule 11.8: cold-chain putaway into ambient rack refused (422)",
      status == 422, f"got {status}: {err}")
check("Rule 11.8 violation reports the rule number",
      status == 422 and isinstance(err, dict) and err.get("ruleNumber") == 8,
      f"got {err.get('ruleNumber') if isinstance(err, dict) else None}")

# Putaway into a location that forbids putaway
status, _ = call("POST", f"/api/putaway-tasks/{t_item['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["PACK-01"], "quantity": None, "notes": None})
check("Putaway into a location with IsPutawayAllowed=false refused (422)",
      status == 422, f"got {status}")

# Step 6: move to correct storage locations
status, done_milk = call("POST", f"/api/putaway-tasks/{t_milk['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["C-01-A-01"], "quantity": None, "notes": "To cold storage"})
check("Step 6: cold-chain putaway to the cold location succeeds",
      status == 200 and done_milk["status"] == "Completed",
      f"got {status}: {done_milk}")
check("ActualLocationId recorded on completion",
      status == 200 and done_milk.get("actualLocationCode") == "C-01-A-01")
check("CompletedAt timestamp recorded", status == 200 and done_milk.get("completedAt") is not None)

status, done_item = call("POST", f"/api/putaway-tasks/{t_item['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["A-01-A-01"], "quantity": None, "notes": "To reserve"})
check("Step 6: ambient putaway to reserve storage succeeds", status == 200, f"got {status}")

# Completing twice must fail
status, _ = call("POST", f"/api/putaway-tasks/{t_milk['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["C-01-A-01"], "quantity": None, "notes": None})
check("Completing an already-complete task refused (409)", status == 409, f"got {status}")

# Receiving location must now be empty of the received stock
status, recv_after = call("GET", f"/api/inventory/by-location/{LOCS['RECV-01']}", token=T)
check("Step 6: receiving location emptied after putaway",
      status == 200 and len(recv_after) == 0, f"got {recv_after}")

status, cold = call("GET", f"/api/inventory/by-location/{LOCS['C-01-A-01']}", token=T)
milk_cold = next((b for b in cold if b.get("lotNumber") == "LOT-S1"), None)
check("Step 6: 120 units now in cold storage under LOT-S1",
      milk_cold and milk_cold["onHandQuantity"] == 120,
      f"got {milk_cold['onHandQuantity'] if milk_cold else None}")

# Step 7: the whole chain must be visible in transaction history
status, hist = call(
    "GET", f"/api/inventory/transactions?ItemId={MILK}&PageSize=100", token=T)
types = [t["transactionType"] for t in hist["items"]] if status == 200 else []
check("Step 7: Receipt transaction present in history",
      "Receipt" in types, f"got {types}")
check("Step 7: Putaway transaction present in history",
      "Putaway" in types, f"got {types}")

putaway_tx = next((t for t in hist["items"] if t["transactionType"] == "Putaway"), None)
check("Step 7: putaway transaction records RECV-01 -> C-01-A-01",
      putaway_tx and putaway_tx.get("fromLocationCode") == "RECV-01"
      and putaway_tx.get("toLocationCode") == "C-01-A-01",
      f"got {putaway_tx}")
check("Step 7: putaway transaction references the putaway task",
      putaway_tx and putaway_tx["referenceType"] == "PutawayTask"
      and putaway_tx.get("referenceId") == t_milk["id"])

receipt_tx = next((t for t in hist["items"] if t["transactionType"] == "Receipt"
                   and t["referenceType"] == "Receipt"), None)
check("Step 7: receipt transaction references the receipt document",
      receipt_tx and receipt_tx.get("referenceId") == RCPT, f"got {receipt_tx}")
check("Step 7: receipt transaction carries the lot",
      receipt_tx and receipt_tx.get("lotNumber") == "LOT-S1")

status, corr = call(
    "GET", f"/api/inventory/transactions?CorrelationId={receipt['correlationId']}", token=T)
check("Doc 5.6: both receipt lines share one correlation id",
      status == 200 and corr["totalCount"] == 2, f"got {corr.get('totalCount')}")

# =====================================================================
print("\n=== Partial receipt and lifecycle ===")
# =====================================================================

status, ord2 = call("POST", "/api/inbound-orders", token=OP, body={
    "warehouseId": WH, "orderNumber": None, "supplierId": SUP,
    "expectedArrivalDate": None, "notes": "Partial test",
    "lines": [{"itemId": ITEM, "expectedQuantity": 100, "uomId": EA,
               "expectedLotNumber": None, "expectedExpirationDate": None}]})
ORD2 = ord2["id"]
L2 = ord2["details"][0]["id"]
call("POST", f"/api/inbound-orders/{ORD2}/confirm", token=OP)

status, r2 = call("POST", f"/api/inbound-orders/{ORD2}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L2, "receivedQuantity": 40, "receivedUomId": None,
               "lotNumber": None, "manufactureDate": None, "expirationDate": None,
               "supplierLotNumber": None, "serialNumber": None,
               "licensePlateId": None, "inventoryStatusId": None}]})
check("Partial receipt accepted", status == 200, f"got {status}")

status, o2 = call("GET", f"/api/inbound-orders/{ORD2}", token=T)
check("Doc 6.1: order becomes PartiallyReceived",
      status == 200 and o2["status"] == "PartiallyReceived", f"got {o2.get('status')}")
check("Outstanding quantity reported as 60",
      status == 200 and o2["details"][0]["outstandingQuantity"] == 60,
      f"got {o2['details'][0].get('outstandingQuantity')}")

status, _ = call("POST", f"/api/inbound-orders/{ORD2}/cancel", token=OP)
check("Cancelling an order with received stock refused (422)", status == 422, f"got {status}")

status, r3 = call("POST", f"/api/inbound-orders/{ORD2}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L2, "receivedQuantity": 60, "receivedUomId": None,
               "lotNumber": None, "manufactureDate": None, "expirationDate": None,
               "supplierLotNumber": None, "serialNumber": None,
               "licensePlateId": None, "inventoryStatusId": None}]})
status, o2 = call("GET", f"/api/inbound-orders/{ORD2}", token=T)
check("Second receipt completes the order (Received)",
      status == 200 and o2["status"] == "Received", f"got {o2.get('status')}")
check("Both receipts listed on the order",
      status == 200 and len(o2["receipts"]) == 2, f"got {len(o2.get('receipts', []))}")

status, o2 = call("POST", f"/api/inbound-orders/{ORD2}/complete", token=OP)
check("Received -> Completed", status == 200 and o2["status"] == "Completed",
      f"got {status}: {o2.get('status') if status == 200 else None}")

status, _ = call("POST", f"/api/inbound-orders/{ORD2}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L2, "receivedQuantity": 1, "receivedUomId": None,
               "lotNumber": None, "manufactureDate": None, "expirationDate": None,
               "supplierLotNumber": None, "serialNumber": None,
               "licensePlateId": None, "inventoryStatusId": None}]})
check("Receiving against a Completed order refused (409)", status == 409, f"got {status}")

# =====================================================================
print("\n=== Receiving into QualityHold, partial putaway, LPN ===")
# =====================================================================

status, ord3 = call("POST", "/api/inbound-orders", token=OP, body={
    "warehouseId": WH, "orderNumber": None, "supplierId": SUP,
    "expectedArrivalDate": None, "notes": "QC hold test",
    "lines": [{"itemId": ITEM, "expectedQuantity": 50, "uomId": EA,
               "expectedLotNumber": None, "expectedExpirationDate": None}]})
ORD3 = ord3["id"]
L3 = ord3["details"][0]["id"]
call("POST", f"/api/inbound-orders/{ORD3}/confirm", token=OP)

status, r4 = call("POST", f"/api/inbound-orders/{ORD3}/receive", token=OP, body={
    "receivingLocationId": LOCS["RECV-01"], "notes": None,
    "lines": [{"inboundOrderDetailId": L3, "receivedQuantity": 50, "receivedUomId": None,
               "lotNumber": None, "manufactureDate": None, "expirationDate": None,
               "supplierLotNumber": None, "serialNumber": None,
               "licensePlateId": None, "inventoryStatusId": SID["QUALITY_HOLD"]}]})
check("Doc 5.2: stock can be received into QualityHold", status == 200, f"got {status}")

status, recv = call("GET", f"/api/inventory/by-location/{LOCS['RECV-01']}", token=T)
qc = next((b for b in recv if b["statusCode"] == "QUALITY_HOLD"), None)
check("QualityHold stock is NOT allocatable (rule 11.4)",
      qc and qc["isAllocatable"] is False, f"got {qc}")

# Partial putaway
status, tasks3 = call("POST", f"/api/receipts/{r4['id']}/create-putaway", token=OP, body={
    "lines": [{"receiptLineId": r4["lines"][0]["id"], "quantity": 20,
               "suggestedLocationId": None}]})
check("Partial putaway task for 20 of 50 created",
      status == 200 and tasks3[0]["quantity"] == 20, f"got {status}: {tasks3}")

status, rem = call("GET", f"/api/receipts/{r4['id']}", token=T)
check("Remaining 30 still pending putaway",
      status == 200 and rem["lines"][0]["pendingPutawayQuantity"] == 30,
      f"got {rem['lines'][0].get('pendingPutawayQuantity') if status == 200 else None}")

status, _ = call("POST", f"/api/receipts/{r4['id']}/create-putaway", token=OP, body={
    "lines": [{"receiptLineId": r4["lines"][0]["id"], "quantity": 999,
               "suggestedLocationId": None}]})
check("Putaway beyond pending quantity refused (422)", status == 422, f"got {status}")

status, done3 = call("POST", f"/api/putaway-tasks/{tasks3[0]['id']}/complete", token=OP, body={
    "actualLocationId": LOCS["A-01-B-01"], "quantity": None, "notes": None})
check("QualityHold stock keeps its status through putaway", status == 200, f"got {status}")

status, dest = call("GET", f"/api/inventory/by-location/{LOCS['A-01-B-01']}", token=T)
moved_qc = next((b for b in dest if b["statusCode"] == "QUALITY_HOLD"), None)
check("Putaway preserved QualityHold status at destination",
      moved_qc and moved_qc["onHandQuantity"] == 20, f"got {moved_qc}")

# =====================================================================
print("\n=== Authorization ===")
# =====================================================================

status, _ = call("GET", "/api/inbound-orders", token=V)
check("Viewer CAN read inbound orders", status == 200, f"got {status}")

status, _ = call("POST", "/api/inbound-orders", token=V, body={
    "warehouseId": WH, "orderNumber": None, "supplierId": None,
    "expectedArrivalDate": None, "notes": None,
    "lines": [{"itemId": ITEM, "expectedQuantity": 1, "uomId": EA,
               "expectedLotNumber": None, "expectedExpirationDate": None}]})
check("Viewer CANNOT create inbound orders (403)", status == 403, f"got {status}")

status, tlist = call("GET", f"/api/putaway-tasks?WarehouseId={WH}", token=T)
check("Putaway task list endpoint works",
      status == 200 and tlist["totalCount"] >= 3, f"got {status}: {tlist}")

# =====================================================================
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 3 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

st["inbound_order"] = ORD
json.dump(st, open("faz1_state.json", "w"), indent=2)

sys.exit(1 if failed else 0)
