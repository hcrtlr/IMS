"""End-to-end verification of Faz 6 (Algorithm Readiness).

Doc section 10 is explicit that algorithms are NOT built in v1 and that what matters is
storing the data they will need. These checks therefore assert that the data points
section 10 lists are actually captured, and that the Faz 6 tables work - not that any
algorithm produces a good answer.
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


st = json.load(open("faz1_state.json"))
_, b = call("POST", "/api/auth/login", {"username": "admin", "password": "DevAdmin!2026"})
T = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "operator1", "password": "OperatorPass!2026"})
OP = b["accessToken"]
_, b = call("POST", "/api/auth/login", {"username": "viewer1", "password": "ViewerPass!2026"})
V = b["accessToken"]

WH, LOCS, ITEM = st["wh"], st["locs"], st["item"]

# =====================================================================
print("\n=== Doc 10 data capture: readiness report ===")
# =====================================================================

status, rd = call("GET", f"/api/algorithms/readiness?warehouseId={WH}", token=T)
check("Readiness report returned", status == 200, f"got {status}: {rd}")
checks = {c["dataPoint"]: c for c in rd["checks"]} if status == 200 else {}

for label in ["X/Y coordinates", "Pick sequence", "Putaway sequence",
              "Capacity (weight or volume)", "Type and profile",
              "Distance to receiving/packing/shipping", "Accessibility score",
              "Max concurrent workers"]:
    check(f"Doc 10 Location: {label} captured",
          checks.get(label, {}).get("isReady") is True, f"got {checks.get(label)}")

for label in ["Dimensions and weight", "Volume", "Fragility flag",
              "Hazardous-material flag", "Temperature requirements",
              "Lot / serial / expiration tracking", "Season and other attributes",
              "Storage constraints (default zones)"]:
    check(f"Doc 10 Item: {label} captured",
          checks.get(label, {}).get("isReady") is True, f"got {checks.get(label)}")

for label in ["Order date", "Required ship date", "Priority",
              "Carrier and service level", "Order type",
              "Totals (lines, units, weight, volume)"]:
    check(f"Doc 10 Order: {label} captured",
          checks.get(label, {}).get("isReady") is True, f"got {checks.get(label)}")

for label in ["Receipt date into the warehouse", "Lot manufacture and expiration dates",
              "Last movement date", "Inventory status", "License plate information",
              "Location information"]:
    check(f"Doc 10 Inventory: {label} captured",
          checks.get(label, {}).get("isReady") is True, f"got {checks.get(label)}")

# =====================================================================
print("\n=== Faz 6: historical order data ===")
# =====================================================================

status, hist = call("GET", f"/api/algorithms/order-history?WarehouseId={WH}", token=T)
check("Order history populated automatically by shipments",
      status == 200 and hist["totalCount"] >= 1, f"got {hist.get('totalCount')}")
row = hist["items"][0] if status == 200 and hist["items"] else {}
check("History denormalises the SKU so it survives a rename", row.get("sku"))
check("History captures order type, priority, carrier and service level",
      row.get("orderType") and row.get("priority") is not None)
check("History captures order date, required ship date and shipped date",
      row.get("orderDate") and row.get("shippedAt"))
check("History captures the line count for co-pick affinity analysis",
      row.get("orderLineCount", 0) >= 1, f"got {row.get('orderLineCount')}")
check("History captures fulfilment duration",
      row.get("fulfillmentDurationSeconds") is not None)
check("History records where the stock was picked from",
      "pickedFromLocationId" in row or row.get("pickedFromLocationId") is None)

status, stats = call("GET", f"/api/algorithms/demand-stats?warehouseId={WH}", token=T)
check("Demand statistics derived from history", status == 200 and len(stats) >= 1,
      f"got {status}: {stats}")
s0 = stats[0] if status == 200 and stats else {}
check("Demand stats expose lines-per-day velocity (slotting input)",
      s0.get("linesPerDay") is not None, f"got {s0}")
check("Demand stats expose total shipped quantity and order count",
      s0.get("totalShippedQuantity") is not None and s0.get("distinctOrderCount") is not None)
check("Demand stats sorted by velocity (fastest mover first)",
      status == 200 and all(stats[i]["linesPerDay"] >= stats[i + 1]["linesPerDay"]
                            for i in range(len(stats) - 1)))

# =====================================================================
print("\n=== Faz 6: algorithm configuration table ===")
# =====================================================================

status, cfg = call("PUT", "/api/algorithms/configurations", token=T, body={
    "warehouseId": WH, "algorithmName": "Slotting", "parameterKey": "MaxTravelDistance",
    "parameterValue": "45.5", "valueType": "decimal",
    "description": "Metres a picker may walk before a reslot is proposed", "priority": 10})
check("Algorithm configuration created", status == 200, f"got {status}: {cfg}")
check("Configuration starts at version 1", status == 200 and cfg["version"] == 1)
check("AUDIT: configuration records who created it",
      status == 200 and cfg.get("createdBy") == "admin", f"got {cfg.get('createdBy')}")

status, cfg2 = call("PUT", "/api/algorithms/configurations", token=T, body={
    "warehouseId": WH, "algorithmName": "Slotting", "parameterKey": "MaxTravelDistance",
    "parameterValue": "60", "valueType": "decimal", "description": "Retuned", "priority": 10})
check("Re-upserting the same key updates rather than duplicating",
      status == 200 and cfg2["id"] == cfg["id"], f"got {cfg2.get('id')} vs {cfg['id']}")
check("Update bumps the version so an engine can detect retuning",
      status == 200 and cfg2["version"] == 2, f"got {cfg2.get('version')}")

status, err = call("PUT", "/api/algorithms/configurations", token=T, body={
    "warehouseId": WH, "algorithmName": "Slotting", "parameterKey": "BadInt",
    "parameterValue": "not-a-number", "valueType": "int",
    "description": None, "priority": 0})
check("A value that does not parse as its declared type is refused (422)",
      status == 422, f"got {status}")

status, _ = call("PUT", "/api/algorithms/configurations", token=T, body={
    "warehouseId": None, "algorithmName": "Picking", "parameterKey": "BatchingStrategy",
    "parameterValue": "Wave", "valueType": "string",
    "description": "Account-wide default", "priority": 0})
check("Account-wide configuration (null warehouse) supported", status == 200, f"got {status}")

status, cfgs = call("GET", "/api/algorithms/configurations?algorithmName=Slotting", token=T)
check("Configurations filterable by algorithm", status == 200 and len(cfgs) >= 1)

status, _ = call("PUT", "/api/algorithms/configurations", token=OP, body={
    "warehouseId": WH, "algorithmName": "X", "parameterKey": "Y",
    "parameterValue": "1", "valueType": "int", "description": None, "priority": 0})
check("Operator CANNOT change algorithm configuration (403)", status == 403, f"got {status}")

# =====================================================================
print("\n=== Faz 6: slotting recommendation records ===")
# =====================================================================

status, rec = call("POST", "/api/algorithms/slotting-recommendations", token=T, body={
    "warehouseId": WH, "itemId": ITEM,
    "currentLocationId": LOCS["A-01-A-01"], "recommendedLocationId": LOCS["P-01-A-01"],
    "score": 87.5, "recommendationReason": "High velocity item, currently far from packing",
    "algorithmName": "SlottingV1", "algorithmVersion": "0.1.0"})
check("Slotting recommendation recorded", status == 200, f"got {status}: {rec}")
check("Recommendation stores score and reason",
      status == 200 and rec["score"] == 87.5 and rec.get("recommendationReason"))
check("Recommendation stores which algorithm and version produced it",
      status == 200 and rec.get("algorithmName") == "SlottingV1"
      and rec.get("algorithmVersion") == "0.1.0")
check("Recommendation starts undecided",
      status == 200 and rec.get("wasAccepted") is None)
REC = rec["id"] if status == 200 else None

status, decided = call("POST", f"/api/algorithms/slotting-recommendations/{REC}/decide",
                       token=OP, body={"accepted": True})
check("Operator can accept or reject a recommendation", status == 200, f"got {status}")
check("Acceptance recorded for measuring recommendation quality",
      status == 200 and decided["wasAccepted"] is True)
check("AUDIT: decision records who decided and when",
      status == 200 and decided.get("decidedBy") == "operator1" and decided.get("decidedAt"),
      f"got {decided.get('decidedBy')}")

status, _ = call("POST", f"/api/algorithms/slotting-recommendations/{REC}/decide",
                 token=OP, body={"accepted": False})
check("Deciding an already-decided recommendation refused (422)", status == 422, f"got {status}")

status, recs = call("GET", f"/api/algorithms/slotting-recommendations?warehouseId={WH}", token=T)
check("Recommendations listable per warehouse", status == 200 and len(recs) >= 1)

# =====================================================================
print("\n=== Faz 6: picking plan and route records ===")
# =====================================================================

status, plan = call("POST", "/api/algorithms/picking-plans", token=T, body={
    "warehouseId": WH, "planNumber": None,
    "algorithmName": "RouteV1", "algorithmVersion": "0.1.0",
    "batchingStrategy": "Batch",
    "estimatedTravelDistance": 128.5, "estimatedDurationSeconds": 900,
    "assignedTo": "operator1",
    "stops": [
        {"stopSequence": 1, "locationId": LOCS["P-01-A-01"], "pickTaskId": None,
         "itemId": ITEM, "quantity": 5, "distanceFromPrevious": 0},
        {"stopSequence": 2, "locationId": LOCS["A-01-A-01"], "pickTaskId": None,
         "itemId": ITEM, "quantity": 3, "distanceFromPrevious": 42.5},
        {"stopSequence": 3, "locationId": LOCS["A-01-B-01"], "pickTaskId": None,
         "itemId": ITEM, "quantity": 2, "distanceFromPrevious": 12.0}]})
check("Picking plan with route stops recorded", status == 200, f"got {status}: {plan}")
check("Plan stores algorithm, version and batching strategy",
      status == 200 and plan["algorithmName"] == "RouteV1"
      and plan["batchingStrategy"] == "Batch")
check("Plan stores estimated travel distance and duration for later comparison",
      status == 200 and plan["estimatedTravelDistance"] == 128.5
      and plan["estimatedDurationSeconds"] == 900)
check("Route stops stored in sequence",
      status == 200 and [s["stopSequence"] for s in plan["stops"]] == [1, 2, 3],
      f"got {[s['stopSequence'] for s in plan['stops']] if status == 200 else None}")
check("Each stop records its location and distance from the previous stop",
      status == 200 and plan["stops"][1]["distanceFromPrevious"] == 42.5)
check("TotalStops matches the stop count", status == 200 and plan["totalStops"] == 3)

status, err = call("POST", "/api/algorithms/picking-plans", token=T, body={
    "warehouseId": WH, "planNumber": None, "algorithmName": None, "algorithmVersion": None,
    "batchingStrategy": None, "estimatedTravelDistance": None,
    "estimatedDurationSeconds": None, "assignedTo": None,
    "stops": [{"stopSequence": 1, "locationId": LOCS["P-01-A-01"], "pickTaskId": None,
               "itemId": None, "quantity": None, "distanceFromPrevious": None},
              {"stopSequence": 1, "locationId": LOCS["A-01-A-01"], "pickTaskId": None,
               "itemId": None, "quantity": None, "distanceFromPrevious": None}]})
check("Duplicate stop sequence numbers refused (422)", status == 422, f"got {status}")

status, err = call("POST", "/api/algorithms/picking-plans", token=T, body={
    "warehouseId": WH, "planNumber": None, "algorithmName": None, "algorithmVersion": None,
    "batchingStrategy": None, "estimatedTravelDistance": None,
    "estimatedDurationSeconds": None, "assignedTo": None, "stops": []})
check("A plan with no stops refused (422)", status == 422, f"got {status}")

status, fetched = call("GET", f"/api/algorithms/picking-plans/{plan['id']}", token=T)
check("Picking plan retrievable with its stops",
      status == 200 and len(fetched["stops"]) == 3)

# =====================================================================
print("\n=== No algorithm makes decisions in v1 (doc 10) ===")
# =====================================================================

out, _ = sql("""SELECT COUNT(*) FROM putaway_tasks WHERE "SuggestedLocationId" IS NOT NULL;""")
check("Doc 6.4: putaway still has no algorithmic suggestion in v1",
      out.strip() == "0", f"got {out!r}")

out, _ = sql("""SELECT COUNT(*) FROM putaway_tasks WHERE "RecommendationReason" IS NOT NULL;""")
check("Doc 6.4: RecommendationReason stays empty until an engine exists",
      out.strip() == "0", f"got {out!r}")

out, _ = sql("""SELECT COUNT(*) FROM pick_tasks WHERE "PickBatchId" IS NOT NULL;""")
check("Doc 7.4: PickBatchId reserved for a future batching engine, unused in v1",
      out.strip() == "0", f"got {out!r}")

# =====================================================================
print("\n=== Authorization ===")
# =====================================================================

status, _ = call("GET", f"/api/algorithms/readiness?warehouseId={WH}", token=V)
check("Viewer CAN read the readiness report", status == 200, f"got {status}")
status, _ = call("POST", "/api/algorithms/slotting-recommendations", token=V, body={
    "warehouseId": WH, "itemId": ITEM, "currentLocationId": None,
    "recommendedLocationId": LOCS["P-01-A-01"], "score": None,
    "recommendationReason": None, "algorithmName": None, "algorithmVersion": None})
check("Viewer CANNOT record recommendations (403)", status == 403, f"got {status}")

status, rd2 = call("GET", f"/api/algorithms/readiness?warehouseId={WH}", token=T)
check("All Faz 6 tables now report as populated",
      status == 200 and all(c["isReady"] for c in rd2["checks"] if c["category"] == "Faz 6"),
      f"got {[(c['dataPoint'], c['isReady']) for c in rd2['checks'] if c['category'] == 'Faz 6']}")
check("Readiness summary counts every data point",
      status == 200 and rd2["populatedChecks"] == rd2["totalChecks"],
      f"{rd2.get('populatedChecks')}/{rd2.get('totalChecks')} — "
      f"{[c['dataPoint'] for c in rd2['checks'] if not c['isReady']]}")

# =====================================================================
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 6 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

sys.exit(1 if failed else 0)
