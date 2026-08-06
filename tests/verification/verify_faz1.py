"""End-to-end verification of Faz 1 (Master Data) against the running API.

Exercises every documented section 3 and 4 endpoint plus the auth layer, and asserts
the business rules the spec states. Prints PASS/FAIL per check and exits non-zero on
any failure.
"""
import json
import sys
import urllib.request
import urllib.error

BASE = "http://localhost:5080"
results = []
state = {}


def call(method, path, body=None, token=None, expect=None):
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
    mark = "PASS" if condition else "FAIL"
    print(f"[{mark}] {name}" + (f"  -- {detail}" if detail and not condition else ""))


# ---------------------------------------------------------------- auth
print("\n=== Authentication & authorization ===")

status, _ = call("GET", "/api/items")
check("Unauthenticated request is rejected (401)", status == 401, f"got {status}")

status, body = call("POST", "/api/auth/login",
                    {"username": "admin", "password": "WrongPassword"})
check("Bad password is rejected (401)", status == 401, f"got {status}")

status, body = call("POST", "/api/auth/login",
                    {"username": "admin", "password": "DevAdmin!2026"})
check("Admin login succeeds", status == 200, f"got {status}: {body}")
admin = body["accessToken"] if status == 200 else None
state["accountId"] = body["user"]["accountId"] if status == 200 else None
check("Token carries account claim", state.get("accountId") is not None)
check("Admin role returned", status == 200 and body["user"]["role"] == "Admin")

status, body = call("GET", "/api/auth/me", token=admin)
check("GET /api/auth/me returns profile", status == 200 and body["username"] == "admin")

# ---------------------------------------------------------------- seeded reference data
print("\n=== Seeded reference data (doc 4.3, 5.2) ===")

status, uoms = call("GET", "/api/units-of-measure", token=admin)
check("Seeded UOMs present (EA/PK/CS/PL)", status == 200 and len(uoms) >= 4,
      f"got {status}: {uoms}")
uom_by_code = {u["code"]: u["id"] for u in uoms} if status == 200 else {}
check("Each/Pack/Case/Pallet all seeded",
      all(c in uom_by_code for c in ["EA", "PK", "CS", "PL"]))

# ---------------------------------------------------------------- warehouse hierarchy
print("\n=== Doc 3: Account -> Warehouse -> Zone -> Location ===")

status, accounts = call("GET", "/api/accounts", token=admin)
check("Account list scoped to caller", status == 200 and len(accounts) == 1,
      f"got {status}: {accounts}")

status, wh = call("POST", "/api/warehouses", token=admin, body={
    "code": "WH01", "name": "Istanbul Main Depot",
    "address": "Tuzla Organize Sanayi", "timeZone": "Europe/Istanbul"})
check("Create warehouse (POST /api/warehouses)", status == 201, f"got {status}: {wh}")
state["wh"] = wh["id"] if status == 201 else None

status, dup = call("POST", "/api/warehouses", token=admin, body={
    "code": "WH01", "name": "Duplicate", "address": None, "timeZone": None})
check("Duplicate warehouse code rejected (409)", status == 409, f"got {status}")

zones = {}
for code, name, ztype, prio in [
        ("RCV", "Receiving Dock", "Receiving", 1),
        ("RSV", "Reserve Storage", "ReserveStorage", 2),
        ("PCK", "Pick Face", "Picking", 3),
        ("PACK", "Packing", "Packing", 4),
        ("SHIP", "Shipping Staging", "ShippingStaging", 5),
        ("COLD", "Cold Storage", "ColdStorage", 6),
        ("DMG", "Damaged", "Damaged", 7)]:
    status, z = call("POST", "/api/zones", token=admin, body={
        "warehouseId": state["wh"], "code": code, "name": name,
        "zoneType": ztype, "priority": prio})
    if status == 201:
        zones[code] = z["id"]
check("Create all 7 zone types (POST /api/zones)", len(zones) == 7, f"created {len(zones)}")
state["zones"] = zones

status, zlist = call("GET", f"/api/zones?warehouseId={state['wh']}&zoneType=Receiving", token=admin)
check("Filter zones by ZoneType", status == 200 and len(zlist) == 1 and zlist[0]["code"] == "RCV",
      f"got {status}: {zlist}")

# ---------------------------------------------------------------- location profiles (doc 3.5)
print("\n=== Doc 3.5: Location profiles ===")

status, amb = call("POST", "/api/location-profiles", token=admin, body={
    "code": "PALLET-STD", "name": "Standard Pallet Rack", "locationType": "PalletRack",
    "maxWeight": 1200, "maxVolume": 2.5, "allowedItemCategoryId": None,
    "temperatureMin": None, "temperatureMax": None,
    "isMixedItemAllowed": True, "isMixedLotAllowed": True})
check("Create ambient location profile", status == 201, f"got {status}: {amb}")
state["profile_ambient"] = amb["id"] if status == 201 else None

status, cold = call("POST", "/api/location-profiles", token=admin, body={
    "code": "COLD-SHELF", "name": "Cold Storage Shelf", "locationType": "ColdStorage",
    "maxWeight": 300, "maxVolume": 1.0, "allowedItemCategoryId": None,
    "temperatureMin": 2, "temperatureMax": 5,
    "isMixedItemAllowed": True, "isMixedLotAllowed": True})
check("Create cold-chain profile with temperature band", status == 201, f"got {status}")
state["profile_cold"] = cold["id"] if status == 201 else None

status, frz = call("POST", "/api/location-profiles", token=admin, body={
    "code": "FREEZER", "name": "Deep Freeze", "locationType": "ColdStorage",
    "maxWeight": 300, "maxVolume": 1.0, "allowedItemCategoryId": None,
    "temperatureMin": -20, "temperatureMax": -15,
    "isMixedItemAllowed": True, "isMixedLotAllowed": True})
check("Create freezer profile", status == 201, f"got {status}")
state["profile_freezer"] = frz["id"] if status == 201 else None

status, bad = call("POST", "/api/location-profiles", token=admin, body={
    "code": "BADTEMP", "name": "Invalid", "locationType": "ColdStorage",
    "maxWeight": None, "maxVolume": None, "allowedItemCategoryId": None,
    "temperatureMin": 10, "temperatureMax": -10,
    "isMixedItemAllowed": True, "isMixedLotAllowed": True})
check("Inverted temperature band rejected (422)", status == 422, f"got {status}")

# ---------------------------------------------------------------- locations (doc 3.4 + 10)
print("\n=== Doc 3.4 + 10: Locations with algorithm-readiness fields ===")

locs = {}
loc_specs = [
    ("RECV-01", "RCV", "FloorStorage", None, 0, 0, False, True),
    ("A-01-A-01", "RSV", "PalletRack", state["profile_ambient"], 10, 5, True, True),
    ("A-01-B-01", "RSV", "PalletRack", state["profile_ambient"], 12, 5, True, True),
    ("P-01-A-01", "PCK", "PickFace", state["profile_ambient"], 4, 2, True, True),
    ("C-01-A-01", "COLD", "ColdStorage", state["profile_cold"], 30, 20, True, True),
    ("PACK-01", "PACK", "FloorStorage", None, 2, 1, True, False),
    ("SHIP-01", "SHIP", "FloorStorage", None, 1, 1, True, False),
    ("DMG-01", "DMG", "FloorStorage", None, 40, 30, False, True),
    ("F-01-A-01", "COLD", "ColdStorage", state["profile_freezer"], 35, 25, True, True),
]
for code, zone, ltype, profile, dist_recv, dist_ship, pickable, putaway in loc_specs:
    parts = code.split("-")
    status, l = call("POST", "/api/locations", token=admin, body={
        "warehouseId": state["wh"], "zoneId": state["zones"][zone], "code": code,
        "aisle": parts[0] if len(parts) == 4 else None,
        "bay": parts[1] if len(parts) == 4 else None,
        "level": parts[2] if len(parts) == 4 else None,
        "position": parts[3] if len(parts) == 4 else None,
        "locationType": ltype, "locationProfileId": profile,
        "pickSequence": len(locs) + 1, "putawaySequence": len(locs) + 1,
        "coordinateX": len(locs) * 2.5, "coordinateY": 1.0, "coordinateZ": 0.0,
        "maxWeight": None, "maxVolume": None,
        "isPickable": pickable, "isPutawayAllowed": putaway,
        "distanceToReceiving": dist_recv, "distanceToPacking": dist_ship,
        "distanceToShipping": dist_ship,
        "accessibilityScore": 80, "maxConcurrentWorkers": 1})
    if status == 201:
        locs[code] = l["id"]
    else:
        print(f"      location {code} failed: {status} {l}")
check("Create 9 locations across zones", len(locs) == 9, f"created {len(locs)}")
state["locs"] = locs

status, loc = call("GET", f"/api/locations/{locs['A-01-A-01']}", token=admin)
check("Location parses A-01-A-01 into Aisle/Bay/Level/Position",
      status == 200 and loc["aisle"] == "A" and loc["bay"] == "01"
      and loc["level"] == "A" and loc["position"] == "01",
      f"got {status}: {loc}")
check("Doc 10 coordinate fields persisted",
      status == 200 and loc["coordinateX"] is not None and loc["pickSequence"] is not None)
check("Doc 10 distance + accessibility fields persisted",
      status == 200 and loc["distanceToReceiving"] is not None
      and loc["accessibilityScore"] == 80 and loc["maxConcurrentWorkers"] == 1)

# ---------------------------------------------------------------- categories & attributes
print("\n=== Doc 4.2: Dynamic attribute system ===")

status, cat = call("POST", "/api/item-categories", token=admin,
                   body={"code": "APPAREL", "name": "Apparel", "parentCategoryId": None})
check("Create item category", status == 200, f"got {status}: {cat}")
state["cat"] = cat["id"] if status == 200 else None

status, sub = call("POST", "/api/item-categories", token=admin,
                   body={"code": "TSHIRT", "name": "T-Shirts", "parentCategoryId": state["cat"]})
check("Create nested category", status == 200 and sub["parentCategoryName"] == "Apparel",
      f"got {status}: {sub}")

status, cyc = call("PUT", f"/api/item-categories/{state['cat']}", token=admin,
                   body={"name": "Apparel", "parentCategoryId": sub["id"], "isActive": True})
check("Circular category hierarchy rejected (422)", status == 422, f"got {status}")

attrs = {}
for code, name, dtype, slotting, picking in [
        ("COLOR", "Color", "Text", False, False),
        ("SIZE", "Size", "Text", False, False),
        ("SEASON", "Season", "Text", True, False),
        ("FRAGILE_RANK", "Fragility Rank", "Number", False, True),
        ("HAZARD", "Hazard Class", "Text", True, True)]:
    status, a = call("POST", "/api/attribute-definitions", token=admin, body={
        "code": code, "name": name, "dataType": dtype, "isRequired": False,
        "isFilterable": True, "isSlottingRelevant": slotting, "isPickingRelevant": picking})
    if status == 200:
        attrs[code] = a["id"]
check("Create 5 attribute definitions", len(attrs) == 5, f"created {len(attrs)}")
state["attrs"] = attrs

status, adefs = call("GET", "/api/attribute-definitions", token=admin)
slotting_flagged = [a for a in adefs if a["isSlottingRelevant"]] if status == 200 else []
check("IsSlottingRelevant / IsPickingRelevant persisted (doc 4.2)",
      len(slotting_flagged) == 2, f"slotting-relevant: {len(slotting_flagged)}")

# ---------------------------------------------------------------- item master (doc 4.1)
print("\n=== Doc 4.1: Item master ===")

status, item = call("POST", "/api/items", token=admin, body={
    "sku": "TSHIRT-001", "name": "Cotton T-Shirt Black XL",
    "description": "Summer collection", "categoryId": state["cat"],
    "baseUomId": uom_by_code["EA"],
    "weight": 0.2, "length": 0.3, "width": 0.25, "height": 0.02, "volume": None,
    "isLotTracked": False, "isSerialTracked": False, "isExpirationTracked": False,
    "shelfLifeDays": None, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": False,
    "minimumStorageTemperature": None, "maximumStorageTemperature": None,
    "stackableQuantity": 20, "defaultPutawayZoneId": state["zones"]["RSV"],
    "defaultPickZoneId": state["zones"]["PCK"]})
check("Create item (POST /api/items)", status == 201, f"got {status}: {item}")
state["item"] = item["id"] if status == 201 else None
check("Volume auto-computed from dimensions",
      status == 201 and item["volume"] is not None and abs(item["volume"] - 0.0015) < 1e-6,
      f"volume={item.get('volume') if status == 201 else None}")
check("Base UOM auto-registered as 1:1 conversion",
      status == 201 and any(u["conversionQuantity"] == 1 for u in item["uoms"]))

status, dup = call("POST", "/api/items", token=admin, body={
    "sku": "TSHIRT-001", "name": "Duplicate SKU", "description": None,
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": None, "length": None, "width": None, "height": None, "volume": None,
    "isLotTracked": False, "isSerialTracked": False, "isExpirationTracked": False,
    "shelfLifeDays": None, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": False, "minimumStorageTemperature": None,
    "maximumStorageTemperature": None, "stackableQuantity": None,
    "defaultPutawayZoneId": None, "defaultPickZoneId": None})
check("Duplicate SKU in same account rejected (409, doc 4.1)", status == 409, f"got {status}")

# Rule 11.6: expiration tracking needs a shelf life
status, bad = call("POST", "/api/items", token=admin, body={
    "sku": "BADEXP-001", "name": "Bad expiration config", "description": None,
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": None, "length": None, "width": None, "height": None, "volume": None,
    "isLotTracked": True, "isSerialTracked": False, "isExpirationTracked": True,
    "shelfLifeDays": None, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": False, "minimumStorageTemperature": None,
    "maximumStorageTemperature": None, "stackableQuantity": None,
    "defaultPutawayZoneId": None, "defaultPickZoneId": None})
check("Expiration-tracked item without shelf life is allowed (date is required at receipt, doc 11.6)",
      status == 201, f"got {status}: {bad}")

# Expiration without lot tracking has nowhere to store the date
status, bad2 = call("POST", "/api/items", token=admin, body={
    "sku": "BADEXP-002", "name": "Expiration without lot", "description": None,
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": None, "length": None, "width": None, "height": None, "volume": None,
    "isLotTracked": False, "isSerialTracked": False, "isExpirationTracked": True,
    "shelfLifeDays": 30, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": False, "minimumStorageTemperature": None,
    "maximumStorageTemperature": None, "stackableQuantity": None,
    "defaultPutawayZoneId": None, "defaultPickZoneId": None})
check("Expiration tracking without lot tracking rejected (422)", status == 422, f"got {status}")

# Temperature-controlled without a band
status, bad3 = call("POST", "/api/items", token=admin, body={
    "sku": "BADTEMP-001", "name": "Temp controlled, no band", "description": None,
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": None, "length": None, "width": None, "height": None, "volume": None,
    "isLotTracked": False, "isSerialTracked": False, "isExpirationTracked": False,
    "shelfLifeDays": None, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": True, "minimumStorageTemperature": None,
    "maximumStorageTemperature": None, "stackableQuantity": None,
    "defaultPutawayZoneId": None, "defaultPickZoneId": None})
check("Temperature-controlled item without a band rejected (422)", status == 422, f"got {status}")

# A valid lot + expiration tracked item, used later by Faz 2-4
status, milk = call("POST", "/api/items", token=admin, body={
    "sku": "MILK-1L", "name": "Full Fat Milk 1L", "description": "Cold chain",
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": 1.03, "length": 0.07, "width": 0.07, "height": 0.24, "volume": None,
    "isLotTracked": True, "isSerialTracked": False, "isExpirationTracked": True,
    "shelfLifeDays": 14, "isFragile": False, "isHazardous": False,
    "isTemperatureControlled": True,
    "minimumStorageTemperature": 2, "maximumStorageTemperature": 6,
    "stackableQuantity": 6, "defaultPutawayZoneId": state["zones"]["COLD"],
    "defaultPickZoneId": state["zones"]["COLD"]})
check("Create lot+expiration+temperature tracked item", status == 201, f"got {status}: {milk}")
state["milk"] = milk["id"] if status == 201 else None

# A serial-tracked item for rule 11.5 later
status, phone = call("POST", "/api/items", token=admin, body={
    "sku": "PHONE-X1", "name": "Smartphone X1", "description": None,
    "categoryId": None, "baseUomId": uom_by_code["EA"],
    "weight": 0.19, "length": 0.16, "width": 0.08, "height": 0.01, "volume": None,
    "isLotTracked": False, "isSerialTracked": True, "isExpirationTracked": False,
    "shelfLifeDays": None, "isFragile": True, "isHazardous": False,
    "isTemperatureControlled": False, "minimumStorageTemperature": None,
    "maximumStorageTemperature": None, "stackableQuantity": 10,
    "defaultPutawayZoneId": None, "defaultPickZoneId": None})
check("Create serial-tracked item", status == 201, f"got {status}")
state["phone"] = phone["id"] if status == 201 else None

# ---------------------------------------------------------------- attribute values
print("\n=== Doc 4.2: Attribute values on an item ===")

for code, field, value in [("COLOR", "textValue", "Black"),
                           ("SIZE", "textValue", "XL"),
                           ("SEASON", "textValue", "Summer")]:
    body = {"attributeDefinitionId": state["attrs"][code],
            "textValue": None, "numberValue": None,
            "booleanValue": None, "dateValue": None}
    body[field] = value
    status, r = call("PUT", f"/api/items/{state['item']}/attributes", token=admin, body=body)
check("Set Color/Size/Season attributes", status == 200, f"got {status}")

status, it = call("GET", f"/api/items/{state['item']}", token=admin)
attr_map = {a["attributeCode"]: a["textValue"] for a in it["attributes"]} if status == 200 else {}
check("Doc 4.2 example reproduced (Color=Black, Size=XL, Season=Summer)",
      attr_map.get("COLOR") == "Black" and attr_map.get("SIZE") == "XL"
      and attr_map.get("SEASON") == "Summer", f"got {attr_map}")

# Type mismatch must be refused
status, r = call("PUT", f"/api/items/{state['item']}/attributes", token=admin, body={
    "attributeDefinitionId": state["attrs"]["FRAGILE_RANK"],
    "textValue": "not-a-number", "numberValue": None,
    "booleanValue": None, "dateValue": None})
check("Attribute type mismatch rejected (422)", status == 422, f"got {status}")

status, r = call("PUT", f"/api/items/{state['item']}/attributes", token=admin, body={
    "attributeDefinitionId": state["attrs"]["FRAGILE_RANK"],
    "textValue": None, "numberValue": 3, "booleanValue": None, "dateValue": None})
check("Number attribute accepted into NumberValue column",
      status == 200 and any(a["attributeCode"] == "FRAGILE_RANK" and a["numberValue"] == 3
                            for a in r["attributes"]), f"got {status}")

# ---------------------------------------------------------------- UOM + barcodes
print("\n=== Doc 4.3 / 4.4: UOM hierarchy and barcodes ===")

status, r = call("POST", f"/api/items/{state['item']}/uoms", token=admin, body={
    "uomId": uom_by_code["PK"], "conversionQuantity": 6, "barcode": None,
    "length": None, "width": None, "height": None, "weight": 1.2,
    "isReceivingUom": False, "isPickingUom": True, "isShippingUom": False})
check("Add Pack = 6 conversion", status == 200, f"got {status}")

status, r = call("POST", f"/api/items/{state['item']}/uoms", token=admin, body={
    "uomId": uom_by_code["CS"], "conversionQuantity": 24, "barcode": None,
    "length": None, "width": None, "height": None, "weight": 4.8,
    "isReceivingUom": True, "isPickingUom": False, "isShippingUom": True})
check("Add Case = 24 conversion (doc 4.3 example)", status == 200, f"got {status}")
conv = {u["uomCode"]: u["conversionQuantity"] for u in r["uoms"]} if status == 200 else {}
check("Doc 4.3 hierarchy stored (EA=1, PK=6, CS=24)",
      conv.get("EA") == 1 and conv.get("PK") == 6 and conv.get("CS") == 24, f"got {conv}")

status, r = call("POST", f"/api/items/{state['item']}/uoms", token=admin, body={
    "uomId": uom_by_code["PL"], "conversionQuantity": 0, "barcode": None,
    "length": None, "width": None, "height": None, "weight": None,
    "isReceivingUom": False, "isPickingUom": False, "isShippingUom": False})
check("Zero conversion quantity rejected (422)", status == 422, f"got {status}")

status, r = call("POST", f"/api/items/{state['item']}/barcodes", token=admin, body={
    "uomId": uom_by_code["EA"], "barcode": "8691234567890",
    "barcodeType": "Ean13", "isPrimary": True})
check("Add primary EAN-13 barcode", status == 200, f"got {status}")

status, r = call("POST", f"/api/items/{state['item']}/barcodes", token=admin, body={
    "uomId": uom_by_code["CS"], "barcode": "18691234567897",
    "barcodeType": "Itf14", "isPrimary": True})
primaries = [b for b in r["barcodes"] if b["isPrimary"]] if status == 200 else []
check("Only one primary barcode per item (doc 4.4)", len(primaries) == 1,
      f"primaries={len(primaries)}")

status, r = call("POST", f"/api/items/{state['milk']}/barcodes", token=admin, body={
    "uomId": uom_by_code["EA"], "barcode": "8691234567890",
    "barcodeType": "Ean13", "isPrimary": False})
check("Barcode reuse across items rejected (409)", status == 409, f"got {status}")

status, found = call("GET", "/api/items/by-barcode/8691234567890", token=admin)
check("Barcode scan resolves to the right item",
      status == 200 and found["sku"] == "TSHIRT-001", f"got {status}")

status, found = call("GET", "/api/items/by-sku/MILK-1L", token=admin)
check("Lookup by SKU works", status == 200 and found["sku"] == "MILK-1L", f"got {status}")

# ---------------------------------------------------------------- locations/available
print("\n=== Doc 12: GET /api/locations/available ===")

status, avail = call("GET", f"/api/locations/available?WarehouseId={state['wh']}&PutawayOnly=true",
                     token=admin)
codes = [l["code"] for l in avail] if status == 200 else []
check("Available locations returns putaway-allowed only",
      status == 200 and "PACK-01" not in codes and "A-01-A-01" in codes,
      f"got {status}: {codes}")

status, avail = call(
    "GET",
    f"/api/locations/available?WarehouseId={state['wh']}&ItemId={state['milk']}",
    token=admin)
codes = [l["code"] for l in avail] if status == 200 else []
check("Rule 11.8: cold-chain item offered only the matching cold location",
      status == 200 and codes == ["C-01-A-01"], f"got {status}: {codes}")
check("Rule 11.8: freezer (-20..-15) excluded for a 2..6 item despite being 'cold'",
      status == 200 and "F-01-A-01" not in codes, f"got {codes}")

status, avail = call(
    "GET",
    f"/api/locations/available?WarehouseId={state['wh']}&ItemId={state['item']}&EmptyOnly=true",
    token=admin)
check("Ambient item offered multiple ambient locations",
      status == 200 and len(avail) >= 3, f"got {status}: {len(avail) if status == 200 else None}")

# ---------------------------------------------------------------- suppliers / customers
print("\n=== Supporting master data ===")

status, sup = call("POST", "/api/suppliers", token=admin, body={
    "code": "SUP01", "name": "Anadolu Tekstil A.S.", "contactName": "Satis",
    "email": "satis@anadolutekstil.example", "phone": "+90 212 000 0000",
    "address": "Bursa"})
check("Create supplier", status == 200, f"got {status}")
state["supplier"] = sup["id"] if status == 200 else None

status, cust = call("POST", "/api/customers", token=admin, body={
    "code": "CUST01", "name": "Migros Ticaret", "contactName": "Satinalma",
    "email": "satinalma@migros.example", "phone": "+90 216 000 0000",
    "shippingAddress": "Atasehir, Istanbul"})
check("Create customer", status == 200, f"got {status}")
state["customer"] = cust["id"] if status == 200 else None

# ---------------------------------------------------------------- RBAC
print("\n=== Role-based authorization ===")

status, viewer = call("POST", "/api/auth/users", token=admin, body={
    "username": "viewer1", "email": "viewer1@ims.local", "fullName": "Read Only User",
    "password": "ViewerPass!2026", "role": "Viewer", "defaultWarehouseId": state["wh"]})
check("Admin can create a Viewer user", status == 201, f"got {status}: {viewer}")

status, body = call("POST", "/api/auth/login",
                    {"username": "viewer1", "password": "ViewerPass!2026"})
viewer_token = body["accessToken"] if status == 200 else None
check("Viewer can log in", status == 200, f"got {status}")

status, _ = call("GET", "/api/items", token=viewer_token)
check("Viewer CAN read items (200)", status == 200, f"got {status}")

status, _ = call("POST", "/api/warehouses", token=viewer_token,
                 body={"code": "WH99", "name": "Should fail", "address": None, "timeZone": None})
check("Viewer CANNOT create master data (403)", status == 403, f"got {status}")

status, operator = call("POST", "/api/auth/users", token=admin, body={
    "username": "operator1", "email": "op1@ims.local", "fullName": "Floor Operator",
    "password": "OperatorPass!2026", "role": "Operator", "defaultWarehouseId": state["wh"]})
check("Admin can create an Operator user", status == 201, f"got {status}")

status, body = call("POST", "/api/auth/login",
                    {"username": "operator1", "password": "OperatorPass!2026"})
op_token = body["accessToken"] if status == 200 else None
status, _ = call("POST", "/api/warehouses", token=op_token,
                 body={"code": "WH98", "name": "Should fail", "address": None, "timeZone": None})
check("Operator CANNOT create master data (403)", status == 403, f"got {status}")

status, _ = call("POST", "/api/auth/users", token=viewer_token, body={
    "username": "hacker", "email": "h@x.local", "fullName": "X",
    "password": "Whatever!2026", "role": "Admin", "defaultWarehouseId": None})
check("Viewer CANNOT create users (403)", status == 403, f"got {status}")

# ---------------------------------------------------------------- validation / errors
print("\n=== Error handling ===")

status, err = call("GET", f"/api/items/{'0'*8}-0000-0000-0000-{'0'*12}", token=admin)
check("Unknown id returns 404 with errorCode",
      status == 404 and isinstance(err, dict) and err.get("errorCode") == "NOT_FOUND",
      f"got {status}: {err}")
check("Error payload includes traceId",
      status == 404 and isinstance(err, dict) and "traceId" in err)

status, err = call("GET", f"/api/zones/{state['wh']}", token=admin)
check("Cross-entity id mismatch returns 404", status == 404, f"got {status}")

# ---------------------------------------------------------------- summary
print("\n" + "=" * 62)
passed = sum(1 for _, ok, _ in results if ok)
failed = [r for r in results if not r[1]]
print(f"FAZ 1 RESULT: {passed}/{len(results)} checks passed")
if failed:
    print("\nFAILURES:")
    for name, _, detail in failed:
        print(f"  - {name}  ({detail})")

with open("faz1_state.json", "w") as f:
    json.dump(state, f, indent=2)
print("\nIds written to faz1_state.json for the Faz 2+ scripts.")

sys.exit(1 if failed else 0)
