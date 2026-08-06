"""End-to-end verification of Faz 5 (Inventory Control): counting and adjustment.

Section 9 of the source document is MISSING, so this workflow is reconstructed.
These checks pin down the reconstruction and, above all, the AUDIT TRAIL:
who created the plan, who counted, who approved or rejected, and the actual
inventory values before and after the change.
"""
import json
import subprocess
import sys
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


def onhand_at(item_id, location_id):
    out, _ = sql(f"""SELECT COALESCE(SUM("OnHandQuantity"),0) FROM inventory_balances
                     WHERE "ItemId"='{item_id}' AND "LocationId"='{location_id}';""")
    return float(out or 0)



def onhand_of(balance_id):
    """On-hand of ONE balance row. A count task targets a single row, not a location."""
    out, _ = sql('SELECT COALESCE(SUM("OnHandQuantity"),0) FROM inventory_balances '
                 f"""WHERE "Id"='{balance_id}';""")
    return float(out or 0)


def make_count_location(code, zone_id, qty):
    """Dedicated location holding exactly one balance row, for deterministic counting."""
    s_, loc = call("POST", "/api/locations", token=T, body={
        "warehouseId": WH, "zoneId": zone_id, "code": code,
        "aisle": None, "bay": None, "level": None, "position": None,
        "locationType": "StandardShelf", "locationProfileId": None,
        "pickSequence": 900, "putawaySequence": 900,
        "coordinateX": 0, "coordinateY": 0, "coordinateZ": 0,
        "maxWeight": None, "maxVolume": None,
        "isPickable": True, "isPutawayAllowed": True,
        "distanceToReceiving": 10, "distanceToPacking": 10, "distanceToShipping": 10,
        "accessibilityScore": 50, "maxConcurrentWorkers": 1})
    if s_ != 201:
        raise RuntimeError(f"fixture location {code} failed: {s_} {loc}")
    s_, bal = call("POST", "/api/inventory/manual-entry", token=OP, body={
        "warehouseId": WH, "locationId": loc["id"], "itemId": ITEM,
        "quantity": qty, "uomId": None, "inventoryStatusId": None,
        "lotNumber": None, "manufactureDate": None, "expirationDate": None,
        "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None,
        "notes": f"count fixture {code}"})
    if s_ != 200:
        raise RuntimeError(f"fixture stock for {code} failed: {s_} {bal}")
    return loc["id"], bal["id"]


st = json.load(open("faz1_state.json"))
_, b = call("POST", "/api/auth/login", {"username": "admin", "password": "DevAdmin!2026"})
T = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "operator1", "password": "OperatorPass!2026"})
OP = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "viewer1", "password": "ViewerPass!2026"})
V = b["accessToken"]

WH, LOCS, ITEM, MILK = st["wh"], st["locs"], st["item"], st["milk"]
SID = st["statusIds"]

# A WarehouseManager to act as the approver: separation of duties means the person
# who raises an adjustment cannot approve it.
status, mgr = call("POST", "/api/auth/users", token=T, body={
    "username": "manager1", "email": "mgr1@ims.local", "fullName": "Warehouse Manager",
    "password": "ManagerPass!2026", "role": "WarehouseManager", "defaultWarehouseId": WH})
check("Fixture: WarehouseManager created to act as approver", status == 201, f"got {status}")
_, b = call("POST", "/api/auth/login", {"username": "manager1", "password": "ManagerPass!2026"})
MGR = b["accessToken"]


# Isolated locations so counting is independent of stock left by earlier phases.
_, zlist = call("GET", f"/api/zones?warehouseId={WH}", token=T)
ZONE_RSV = next(z["id"] for z in zlist if z["code"] == "RSV")

CNT1, BAL1 = make_count_location("CNT-01", ZONE_RSV, 100)
CNT2, BAL2 = make_count_location("CNT-02", ZONE_RSV, 60)
CNT3, BAL3 = make_count_location("CNT-03", ZONE_RSV, 40)
CNT4, BAL4 = make_count_location("CNT-04", ZONE_RSV, 25)
check("Fixture: isolated count locations created with known stock",
      onhand_of(BAL1) == 100 and onhand_of(BAL4) == 25,
      f"{onhand_of(BAL1)}/{onhand_of(BAL4)}")

# =====================================================================
print("\n=== Count plan creation and release ===")
# =====================================================================

status, err = call("POST", "/api/count-plans", token=OP, body={
    "warehouseId": WH, "planNumber": None, "name": "Bad", "countType": "Cycle",
    "selectionMode": "ByZone", "zoneId": None, "itemId": None, "locationIds": None,
    "scheduledDate": None, "blockAllocationDuringCount": False, "notes": None})
check("ByZone plan without a zone refused (422)", status == 422, f"got {status}")

status, plan = call("POST", "/api/count-plans", token=OP, body={
    "warehouseId": WH, "planNumber": None, "name": "Reserve storage cycle count",
    "countType": "Cycle", "selectionMode": "ByLocation", "zoneId": None, "itemId": None,
    "locationIds": [CNT1], "scheduledDate": "2026-08-10T06:00:00Z",
    "blockAllocationDuringCount": False, "notes": "Weekly cycle"})
check("Count plan created (POST /api/count-plans, doc 12)", status == 201, f"got {status}: {plan}")
check("Plan number auto-generated as CP-yyyyMMdd-nnnn",
      status == 201 and plan["planNumber"].startswith("CP-"))
check("Plan starts in Draft", status == 201 and plan["status"] == "Draft")
check("AUDIT: plan records WHO created it",
      status == 201 and plan.get("createdBy") == "operator1",
      f"createdBy={plan.get('createdBy')}")
check("AUDIT: plan records WHEN it was created", status == 201 and plan.get("createdAt"))
PLAN = plan["id"]

status, err = call("POST", f"/api/count-plans/{PLAN}/release", token=OP, body=None)
check("Releasing a ByLocation plan without locations refused (422)", status == 422, f"got {status}")

before_qty = onhand_of(BAL1)
check("Fixture: reserve location holds stock to count", before_qty > 0, f"qty={before_qty}")

status, plan = call("POST", f"/api/count-plans/{PLAN}/release", token=OP,
                    body=[CNT1])
check("Plan released and tasks generated", status == 200 and plan["taskCount"] >= 1,
      f"got {status}: taskCount={plan.get('taskCount') if status == 200 else None}")
check("Plan moved to Released", status == 200 and plan["status"] == "Released")
check("AUDIT: ReleasedAt recorded", status == 200 and plan.get("releasedAt"))

task = next(t for t in plan["tasks"] if t["itemId"] == ITEM)
check("AUDIT: task snapshots the system quantity at generation (blind count baseline)",
      abs(task["systemQuantity"] - before_qty) < 0.001,
      f"snapshot={task['systemQuantity']} actual={before_qty}")
check("Task starts uncounted", task.get("countedQuantity") is None
      and task["status"] == "Created")
check("Task resolves to exactly one inventory balance (doc 5.1 tuple)",
      task.get("inventoryBalanceId") is not None)
TASK = task["id"]

# =====================================================================
print("\n=== Counting with NO variance ===")
# =====================================================================

# Count a different task exactly right, if one exists; otherwise use a matching count.
status, matched = call("POST", f"/api/count-tasks/{TASK}/complete", token=OP,
                       body={"countedQuantity": task["systemQuantity"], "notes": "Matches"})
check("Count matching the system quantity accepted", status == 200, f"got {status}: {matched}")
check("No-variance count closes the task as Completed",
      status == 200 and matched["status"] == "Completed", f"got {matched.get('status')}")
check("No-variance count raises NO adjustment",
      status == 200 and matched.get("inventoryAdjustmentId") is None)
check("AUDIT: task records WHO counted", status == 200 and matched.get("countedBy") == "operator1",
      f"countedBy={matched.get('countedBy')}")
check("AUDIT: task records WHEN it was counted", status == 200 and matched.get("countedAt"))
check("AUDIT: variance recorded as zero", status == 200 and matched.get("variance") == 0)
check("No stock moved on a matching count",
      abs(onhand_of(BAL1) - before_qty) < 0.001)

status, _ = call("POST", f"/api/count-tasks/{TASK}/complete", token=OP,
                 body={"countedQuantity": 5, "notes": None})
check("Re-counting a completed task refused (409)", status == 409, f"got {status}")

# =====================================================================
print("\n=== Counting WITH variance -> adjustment -> approval ===")
# =====================================================================

status, plan2 = call("POST", "/api/count-plans", token=OP, body={
    "warehouseId": WH, "planNumber": None, "name": "Variance count",
    "countType": "Spot", "selectionMode": "ByLocation", "zoneId": None, "itemId": None,
    "locationIds": [CNT2], "scheduledDate": None,
    "blockAllocationDuringCount": False, "notes": None})
PLAN2 = plan2["id"]
status, plan2 = call("POST", f"/api/count-plans/{PLAN2}/release", token=OP,
                     body=[CNT2])
task2 = next(t for t in plan2["tasks"] if t["itemId"] == ITEM)
TASK2 = task2["id"]
system_qty = task2["systemQuantity"]
short_by = 7
counted = system_qty - short_by

status, t2 = call("POST", f"/api/count-tasks/{TASK2}/complete", token=OP,
                  body={"countedQuantity": counted, "notes": "7 units missing from the shelf"})
check("Variance count accepted", status == 200, f"got {status}: {t2}")
check("Variance count sets the task to VarianceFound (held open)",
      status == 200 and t2["status"] == "VarianceFound", f"got {t2.get('status')}")
check("AUDIT: variance computed and stored",
      status == 200 and abs(t2["variance"] + short_by) < 0.001, f"variance={t2.get('variance')}")
check("Variance raised an adjustment", status == 200 and t2.get("inventoryAdjustmentId"))
ADJ = t2["inventoryAdjustmentId"]

check("Stock NOT yet changed - adjustment is only pending",
      abs(onhand_of(BAL2) - system_qty) < 0.001,
      f"expected {system_qty}, got {onhand_of(BAL2)}")

status, adj = call("GET", f"/api/inventory-adjustments/{ADJ}", token=T)
check("Adjustment is Pending approval", status == 200 and adj["status"] == "Pending")
check("Adjustment number auto-generated as ADJ-yyyyMMdd-nnnn",
      status == 200 and adj["adjustmentNumber"].startswith("ADJ-"))
check("AUDIT: adjustment records WHO raised it",
      status == 200 and adj.get("requestedBy") == "operator1", f"got {adj.get('requestedBy')}")
check("AUDIT: adjustment reason is CountVariance",
      status == 200 and adj["reason"] == "CountVariance")
check("AUDIT: adjustment stores system vs counted quantities",
      status == 200 and abs(adj["systemQuantity"] - system_qty) < 0.001
      and abs(adj["countedQuantity"] - counted) < 0.001, f"got {adj}")
check("AUDIT: before/after values are empty until approval",
      status == 200 and adj.get("quantityBeforeApproval") is None
      and adj.get("quantityAfterApproval") is None)

# Separation of duties
status, err = call("POST", f"/api/inventory-adjustments/{ADJ}/approve", token=OP, body=None)
check("Operator CANNOT approve an adjustment (403)", status == 403, f"got {status}")

status, err = call("POST", f"/api/inventory-adjustments/{ADJ}/approve", token=V, body=None)
check("Viewer CANNOT approve an adjustment (403)", status == 403, f"got {status}")

# Approve as the manager
status, approved = call("POST", f"/api/inventory-adjustments/{ADJ}/approve", token=MGR,
                        body={"notes": "Verified with the floor supervisor"})
check("Manager CAN approve (POST /api/inventory-adjustments/{id}/approve, doc 12)",
      status == 200, f"got {status}: {approved}")
check("Adjustment status becomes Approved", status == 200 and approved["status"] == "Approved")
check("AUDIT: records WHO approved",
      status == 200 and approved.get("approvedBy") == "manager1",
      f"approvedBy={approved.get('approvedBy')}")
check("AUDIT: records WHEN it was approved", status == 200 and approved.get("approvedAt"))
check("AUDIT: requester and approver are DIFFERENT people",
      status == 200 and approved.get("requestedBy") != approved.get("approvedBy"),
      f"{approved.get('requestedBy')} vs {approved.get('approvedBy')}")

check("AUDIT: BEFORE value captured from the real balance",
      status == 200 and abs(approved["quantityBeforeApproval"] - system_qty) < 0.001,
      f"before={approved.get('quantityBeforeApproval')}, expected {system_qty}")
check("AUDIT: AFTER value captured from the real balance",
      status == 200 and abs(approved["quantityAfterApproval"] - counted) < 0.001,
      f"after={approved.get('quantityAfterApproval')}, expected {counted}")
check("AUDIT: applied delta matches before -> after",
      status == 200 and abs(approved["adjustmentQuantity"] + short_by) < 0.001,
      f"delta={approved.get('adjustmentQuantity')}")
check("AUDIT: no drift flagged (stock did not move between raise and approve)",
      status == 200 and approved.get("driftedBeforeApproval") in (False, None))

check("Stock ACTUALLY changed on approval",
      abs(onhand_of(BAL2) - counted) < 0.001,
      f"expected {counted}, got {onhand_of(BAL2)}")

status, t2b = call("GET", f"/api/count-tasks/{TASK2}", token=T)
check("Originating count task closed once its adjustment was decided",
      status == 200 and t2b["status"] == "Completed", f"got {t2b.get('status')}")

# Rule 11.9: the ledger entry
status, txn = call("GET", f"/api/inventory/transactions?TransactionType=CountAdjustment", token=T)
check("Rule 11.9: approval wrote a CountAdjustment ledger row",
      status == 200 and txn["totalCount"] >= 1, f"got {txn.get('totalCount')}")
tx = txn["items"][0] if status == 200 and txn["items"] else {}
check("Ledger row references the adjustment document",
      tx.get("referenceType") == "InventoryAdjustment" and tx.get("referenceId") == ADJ,
      f"got {tx}")
check("Negative adjustment leaves the warehouse (source set, no destination)",
      tx.get("fromLocationCode") == "CNT-02" and tx.get("toLocationCode") is None,
      f"got from={tx.get('fromLocationCode')} to={tx.get('toLocationCode')}")
check("AUDIT: adjustment links to its ledger row",
      approved.get("inventoryTransactionId") == tx.get("id"),
      f"{approved.get('inventoryTransactionId')} vs {tx.get('id')}")

status, _ = call("POST", f"/api/inventory-adjustments/{ADJ}/approve", token=MGR, body=None)
check("Approving an already-approved adjustment refused (409)", status == 409, f"got {status}")

# =====================================================================
print("\n=== Full audit trail endpoint ===")
# =====================================================================

status, audit = call("GET", f"/api/inventory-adjustments/{ADJ}/audit", token=T)
check("Audit trail endpoint returns the full history", status == 200, f"got {status}: {audit}")
events = {e["event"]: e for e in audit["timeline"]} if status == 200 else {}
check("Trail includes CountPlanCreated with its actor",
      "CountPlanCreated" in events and events["CountPlanCreated"]["actor"] == "operator1",
      f"got {events.get('CountPlanCreated')}")
check("Trail includes CountPlanReleased", "CountPlanReleased" in events)
check("Trail includes CountTaskCreated with the blind snapshot",
      "CountTaskCreated" in events
      and str(int(system_qty)) in (events["CountTaskCreated"]["detail"] or ""),
      f"got {events.get('CountTaskCreated')}")
check("Trail includes Counted with WHO counted and what they found",
      "Counted" in events and events["Counted"]["actor"] == "operator1",
      f"got {events.get('Counted')}")
check("Trail includes AdjustmentRaised with WHO raised it",
      "AdjustmentRaised" in events and events["AdjustmentRaised"]["actor"] == "operator1",
      f"got {events.get('AdjustmentRaised')}")
check("Trail includes Approved with WHO approved it",
      "Approved" in events and events["Approved"]["actor"] == "manager1",
      f"got {events.get('Approved')}")
check("Approved event states the before -> after values",
      "Approved" in events and "on-hand" in (events["Approved"]["detail"] or ""),
      f"got {events.get('Approved', {}).get('detail')}")
check("Trail is ordered chronologically",
      status == 200 and [e["at"] for e in audit["timeline"] if e["at"]]
      == sorted([e["at"] for e in audit["timeline"] if e["at"]]))
check("Audit summary carries before/after and the ledger link",
      status == 200 and audit["quantityBeforeApproval"] is not None
      and audit["quantityAfterApproval"] is not None
      and audit["inventoryTransactionId"] is not None, f"got {audit}")

# =====================================================================
print("\n=== Rejection path (no stock change) ===")
# =====================================================================

status, plan3 = call("POST", "/api/count-plans", token=OP, body={
    "warehouseId": WH, "planNumber": None, "name": "Rejection test", "countType": "Spot",
    "selectionMode": "ByLocation", "zoneId": None, "itemId": None,
    "locationIds": [CNT3], "scheduledDate": None,
    "blockAllocationDuringCount": False, "notes": None})
PLAN3 = plan3["id"]
status, plan3 = call("POST", f"/api/count-plans/{PLAN3}/release", token=OP,
                     body=[CNT3])
task3 = next(t for t in plan3["tasks"] if t["itemId"] == ITEM)
qty_before_reject = onhand_of(BAL3)

status, t3 = call("POST", f"/api/count-tasks/{task3['id']}/complete", token=OP,
                  body={"countedQuantity": task3["systemQuantity"] + 99, "notes": "Suspicious"})
ADJ3 = t3["inventoryAdjustmentId"]

status, err = call("POST", f"/api/inventory-adjustments/{ADJ3}/reject", token=MGR,
                   body={"rejectionReason": ""})
check("Rejection without a reason refused (422)", status == 422, f"got {status}")

status, rejected = call("POST", f"/api/inventory-adjustments/{ADJ3}/reject", token=MGR,
                        body={"rejectionReason": "Recount required; counter miskeyed"})
check("Adjustment rejected", status == 200 and rejected["status"] == "Rejected",
      f"got {status}: {rejected}")
check("AUDIT: rejection records WHO decided and WHEN",
      status == 200 and rejected.get("approvedBy") == "manager1" and rejected.get("approvedAt"))
check("AUDIT: rejection reason recorded",
      status == 200 and "Recount required" in (rejected.get("rejectionReason") or ""))
check("Rejection changed NO stock",
      abs(onhand_of(BAL3) - qty_before_reject) < 0.001,
      f"before={qty_before_reject} after={onhand_of(BAL3)}")
check("AUDIT: rejected adjustment has no before/after values",
      status == 200 and rejected.get("quantityAfterApproval") is None)

status, audit3 = call("GET", f"/api/inventory-adjustments/{ADJ3}/audit", token=T)
ev3 = {e["event"] for e in audit3["timeline"]} if status == 200 else set()
check("Rejected adjustment's trail shows Rejected, not Approved",
      "Rejected" in ev3 and "Approved" not in ev3, f"got {ev3}")

# =====================================================================
print("\n=== Standalone adjustment (damage write-off) ===")
# =====================================================================

dmg_before = onhand_of(BAL4)
check("Fixture: adjustment location holds stock", dmg_before > 0, f"qty={dmg_before}")

status, sa = call("POST", "/api/inventory-adjustments", token=OP, body={
    "warehouseId": WH, "locationId": CNT4, "itemId": ITEM,
    "inventoryStatusId": SID["AVAILABLE"], "lotId": None, "serialId": None,
    "licensePlateId": None, "countedQuantity": dmg_before - 3,
    "reason": "Damage", "notes": "3 units crushed by forklift"})
check("Standalone adjustment raised outside a count", status == 201, f"got {status}: {sa}")
check("AUDIT: standalone adjustment records the reason",
      status == 201 and sa["reason"] == "Damage")
check("AUDIT: standalone adjustment has no originating count task",
      status == 201 and sa.get("countTaskId") is None)
SA = sa["id"]

status, err = call("POST", "/api/inventory-adjustments", token=OP, body={
    "warehouseId": WH, "locationId": CNT4, "itemId": ITEM,
    "inventoryStatusId": SID["AVAILABLE"], "lotId": None, "serialId": None,
    "licensePlateId": None, "countedQuantity": dmg_before,
    "reason": "Damage", "notes": None})
check("A no-op adjustment (counted == system) refused (422)", status == 422, f"got {status}")

status, sap = call("POST", f"/api/inventory-adjustments/{SA}/approve", token=MGR, body=None)
check("Standalone adjustment approved", status == 200 and sap["status"] == "Approved",
      f"got {status}")
check("Damage write-off reduced stock by exactly 3",
      abs(onhand_of(BAL4) - (dmg_before - 3)) < 0.001,
      f"before={dmg_before} after={onhand_of(BAL4)}")
check("AUDIT: before/after recorded on the standalone adjustment",
      abs(sap["quantityBeforeApproval"] - dmg_before) < 0.001
      and abs(sap["quantityAfterApproval"] - (dmg_before - 3)) < 0.001, f"got {sap}")

# Positive adjustment (found stock)
found_before = onhand_of(BAL4)
status, fa = call("POST", "/api/inventory-adjustments", token=OP, body={
    "warehouseId": WH, "locationId": CNT4, "itemId": ITEM,
    "inventoryStatusId": SID["AVAILABLE"], "lotId": None, "serialId": None,
    "licensePlateId": None, "countedQuantity": found_before + 4,
    "reason": "Found", "notes": "Found behind the rack"})
status, fap = call("POST", f"/api/inventory-adjustments/{fa['id']}/approve", token=MGR, body=None)
check("Positive adjustment (found stock) increases on-hand",
      status == 200 and abs(onhand_of(BAL4) - (found_before + 4)) < 0.001,
      f"got {onhand_of(BAL4)}")
status, ftx = call("GET", "/api/inventory/transactions?TransactionType=CountAdjustment&PageSize=50",
                   token=T)
pos_tx = next((t for t in ftx["items"] if t.get("toLocationCode") == "CNT-04"
               and t.get("fromLocationCode") is None), None)
check("Positive adjustment enters the system (destination set, no source)",
      pos_tx is not None, f"got {[(t.get('fromLocationCode'), t.get('toLocationCode')) for t in ftx['items']][:5]}")

# =====================================================================
print("\n=== Separation of duties and protection of reserved stock ===")
# =====================================================================

# The manager raises one themselves, then tries to approve it.
status, own = call("POST", "/api/inventory-adjustments", token=MGR, body={
    "warehouseId": WH, "locationId": CNT4, "itemId": ITEM,
    "inventoryStatusId": SID["AVAILABLE"], "lotId": None, "serialId": None,
    "licensePlateId": None, "countedQuantity": onhand_of(BAL4) - 1,
    "reason": "Loss", "notes": "Self-approval attempt"})
check("Manager can raise an adjustment", status == 201, f"got {status}")
status, err = call("POST", f"/api/inventory-adjustments/{own['id']}/approve", token=MGR, body=None)
check("Separation of duties: cannot approve your OWN adjustment (422)",
      status == 422, f"got {status}: {err}")
status, ok = call("POST", f"/api/inventory-adjustments/{own['id']}/approve", token=T, body=None)
check("A different approver (admin) CAN approve it", status == 200, f"got {status}")

# A write-off must not consume stock reserved for an order.
_, res_stock = call("POST", "/api/inventory/manual-entry", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["SHIP-01"], "itemId": ITEM,
    "quantity": 10, "uomId": None, "inventoryStatusId": None,
    "lotNumber": None, "manufactureDate": None, "expirationDate": None,
    "supplierLotNumber": None, "serialNumber": None, "licensePlateId": None, "notes": None})
call("POST", "/api/inventory/hold", token=OP,
     body={"inventoryBalanceId": res_stock["id"], "quantity": 8, "notes": "reserved for QC"})

status, ra = call("POST", "/api/inventory-adjustments", token=OP, body={
    "warehouseId": WH, "locationId": LOCS["SHIP-01"], "itemId": ITEM,
    "inventoryStatusId": SID["AVAILABLE"], "lotId": None, "serialId": None,
    "licensePlateId": None, "countedQuantity": 0, "reason": "Loss", "notes": "write off all"})
status, err = call("POST", f"/api/inventory-adjustments/{ra['id']}/approve", token=MGR, body=None)
check("A write-off cannot consume held quantity (422)", status == 422, f"got {status}: {err}")
check("Held stock survived the rejected write-off",
      abs(onhand_at(ITEM, LOCS["SHIP-01"]) - 10) < 0.001,
      f"got {onhand_at(ITEM, LOCS['SHIP-01'])}")

# =====================================================================
print("\n=== Transaction reporting (Faz 5) ===")
# =====================================================================

status, rep = call("GET", f"/api/inventory/transactions?ItemId={ITEM}&PageSize=200", token=T)
kinds = {t["transactionType"] for t in rep["items"]} if status == 200 else set()
check("Faz 5 transaction report covers the whole item lifecycle",
      {"Receipt", "Movement", "StatusChange", "Allocation", "Pick", "Ship",
       "CountAdjustment"}.issubset(kinds), f"got {kinds}")
check("Every ledger row names the actor who performed it",
      status == 200 and all(t.get("performedBy") for t in rep["items"]),
      "some rows have no performedBy")

status, alist = call("GET", "/api/inventory-adjustments?Status=Approved", token=T)
check("Adjustments filterable by status", status == 200 and alist["totalCount"] >= 3,
      f"got {alist.get('totalCount')}")

status, _ = call("POST", "/api/count-plans", token=V, body={
    "warehouseId": WH, "planNumber": None, "name": "x", "countType": "Cycle",
    "selectionMode": "ByWarehouse", "zoneId": None, "itemId": None, "locationIds": None,
    "scheduledDate": None, "blockAllocationDuringCount": False, "notes": None})
check("Viewer CANNOT create count plans (403)", status == 403, f"got {status}")

# =====================================================================
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 5 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

sys.exit(1 if failed else 0)
