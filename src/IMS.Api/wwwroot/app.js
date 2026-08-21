/* Minimal demo client for the IMS API.
 *
 * Vanilla JS, no framework, no build step. It exists to exercise and demonstrate the
 * backend end to end; it is deliberately not a designed UI.
 *
 * The token lives in sessionStorage, so it is cleared when the tab closes and is never
 * written to localStorage where it would outlive the session.
 */

const State = {
  token: sessionStorage.getItem('ims.token') || null,
  user: null,
  warehouseId: null,
  cache: { items: [], locations: [], uoms: [], suppliers: [], customers: [], warehouses: [] },
  // Id of the record currently loaded into a form, or null when that form is adding a new one.
  editing: { supplier: null, customer: null, location: null }
};

/* When the API rejects a request it names the business rule that was broken. The rule
   itself means nothing to whoever is standing at the screen, so each one is spelled out
   in plain language and appended to the message instead. */
const RULE_EXPLANATIONS = {
  5: 'Serial-tracked items are handled one unit at a time. Every movement has to name the '
   + 'serial number it applies to, and a single serial can never hold more than one unit.',
  6: 'Expiration-tracked items must always carry an expiration date. Supply the date with '
   + 'the stock, or give the item a shelf life so the system can work the date out itself.',
  8: 'A location only accepts stock it is equipped to hold. Hazardous goods need a hazmat '
   + 'zone, temperature-controlled goods need a location inside their temperature range, '
   + 'and some locations take only one item category or refuse to mix two items at once.',
  10: 'Stock transactions are a permanent audit trail. New ones can be added, but an '
    + 'existing one can never be edited or deleted.'
};

// --------------------------------------------------------------- HTTP

async function api(method, path, body) {
  const headers = { 'Content-Type': 'application/json' };
  if (State.token) headers['Authorization'] = 'Bearer ' + State.token;

  const res = await fetch(path, {
    method,
    headers,
    body: body === undefined ? undefined : JSON.stringify(body)
  });

  if (res.status === 401 && State.token) {
    signOut();
    throw new Error('Session expired. Please sign in again.');
  }

  const text = await res.text();
  const payload = text ? JSON.parse(text) : null;

  if (!res.ok) {
    // The API returns RFC7807-style problems; surface the useful parts, including the
    // per-item shortfall list that acceptance scenario 3 requires.
    let msg = payload?.detail || payload?.title || `The request failed (HTTP ${res.status}).`;
    if (payload?.errors) {
      msg += '\n\nThese fields need attention:\n' + Object.entries(payload.errors)
        .map(([k, v]) => `• ${k} — ${[].concat(v).join(' ')}`).join('\n');
    }
    if (payload?.shortfalls?.length) {
      msg += '\n\nThere is not enough stock to cover the order:\n' + payload.shortfalls
        .map(s => `• ${s.sku} — ${s.requestedQuantity} requested, ${s.availableQuantity} available, ` +
                  `${s.shortQuantity} short`)
        .join('\n');
    }
    const explanation = RULE_EXPLANATIONS[payload?.ruleNumber];
    if (explanation) msg += `\n\nWhy: ${explanation}`;
    throw new Error(msg);
  }

  return payload;
}

// --------------------------------------------------------------- UI helpers

function toast(message, isError) {
  const el = document.getElementById('toast');
  el.textContent = message;
  el.className = isError ? 'error' : '';
  el.style.display = 'block';
  clearTimeout(toast._t);
  // Errors now carry a written explanation, so they need long enough on screen to read.
  toast._t = setTimeout(() => { el.style.display = 'none'; }, isError ? 15000 : 3500);
}

/** Escapes text before it reaches innerHTML, so item names cannot inject markup. */
function esc(v) {
  if (v === null || v === undefined) return '';
  return String(v).replace(/[&<>"']/g, c =>
    ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

function num(v, dp = 2) {
  if (v === null || v === undefined) return '';
  return Number(v).toLocaleString(undefined, { minimumFractionDigits: 0, maximumFractionDigits: dp });
}

function pill(text, kind) {
  return `<span class="pill ${kind || ''}">${esc(text)}</span>`;
}

/** Renders rows into a table. `cols` is [{ head, get, cls }]. */
function table(target, rows, cols, emptyMessage) {
  const el = typeof target === 'string' ? document.getElementById(target) : target;
  if (!rows || rows.length === 0) {
    el.innerHTML = `<p class="muted">${esc(emptyMessage || 'Nothing to show.')}</p>`;
    return;
  }
  el.innerHTML =
    `<table><thead><tr>${cols.map(c => `<th class="${c.cls || ''}">${esc(c.head)}</th>`).join('')}</tr></thead>` +
    `<tbody>${rows.map(r =>
      `<tr>${cols.map(c => `<td class="${c.cls || ''}">${c.get(r)}</td>`).join('')}</tr>`).join('')}</tbody></table>`;
}

/** Trimmed value of a text input, or null when the field was left empty. */
function val(id) {
  const v = document.getElementById(id).value.trim();
  return v === '' ? null : v;
}

/** Numeric value of an input, or null when it was left empty. */
function numberOrNull(id) {
  const v = document.getElementById(id).value.trim();
  return v === '' ? null : Number(v);
}

/** A date input as an ISO timestamp, or null when no date was picked. */
function expiryOrNull(id) {
  const v = document.getElementById(id).value;
  return v ? new Date(v).toISOString() : null;
}

function fill(selectId, rows, valueKey, labelFn, includeBlank) {
  const el = document.getElementById(selectId);
  if (!el) return;
  const previous = el.value;
  el.innerHTML = (includeBlank ? '<option value="">—</option>' : '') +
    rows.map(r => `<option value="${esc(r[valueKey])}">${esc(labelFn(r))}</option>`).join('');
  if (previous) el.value = previous;
}

/** Wires a delegated click handler for buttons carrying data-act. */
function onAction(containerId, handler) {
  document.getElementById(containerId).addEventListener('click', async e => {
    const btn = e.target.closest('button[data-act]');
    if (!btn) return;
    btn.disabled = true;
    try {
      await handler(btn.dataset.act, btn.dataset.id, btn);
    } catch (err) {
      toast(err.message, true);
    } finally {
      btn.disabled = false;
    }
  });
}

async function guard(fn) {
  try { await fn(); } catch (err) { toast(err.message, true); }
}

// --------------------------------------------------------------- auth

async function signIn() {
  const username = document.getElementById('login-user').value.trim();
  const password = document.getElementById('login-pass').value;

  const res = await api('POST', '/api/auth/login', { username, password });
  State.token = res.accessToken;
  State.user = res.user;
  sessionStorage.setItem('ims.token', State.token);

  await startApp();
}

function signOut() {
  State.token = null;
  State.user = null;
  sessionStorage.removeItem('ims.token');
  document.getElementById('app-view').classList.add('hidden');
  document.getElementById('login-view').classList.remove('hidden');
}

// --------------------------------------------------------------- navigation

const TABS = [
  ['dashboard', 'Dashboard', loadDashboard],
  ['master', 'Master data', loadMaster],
  ['partners', 'Partners', loadPartners],
  ['inventory', 'Inventory', loadInventory],
  ['inbound', 'Inbound', loadInbound],
  ['outbound', 'Outbound', loadOutbound],
  ['counting', 'Counting', loadCounting],
  ['transactions', 'Transactions', loadTransactions],
  ['readiness', 'Readiness', loadReadiness]
];

function buildNav() {
  const nav = document.getElementById('nav');
  nav.innerHTML = TABS.map(([id, label]) =>
    `<button data-tab="${id}">${esc(label)}</button>`).join('') +
    `<button data-tab="__swagger">Swagger</button>`;

  nav.addEventListener('click', e => {
    const btn = e.target.closest('button[data-tab]');
    if (!btn) return;
    if (btn.dataset.tab === '__swagger') { window.open('/swagger', '_blank'); return; }
    showTab(btn.dataset.tab);
  });
}

function showTab(id) {
  document.querySelectorAll('nav button').forEach(b =>
    b.classList.toggle('active', b.dataset.tab === id));
  document.querySelectorAll('main section').forEach(s =>
    s.classList.toggle('active', s.id === 'tab-' + id));

  const tab = TABS.find(t => t[0] === id);
  if (tab) guard(tab[2]);
}

// --------------------------------------------------------------- bootstrap

async function startApp() {
  document.getElementById('login-view').classList.add('hidden');
  document.getElementById('app-view').classList.remove('hidden');

  if (!State.user) State.user = await api('GET', '/api/auth/me');
  document.getElementById('who-name').textContent = State.user.fullName || State.user.username;
  document.getElementById('who-role').textContent = State.user.role;

  const warehouses = await api('GET', '/api/warehouses?PageSize=100');
  State.cache.warehouses = warehouses.items;

  const picker = document.getElementById('warehouse-picker');
  picker.innerHTML = warehouses.items
    .map(w => `<option value="${esc(w.id)}">${esc(w.code)} — ${esc(w.name)}</option>`).join('');

  State.warehouseId = State.user.defaultWarehouseId || warehouses.items[0]?.id || null;
  if (State.warehouseId) picker.value = State.warehouseId;

  picker.onchange = () => {
    State.warehouseId = picker.value;
    showTab(document.querySelector('nav button.active')?.dataset.tab || 'dashboard');
  };

  await loadReferenceData();
  showTab('dashboard');
}

const LOCATION_TYPES = ['SmallBin', 'StandardShelf', 'PalletRack', 'FloorStorage',
  'ColdStorage', 'DangerousGoods', 'PickFace', 'ReserveStorage'];

async function loadReferenceData() {
  const [items, locations, uoms, suppliers, customers, statuses, zones, profiles] = await Promise.all([
    api('GET', '/api/items?PageSize=200&isActive=true'),
    api('GET', `/api/locations?warehouseId=${State.warehouseId}&PageSize=200`),
    api('GET', '/api/units-of-measure'),
    api('GET', '/api/suppliers'),
    api('GET', '/api/customers'),
    api('GET', '/api/inventory/statuses'),
    api('GET', `/api/zones?warehouseId=${State.warehouseId}`),
    api('GET', '/api/location-profiles')
  ]);

  State.cache.items = items.items;
  State.cache.locations = locations.items;
  State.cache.uoms = uoms;
  State.cache.suppliers = suppliers;
  State.cache.customers = customers;
  State.cache.statuses = statuses;
  State.cache.zones = zones;
  State.cache.profiles = profiles;

  fill('loc-zone', zones, 'id', z => `${z.code} — ${z.name} (${z.zoneType})`);
  fill('loc-profile', profiles, 'id', p => `${p.code} — ${p.name}`, true);
  document.getElementById('loc-type').innerHTML =
    LOCATION_TYPES.map(t => `<option>${t}</option>`).join('');
  fill('slot-item', items.items, 'id', i => `${i.sku} — ${i.name}`);

  const itemLabel = i => `${i.sku} — ${i.name}`;
  const locLabel = l => `${l.code} (${l.zoneCode})`;

  ['item-uom'].forEach(id => fill(id, uoms, 'id', u => `${u.code} — ${u.name}`));
  ['me-item', 'mv-item', 'ib-item', 'ob-item'].forEach(id => fill(id, items.items, 'id', itemLabel));
  ['me-location', 'mv-from', 'mv-to', 'cp-location'].forEach(id =>
    fill(id, locations.items, 'id', locLabel));
  fill('ib-supplier', suppliers, 'id', s => `${s.code} — ${s.name}`, true);
  fill('ob-customer', customers, 'id', c => `${c.code} — ${c.name}`, true);

  const txnTypes = ['Receipt', 'Putaway', 'Movement', 'Allocation', 'Deallocation',
    'Pick', 'Ship', 'CountAdjustment', 'Damage', 'Return', 'StatusChange'];
  document.getElementById('txn-type').innerHTML =
    '<option value="">All</option>' + txnTypes.map(t => `<option>${t}</option>`).join('');
}

// --------------------------------------------------------------- dashboard

async function loadDashboard() {
  const summary = await api('GET', `/api/inventory/summary?warehouseId=${State.warehouseId}`);
  table('dash-summary', summary, [
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Item', get: r => esc(r.itemName) },
    { head: 'UOM', get: r => esc(r.baseUomCode) },
    { head: 'On hand', cls: 'num', get: r => num(r.totalOnHand) },
    { head: 'Allocated', cls: 'num', get: r => num(r.totalAllocated) },
    { head: 'Hold', cls: 'num', get: r => num(r.totalHold) },
    { head: 'Available', cls: 'num', get: r => num(r.totalAvailable) },
    { head: 'Locations', cls: 'num', get: r => r.locationCount },
    { head: 'Lots', cls: 'num', get: r => r.lotCount }
  ], 'No stock in this warehouse yet.');

  const [putaway, picks, counts, adjustments, txns] = await Promise.all([
    api('GET', `/api/putaway-tasks?WarehouseId=${State.warehouseId}&Status=Created&PageSize=1`),
    api('GET', `/api/pick-tasks?WarehouseId=${State.warehouseId}&PageSize=100`),
    api('GET', `/api/count-tasks?WarehouseId=${State.warehouseId}&PageSize=100`),
    api('GET', `/api/inventory-adjustments?WarehouseId=${State.warehouseId}&Status=Pending&PageSize=1`),
    api('GET', `/api/inventory/transactions?WarehouseId=${State.warehouseId}&PageSize=15`)
  ]);

  const openPicks = picks.items.filter(t =>
    ['Created', 'Assigned', 'InProgress'].includes(t.status)).length;
  const openCounts = counts.items.filter(t =>
    ['Created', 'Assigned', 'InProgress'].includes(t.status)).length;

  document.getElementById('dash-putaway').textContent = putaway.totalCount;
  document.getElementById('dash-pick').textContent = openPicks;
  document.getElementById('dash-count').textContent = openCounts;
  document.getElementById('dash-adj').textContent = adjustments.totalCount;

  renderTxns('dash-txn', txns.items);
}

function renderTxns(target, rows) {
  table(target, rows, [
    { head: 'When', get: r => esc(new Date(r.createdAt).toLocaleString()) },
    { head: 'Type', get: r => pill(r.transactionType) },
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Qty', cls: 'num', get: r => num(r.quantity) },
    { head: 'From', get: r => esc(r.fromLocationCode || '—') },
    { head: 'To', get: r => esc(r.toLocationCode || '—') },
    { head: 'Lot', get: r => esc(r.lotNumber || '—') },
    { head: 'Reference', get: r => esc(r.referenceType) },
    { head: 'By', get: r => esc(r.performedBy || '—') }
  ], 'No stock movements yet.');
}

// --------------------------------------------------------------- master data

async function loadMaster() {
  const items = await api('GET', '/api/items?PageSize=100');
  table('item-list', items.items, [
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Name', get: r => esc(r.name) },
    { head: 'Category', get: r => esc(r.categoryName || '—') },
    { head: 'UOM', get: r => esc(r.baseUomCode) },
    { head: 'Weight', cls: 'num', get: r => r.weight == null ? '—' : num(r.weight, 3) },
    { head: 'Volume', cls: 'num', get: r => r.volume == null ? '—' : num(r.volume, 4) },
    { head: 'L×W×H', get: r => [r.length, r.width, r.height].every(v => v == null) ? '—'
        : `${num(r.length, 2)} × ${num(r.width, 2)} × ${num(r.height, 2)}` },
    { head: 'Tracking', get: r => [
        r.isLotTracked ? pill('Lot') : '',
        r.isSerialTracked ? pill('Serial') : '',
        r.isExpirationTracked ? pill('Expiry') : ''
      ].join(' ') || '<span class="muted">none</span>' },
    { head: 'Active', get: r => r.isActive ? pill('Yes', 'ok') : pill('No', 'bad') }
  ]);

  const locations = await api('GET', `/api/locations?warehouseId=${State.warehouseId}&PageSize=200`);
  table('location-list', locations.items, [
    { head: 'Code', get: r => esc(r.code) },
    { head: 'Zone', get: r => esc(r.zoneCode) + ' ' + pill(r.zoneType) },
    { head: 'Type', get: r => esc(r.locationType) },
    { head: 'Profile', get: r => esc(r.locationProfileCode || '—') },
    { head: 'Pick seq', cls: 'num', get: r => r.pickSequence ?? '—' },
    { head: 'Max weight', cls: 'num', get: r => r.maxWeight == null ? '—' : num(r.maxWeight, 3) },
    { head: 'Max volume', cls: 'num', get: r => r.maxVolume == null ? '—' : num(r.maxVolume, 4) },
    { head: 'X / Y / Z', get: r => [r.coordinateX, r.coordinateY, r.coordinateZ].every(v => v == null)
        ? '—' : `${num(r.coordinateX, 1)} / ${num(r.coordinateY, 1)} / ${num(r.coordinateZ, 1)}` },
    { head: 'On hand', cls: 'num', get: r => num(r.currentOnHandQuantity) },
    { head: 'Items', cls: 'num', get: r => r.distinctItemCount },
    { head: 'Putaway', get: r => r.isPutawayAllowed ? pill('Yes', 'ok') : pill('No') },
    { head: 'Actions', get: r =>
        `<button class="action secondary" data-act="edit-location" data-id="${esc(r.id)}">Edit</button>` }
  ]);
}

async function createItem() {
  const isExpiration = document.getElementById('item-exp').value === 'true';
  const shelf = document.getElementById('item-shelf').value;

  const length = numberOrNull('item-length');
  const width = numberOrNull('item-width');
  const height = numberOrNull('item-height');

  // A volume typed by hand wins; otherwise derive it from the three dimensions.
  let volume = numberOrNull('item-volume');
  if (volume === null && length !== null && width !== null && height !== null) {
    volume = length * width * height;
  }

  await api('POST', '/api/items', {
    sku: document.getElementById('item-sku').value.trim(),
    name: document.getElementById('item-name').value.trim(),
    description: null,
    categoryId: null,
    baseUomId: document.getElementById('item-uom').value,
    weight: numberOrNull('item-weight'),
    length, width, height, volume,
    isLotTracked: document.getElementById('item-lot').value === 'true',
    isSerialTracked: document.getElementById('item-serial').value === 'true',
    isExpirationTracked: isExpiration,
    shelfLifeDays: shelf ? Number(shelf) : null,
    isFragile: false, isHazardous: false, isTemperatureControlled: false,
    minimumStorageTemperature: null, maximumStorageTemperature: null,
    stackableQuantity: null, defaultPutawayZoneId: null, defaultPickZoneId: null
  });

  toast('Item created.');
  ['item-sku', 'item-name', 'item-weight', 'item-length', 'item-width', 'item-height',
    'item-volume', 'item-shelf'].forEach(id => { document.getElementById(id).value = ''; });
  await loadReferenceData();
  await loadMaster();
}

// --------------------------------------------------------------- locations

/* Everything except the code and the warehouse can be edited later, so one form serves
   both. Capacity (max weight / max volume) and the coordinate and distance fields are what
   the putaway planner reads, which is why they are on the form rather than left to the API. */

const LOCATION_FIELDS = {
  'loc-aisle': 'aisle', 'loc-bay': 'bay', 'loc-level': 'level', 'loc-position': 'position',
  'loc-x': 'coordinateX', 'loc-y': 'coordinateY', 'loc-z': 'coordinateZ',
  'loc-maxweight': 'maxWeight', 'loc-maxvolume': 'maxVolume',
  'loc-pickseq': 'pickSequence', 'loc-putseq': 'putawaySequence',
  'loc-dist-recv': 'distanceToReceiving', 'loc-dist-pack': 'distanceToPacking',
  'loc-dist-ship': 'distanceToShipping',
  'loc-access': 'accessibilityScore', 'loc-workers': 'maxConcurrentWorkers'
};

const LOCATION_TEXT_FIELDS = ['loc-aisle', 'loc-bay', 'loc-level', 'loc-position'];

function locationFormBody() {
  const body = {
    zoneId: document.getElementById('loc-zone').value,
    locationType: document.getElementById('loc-type').value,
    locationProfileId: document.getElementById('loc-profile').value || null,
    isPickable: document.getElementById('loc-pickable').value === 'true',
    isPutawayAllowed: document.getElementById('loc-putaway').value === 'true'
  };

  for (const [inputId, field] of Object.entries(LOCATION_FIELDS)) {
    body[field] = LOCATION_TEXT_FIELDS.includes(inputId) ? val(inputId) : numberOrNull(inputId);
  }

  return body;
}

function editLocation(id) {
  const location = State.cache.locations.find(l => l.id === id);
  if (!location) return;

  State.editing.location = id;

  document.getElementById('loc-code').value = location.code;
  document.getElementById('loc-code').disabled = true;
  document.getElementById('loc-zone').value = location.zoneId;
  document.getElementById('loc-type').value = location.locationType;
  document.getElementById('loc-profile').value = location.locationProfileId || '';
  document.getElementById('loc-pickable').value = String(location.isPickable);
  document.getElementById('loc-putaway').value = String(location.isPutawayAllowed);

  for (const [inputId, field] of Object.entries(LOCATION_FIELDS)) {
    document.getElementById(inputId).value = location[field] ?? '';
  }

  const active = document.getElementById('loc-active');
  active.disabled = false;
  active.value = String(location.isActive);

  document.getElementById('loc-form-title').textContent = `Edit location ${location.code}`;
  document.getElementById('btn-save-location').textContent = 'Save changes';
  document.getElementById('btn-cancel-location').classList.remove('hidden');
  document.getElementById('loc-zone').focus();
}

function resetLocationForm() {
  document.getElementById('loc-code').value = '';
  document.getElementById('loc-code').disabled = false;
  Object.keys(LOCATION_FIELDS).forEach(id => { document.getElementById(id).value = ''; });

  document.getElementById('loc-pickable').value = 'true';
  document.getElementById('loc-putaway').value = 'true';

  const active = document.getElementById('loc-active');
  active.value = 'true';
  active.disabled = true;

  State.editing.location = null;
  document.getElementById('loc-form-title').textContent = 'Add location';
  document.getElementById('btn-save-location').textContent = 'Add location';
  document.getElementById('btn-cancel-location').classList.add('hidden');
}

async function saveLocation() {
  const body = locationFormBody();
  const id = State.editing.location;

  if (id) {
    body.isActive = document.getElementById('loc-active').value === 'true';
    await api('PUT', `/api/locations/${id}`, body);
    toast('Location updated.');
  } else {
    body.warehouseId = State.warehouseId;
    body.code = val('loc-code');
    await api('POST', '/api/locations', body);
    toast('Location added.');
  }

  resetLocationForm();
  await loadReferenceData();
  await loadMaster();
}

// --------------------------------------------------------------- partners

async function loadPartners() {
  const [suppliers, customers] = await Promise.all([
    api('GET', '/api/suppliers'),
    api('GET', '/api/customers')
  ]);

  State.cache.suppliers = suppliers;
  State.cache.customers = customers;

  const contactColumns = [
    { head: 'Code', get: r => esc(r.code) },
    { head: 'Name', get: r => esc(r.name) },
    { head: 'Contact', get: r => esc(r.contactName || '—') },
    { head: 'Email', get: r => esc(r.email || '—') },
    { head: 'Phone', get: r => esc(r.phone || '—') }
  ];

  table('supplier-list', suppliers, [
    ...contactColumns,
    { head: 'Address', get: r => esc(r.address || '—') },
    { head: 'Active', get: r => r.isActive ? pill('Yes', 'ok') : pill('No', 'bad') },
    { head: 'Actions', get: r =>
        `<button class="action secondary" data-act="edit-supplier" data-id="${esc(r.id)}">Edit</button>` }
  ], 'No suppliers yet. Add one above.');

  table('customer-list', customers, [
    ...contactColumns,
    { head: 'Ships to', get: r => esc(r.shippingAddress || '—') },
    { head: 'Active', get: r => r.isActive ? pill('Yes', 'ok') : pill('No', 'bad') },
    { head: 'Actions', get: r =>
        `<button class="action secondary" data-act="edit-customer" data-id="${esc(r.id)}">Edit</button>` }
  ], 'No customers yet. Add one above.');
}

/* Both forms double as the edit form: picking Edit on a row loads it, and Cancel puts the
   form back into add mode. The code is fixed once a partner exists, so it is locked while
   editing — everything else, including whether the partner is still active, can change. */

function loadPartnerForm(prefix, partner, addressField, label) {
  document.getElementById(`${prefix}-code`).value = partner.code;
  document.getElementById(`${prefix}-code`).disabled = true;
  document.getElementById(`${prefix}-name`).value = partner.name;
  document.getElementById(`${prefix}-contact`).value = partner.contactName || '';
  document.getElementById(`${prefix}-email`).value = partner.email || '';
  document.getElementById(`${prefix}-phone`).value = partner.phone || '';
  document.getElementById(`${prefix}-address`).value = partner[addressField] || '';

  const active = document.getElementById(`${prefix}-active`);
  active.disabled = false;
  active.value = String(partner.isActive);

  document.getElementById(`${prefix}-form-title`).textContent = `Edit ${label} ${partner.code}`;
  document.getElementById(`btn-save-${label}`).textContent = 'Save changes';
  document.getElementById(`btn-cancel-${label}`).classList.remove('hidden');
  document.getElementById(`${prefix}-name`).focus();
}

function resetPartnerForm(prefix, label) {
  ['code', 'name', 'contact', 'email', 'phone', 'address']
    .forEach(f => { document.getElementById(`${prefix}-${f}`).value = ''; });

  document.getElementById(`${prefix}-code`).disabled = false;

  const active = document.getElementById(`${prefix}-active`);
  active.value = 'true';
  active.disabled = true;

  State.editing[label] = null;
  document.getElementById(`${prefix}-form-title`).textContent = `Add ${label}`;
  document.getElementById(`btn-save-${label}`).textContent = `Add ${label}`;
  document.getElementById(`btn-cancel-${label}`).classList.add('hidden');
}

async function savePartner(prefix, label, path, addressField) {
  const body = {
    name: val(`${prefix}-name`),
    contactName: val(`${prefix}-contact`),
    email: val(`${prefix}-email`),
    phone: val(`${prefix}-phone`),
    [addressField]: val(`${prefix}-address`)
  };

  const id = State.editing[label];
  if (id) {
    body.isActive = document.getElementById(`${prefix}-active`).value === 'true';
    await api('PUT', `${path}/${id}`, body);
    toast(`Updated ${label}.`);
  } else {
    body.code = val(`${prefix}-code`);
    await api('POST', path, body);
    toast(`Added ${label}.`);
  }

  resetPartnerForm(prefix, label);
  await loadPartners();
  // Keeps the Inbound and Outbound pickers in step with what was just changed.
  await loadReferenceData();
}

// --------------------------------------------------------------- inventory

async function loadInventory() {
  const balances = await api('GET',
    `/api/inventory?WarehouseId=${State.warehouseId}&PageSize=200`);

  table('inv-list', balances.items, [
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Location', get: r => esc(r.locationCode) },
    { head: 'Status', get: r => pill(r.statusCode, r.isAllocatable ? 'ok' : 'warn') },
    { head: 'Lot', get: r => esc(r.lotNumber || '—') },
    { head: 'Expires', get: r => r.expirationDate ? esc(r.expirationDate.slice(0, 10)) : '—' },
    { head: 'Serial', get: r => esc(r.serialNumber || '—') },
    { head: 'LPN', get: r => esc(r.licensePlateCode || '—') },
    { head: 'On hand', cls: 'num', get: r => num(r.onHandQuantity) },
    { head: 'Allocated', cls: 'num', get: r => num(r.allocatedQuantity) },
    { head: 'Hold', cls: 'num', get: r => num(r.holdQuantity) },
    { head: 'Available', cls: 'num', get: r => num(r.availableQuantity) }
  ], 'No stock yet. Book some with manual entry above.');
}

async function manualEntry() {
  const lot = document.getElementById('me-lot').value.trim();
  const exp = document.getElementById('me-exp').value;

  await api('POST', '/api/inventory/manual-entry', {
    warehouseId: State.warehouseId,
    locationId: document.getElementById('me-location').value,
    itemId: document.getElementById('me-item').value,
    quantity: Number(document.getElementById('me-qty').value),
    uomId: null,
    inventoryStatusId: null,
    lotNumber: lot || null,
    manufactureDate: null,
    expirationDate: exp ? new Date(exp).toISOString() : null,
    supplierLotNumber: null,
    serialNumber: null,
    licensePlateId: null,
    notes: 'Booked from the demo UI'
  });

  toast('Stock booked.');
  await loadInventory();
}

async function moveStock() {
  const available = State.cache.statuses.find(s => s.code === 'AVAILABLE');

  await api('POST', '/api/inventory/movements', {
    warehouseId: State.warehouseId,
    itemId: document.getElementById('mv-item').value,
    fromLocationId: document.getElementById('mv-from').value,
    toLocationId: document.getElementById('mv-to').value,
    quantity: Number(document.getElementById('mv-qty').value),
    inventoryStatusId: available.id,
    lotId: null, serialId: null, licensePlateId: null,
    notes: 'Moved from the demo UI'
  });

  toast('Stock moved.');
  await loadInventory();
}

// --------------------------------------------------------------- inbound

async function loadInbound() {
  const orders = await api('GET',
    `/api/inbound-orders?WarehouseId=${State.warehouseId}&PageSize=50`);

  table('inbound-list', orders.items, [
    { head: 'Order', get: r => esc(r.orderNumber) },
    { head: 'Supplier', get: r => esc(r.supplierName || '—') },
    { head: 'Status', get: r => pill(r.status, r.status === 'Completed' ? 'ok' : '') },
    { head: 'Lines', cls: 'num', get: r => r.lineCount },
    { head: 'Expected', cls: 'num', get: r => num(r.expectedQuantity) },
    { head: 'Received', cls: 'num', get: r => num(r.receivedQuantity) },
    { head: 'Actions', get: r => inboundActions(r) }
  ], 'No inbound orders yet.');

  const tasks = await api('GET',
    `/api/putaway-tasks?WarehouseId=${State.warehouseId}&PageSize=50`);

  table('putaway-list', tasks.items, [
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'From', get: r => esc(r.fromLocationCode) },
    { head: 'Qty', cls: 'num', get: r => num(r.quantity) },
    { head: 'Lot', get: r => esc(r.lotNumber || '—') },
    { head: 'Status', get: r => pill(r.status, r.status === 'Completed' ? 'ok' : '') },
    { head: 'Put to', get: r => esc(r.actualLocationCode || '—') },
    { head: 'Actions', get: r => r.status === 'Completed' ? '' :
        `<select data-loc-for="${esc(r.id)}">` +
        State.cache.locations.filter(l => l.isPutawayAllowed)
          .map(l => `<option value="${esc(l.id)}">${esc(l.code)}</option>`).join('') +
        `</select> <button class="action" data-act="complete-putaway" data-id="${esc(r.id)}">Put away</button>` }
  ], 'No putaway tasks.');
}

function inboundActions(r) {
  const id = esc(r.id);
  if (r.status === 'Draft') return `<button class="action" data-act="confirm-inbound" data-id="${id}">Confirm</button>`;
  if (r.status === 'Expected' || r.status === 'PartiallyReceived')
    return `<button class="action" data-act="receive" data-id="${id}">Receive all</button>`;
  if (r.status === 'Received')
    return `<button class="action" data-act="create-putaway" data-id="${id}">Create putaway</button>` +
           ` <button class="action secondary" data-act="complete-inbound" data-id="${id}">Complete</button>`;
  return '';
}

async function createInbound() {
  const itemId = document.getElementById('ib-item').value;
  const item = State.cache.items.find(i => i.id === itemId);
  const uom = State.cache.uoms.find(u => u.code === item.baseUomCode);

  await api('POST', '/api/inbound-orders', {
    warehouseId: State.warehouseId,
    orderNumber: null,
    supplierId: document.getElementById('ib-supplier').value || null,
    expectedArrivalDate: new Date().toISOString(),
    notes: 'Created from the demo UI',
    lines: [{
      itemId,
      expectedQuantity: Number(document.getElementById('ib-qty').value),
      uomId: uom.id,
      expectedLotNumber: val('ib-lot'),
      expectedExpirationDate: expiryOrNull('ib-exp')
    }]
  });

  toast('Inbound order created in Draft.');
  ['ib-qty', 'ib-lot', 'ib-exp'].forEach(id => { document.getElementById(id).value = ''; });
  await loadInbound();
}

/** Receives every outstanding line into the first Receiving-zone location. */
async function receiveAll(orderId) {
  const order = await api('GET', `/api/inbound-orders/${orderId}`);
  const receiving = State.cache.locations.find(l => l.zoneType === 'Receiving');
  if (!receiving) throw new Error('This warehouse has no location in a Receiving zone.');

  const lines = order.details
    .filter(d => d.outstandingQuantity > 0)
    .map(d => {
      const item = State.cache.items.find(i => i.id === d.itemId);
      const needsLot = item?.isLotTracked;
      const needsExpiry = item?.isExpirationTracked;
      return {
        inboundOrderDetailId: d.id,
        receivedQuantity: d.outstandingQuantity,
        receivedUomId: null,
        // A lot-tracked item cannot be received without a lot (rule §11.6 needs the
        // expiration, which lives on the lot), so the demo generates one.
        lotNumber: needsLot ? (d.expectedLotNumber || 'LOT-' + Date.now()) : null,
        manufactureDate: null,
        expirationDate: needsExpiry && !d.expectedExpirationDate
          ? new Date(Date.now() + 90 * 86400000).toISOString()
          : d.expectedExpirationDate,
        supplierLotNumber: null,
        serialNumber: null,
        licensePlateId: null,
        inventoryStatusId: null
      };
    });

  if (lines.length === 0) throw new Error('Every line on this order is already received.');

  await api('POST', `/api/inbound-orders/${orderId}/receive`, {
    receivingLocationId: receiving.id,
    notes: 'Received from the demo UI',
    lines
  });

  toast('Received into ' + receiving.code + '.');
  await loadInbound();
}

async function createPutaway(orderId) {
  const order = await api('GET', `/api/inbound-orders/${orderId}`);
  const receipt = order.receipts[order.receipts.length - 1];
  if (!receipt) throw new Error('This order has no receipt yet.');

  const tasks = await api('POST', `/api/receipts/${receipt.id}/create-putaway`, null);
  toast(`${tasks.length} putaway task(s) created.`);
  await loadInbound();
}

// --------------------------------------------------------------- outbound

async function loadOutbound() {
  const orders = await api('GET', `/api/orders?WarehouseId=${State.warehouseId}&PageSize=50`);

  table('order-list', orders.items, [
    { head: 'Order', get: r => esc(r.orderNumber) },
    { head: 'Customer', get: r => esc(r.customerName || '—') },
    { head: 'Type', get: r => esc(r.orderType) },
    { head: 'Status', get: r => pill(r.status, r.status === 'Shipped' ? 'ok' : '') },
    { head: 'Lines', cls: 'num', get: r => r.totalLineCount },
    { head: 'Qty', cls: 'num', get: r => num(r.totalQuantity) },
    { head: 'Actions', get: r => outboundActions(r) }
  ], 'No orders yet.');

  const picks = await api('GET', `/api/pick-tasks?WarehouseId=${State.warehouseId}&PageSize=50`);
  table('pick-list', picks.items, [
    { head: 'Seq', cls: 'num', get: r => r.sequenceNumber },
    { head: 'Order', get: r => esc(r.orderNumber) },
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'From', get: r => esc(r.fromLocationCode) },
    { head: 'To', get: r => esc(r.destinationLocationCode || '—') },
    { head: 'Qty', cls: 'num', get: r => num(r.quantity) },
    { head: 'Picked', cls: 'num', get: r => num(r.pickedQuantity) },
    { head: 'Status', get: r => pill(r.status,
        r.status === 'Completed' ? 'ok' : r.status === 'ShortPicked' ? 'warn' : '') },
    { head: 'Actions', get: r => ['Completed', 'ShortPicked', 'Cancelled'].includes(r.status) ? '' :
        `<input type="number" step="0.0001" placeholder="${num(r.quantity)}" data-qty-for="${esc(r.id)}" style="width:80px">` +
        ` <button class="action" data-act="complete-pick" data-id="${esc(r.id)}">Pick</button>` }
  ], 'No pick tasks.');
}

function outboundActions(r) {
  const id = esc(r.id);
  switch (r.status) {
    case 'Draft': return `<button class="action" data-act="confirm-order" data-id="${id}">Confirm</button>`;
    case 'Created': return `<button class="action" data-act="release" data-id="${id}">Release</button>`;
    case 'Released':
    case 'PartiallyAllocated': return `<button class="action" data-act="allocate" data-id="${id}">Allocate</button>`;
    case 'Allocated': return `<button class="action" data-act="create-picks" data-id="${id}">Create pick tasks</button>`;
    case 'Picking': return `<span class="muted">Complete the pick tasks below</span>`;
    case 'Picked':
    case 'Packed': return `<button class="action" data-act="ship" data-id="${id}">Ship</button>`;
    default: return '';
  }
}

async function createOrder() {
  const itemId = document.getElementById('ob-item').value;
  const item = State.cache.items.find(i => i.id === itemId);
  const uom = State.cache.uoms.find(u => u.code === item.baseUomCode);

  await api('POST', '/api/orders', {
    warehouseId: State.warehouseId,
    orderNumber: null,
    customerId: document.getElementById('ob-customer').value || null,
    orderDate: new Date().toISOString(),
    requiredShipDate: new Date(Date.now() + 3 * 86400000).toISOString(),
    carrier: 'Demo Carrier',
    serviceLevel: 'Standard',
    priority: 100,
    orderType: 'Standard',
    notes: 'Created from the demo UI',
    lines: [{
      itemId,
      orderedQuantity: Number(document.getElementById('ob-qty').value),
      uomId: uom.id,
      requiredLotNumber: null,
      requiredSerialNumber: null,
      minimumShelfLifeDays: null
    }]
  });

  toast('Order created in Draft.');
  await loadOutbound();
}

async function allocateOrder(orderId) {
  const res = await api('POST', `/api/orders/${orderId}/allocate`, null);

  if (res.shortfalls.length) {
    // Acceptance scenario 3: a partial allocation is reported, not silently accepted.
    toast('Partially allocated. Short:\n' + res.shortfalls
      .map(s => `${s.sku}: short ${s.shortQuantity} of ${s.requestedQuantity}`).join('\n'), true);
  } else {
    toast(`Fully allocated across ${res.allocations.length} stock record(s).`);
  }

  await loadOutbound();
}

async function createPicks(orderId) {
  const packing = State.cache.locations.find(l => l.zoneType === 'Packing')
    || State.cache.locations.find(l => l.zoneType === 'ShippingStaging');

  const tasks = await api('POST', `/api/orders/${orderId}/create-pick-tasks`, {
    destinationLocationId: packing ? packing.id : null,
    assignTo: State.user.username
  });

  toast(`${tasks.length} pick task(s) created${packing ? ' → ' + packing.code : ''}.`);
  await loadOutbound();
}

// --------------------------------------------------------------- counting

async function loadCounting() {
  const tasks = await api('GET', `/api/count-tasks?WarehouseId=${State.warehouseId}&PageSize=50`);

  table('count-list', tasks.items, [
    { head: 'Location', get: r => esc(r.locationCode) },
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Lot', get: r => esc(r.lotNumber || '—') },
    { head: 'Status', get: r => pill(r.status,
        r.status === 'Completed' ? 'ok' : r.status === 'VarianceFound' ? 'warn' : '') },
    // System quantity is deliberately hidden until the count is submitted: a count
    // anchored to the expected number is not evidence.
    { head: 'System', cls: 'num', get: r => r.countedQuantity === null || r.countedQuantity === undefined
        ? '<span class="muted">hidden</span>' : num(r.systemQuantity) },
    { head: 'Counted', cls: 'num', get: r => r.countedQuantity === null || r.countedQuantity === undefined
        ? '—' : num(r.countedQuantity) },
    { head: 'Variance', cls: 'num', get: r => r.variance === null || r.variance === undefined ? '—'
        : (r.variance === 0 ? pill('0', 'ok') : pill(num(r.variance), 'warn')) },
    { head: 'Counted by', get: r => esc(r.countedBy || '—') },
    { head: 'Actions', get: r => ['Created', 'Assigned', 'InProgress'].includes(r.status)
        ? `<input type="number" step="0.0001" data-count-for="${esc(r.id)}" style="width:90px" placeholder="count">` +
          ` <button class="action" data-act="submit-count" data-id="${esc(r.id)}">Submit</button>`
        : '' }
  ], 'No count tasks. Create a plan above.');

  const adjustments = await api('GET',
    `/api/inventory-adjustments?WarehouseId=${State.warehouseId}&PageSize=50`);

  table('adj-list', adjustments.items, [
    { head: 'Number', get: r => esc(r.adjustmentNumber) },
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Location', get: r => esc(r.locationCode) },
    { head: 'Reason', get: r => esc(r.reason) },
    { head: 'System', cls: 'num', get: r => num(r.systemQuantity) },
    { head: 'Counted', cls: 'num', get: r => num(r.countedQuantity) },
    { head: 'Before', cls: 'num', get: r => r.quantityBeforeApproval == null ? '—' : num(r.quantityBeforeApproval) },
    { head: 'After', cls: 'num', get: r => r.quantityAfterApproval == null ? '—' : num(r.quantityAfterApproval) },
    { head: 'Status', get: r => pill(r.status,
        r.status === 'Approved' ? 'ok' : r.status === 'Rejected' ? 'bad' : 'warn') },
    { head: 'Raised by', get: r => esc(r.requestedBy || '—') },
    { head: 'Decided by', get: r => esc(r.approvedBy || '—') },
    { head: 'Actions', get: r => r.status !== 'Pending' ? '' :
        `<button class="action" data-act="approve-adj" data-id="${esc(r.id)}">Approve</button>` +
        ` <button class="action danger" data-act="reject-adj" data-id="${esc(r.id)}">Reject</button>` }
  ], 'No adjustments.');
}

async function createCountPlan() {
  const plan = await api('POST', '/api/count-plans', {
    warehouseId: State.warehouseId,
    planNumber: null,
    name: document.getElementById('cp-name').value.trim() || 'Cycle count',
    countType: document.getElementById('cp-type').value,
    selectionMode: 'ByLocation',
    zoneId: null,
    itemId: null,
    locationIds: [document.getElementById('cp-location').value],
    scheduledDate: new Date().toISOString(),
    blockAllocationDuringCount: false,
    notes: 'Created from the demo UI'
  });

  const released = await api('POST', `/api/count-plans/${plan.id}/release`,
    [document.getElementById('cp-location').value]);

  toast(`Plan ${released.planNumber} released with ${released.taskCount} task(s).`);
  await loadCounting();
}

// --------------------------------------------------------------- transactions

async function loadTransactions() {
  const type = document.getElementById('txn-type').value;
  const res = await api('GET',
    `/api/inventory/transactions?WarehouseId=${State.warehouseId}&PageSize=100` +
    (type ? `&TransactionType=${type}` : ''));

  renderTxns('txn-list', res.items);
}

// --------------------------------------------------------------- putaway planner

async function planPutaway() {
  const quantity = numberOrNull('slot-qty');
  if (quantity === null || quantity <= 0) throw new Error('Enter how many units to put away.');

  const itemId = document.getElementById('slot-item').value;
  if (!itemId) throw new Error('Pick an item first.');

  const plan = await api('GET', '/api/algorithms/putaway-plan' +
    `?warehouseId=${State.warehouseId}&itemId=${itemId}&quantity=${quantity}`);

  const velocity = plan.isFastMover
    ? pill('fast mover', 'ok')
    : pill('slow mover');

  const placement = plan.unplannedQuantity > 0
    ? pill(`${num(plan.unplannedQuantity)} unplaced`, 'warn')
    : pill('fully placed', 'ok');

  document.getElementById('slot-summary').innerHTML =
    `<p><strong>${esc(plan.sku)}</strong> — ${esc(plan.itemName)} · ` +
    `${num(plan.plannedQuantity)} of ${num(plan.requestedQuantity)} placed ${placement} · ` +
    `${velocity} at ${num(plan.linesPerDay, 3)} lines/day · ` +
    `unit weight ${plan.unitWeight == null ? '—' : num(plan.unitWeight, 3)}, ` +
    `unit volume ${plan.unitVolume == null ? '—' : num(plan.unitVolume, 4)}</p>` +
    plan.notes.map(n => `<p class="muted">${esc(n)}</p>`).join('');

  table('slot-list', plan.lines, [
    { head: 'Put here', cls: 'num', get: r => `<strong>${num(r.quantity)}</strong>` },
    { head: 'Location', get: r => esc(r.locationCode) },
    { head: 'Zone', get: r => esc(r.zoneCode) + ' ' + pill(r.zoneType) },
    { head: 'Type', get: r => esc(r.locationType) },
    { head: 'Capacity', cls: 'num', get: r => r.unitsThatFit == null
        ? '<span class="muted">no limit set</span>' : num(r.unitsThatFit) },
    { head: 'Weight left', cls: 'num', get: r => r.remainingWeight == null ? '—' : num(r.remainingWeight, 3) },
    { head: 'Volume left', cls: 'num', get: r => r.remainingVolume == null ? '—' : num(r.remainingVolume, 4) },
    { head: 'To packing', cls: 'num', get: r => r.distanceToPacking == null ? '—' : num(r.distanceToPacking, 1) },
    { head: 'Score', cls: 'num', get: r => num(r.score, 2) },
    { head: 'Why', get: r => `<span class="muted">${esc(r.reason)}</span>` }
  ], 'No eligible location could take any of it — see the notes above.');
}

// --------------------------------------------------------------- readiness

async function loadReadiness() {
  const rd = await api('GET', `/api/algorithms/readiness?warehouseId=${State.warehouseId}`);

  table('readiness-list', rd.checks, [
    { head: 'Area', get: r => esc(r.category) },
    { head: 'Data point', get: r => esc(r.dataPoint) },
    { head: 'Records', cls: 'num', get: r => r.totalRecords },
    { head: 'Populated', cls: 'num', get: r => r.populatedRecords },
    { head: 'Ready', get: r => r.isReady ? pill('Yes', 'ok') : pill('No data', 'warn') }
  ]);

  const demand = await api('GET', `/api/algorithms/demand-stats?warehouseId=${State.warehouseId}`);
  table('demand-list', demand, [
    { head: 'SKU', get: r => esc(r.sku) },
    { head: 'Item', get: r => esc(r.itemName) },
    { head: 'Order lines', cls: 'num', get: r => r.orderLineCount },
    { head: 'Orders', cls: 'num', get: r => r.distinctOrderCount },
    { head: 'Shipped qty', cls: 'num', get: r => num(r.totalShippedQuantity) },
    { head: 'Avg line qty', cls: 'num', get: r => num(r.averageLineQuantity) },
    { head: 'Lines/day', cls: 'num', get: r => num(r.linesPerDay, 3) }
  ], 'No shipped history yet — ship an order to populate this.');
}

// --------------------------------------------------------------- wiring

document.getElementById('btn-login').onclick = () => guard(signIn);
document.getElementById('login-pass').addEventListener('keydown', e => {
  if (e.key === 'Enter') guard(signIn);
});
document.getElementById('btn-logout').onclick = signOut;

document.getElementById('btn-create-item').onclick = () => guard(createItem);

document.getElementById('btn-save-location').onclick = () => guard(saveLocation);
document.getElementById('btn-cancel-location').onclick = () => resetLocationForm();

onAction('location-list', (act, id) => {
  if (act === 'edit-location') editLocation(id);
});

document.getElementById('btn-save-supplier').onclick =
  () => guard(() => savePartner('sup', 'supplier', '/api/suppliers', 'address'));
document.getElementById('btn-cancel-supplier').onclick =
  () => resetPartnerForm('sup', 'supplier');
document.getElementById('btn-save-customer').onclick =
  () => guard(() => savePartner('cus', 'customer', '/api/customers', 'shippingAddress'));
document.getElementById('btn-cancel-customer').onclick =
  () => resetPartnerForm('cus', 'customer');

onAction('supplier-list', (act, id) => {
  if (act !== 'edit-supplier') return;
  const supplier = State.cache.suppliers.find(s => s.id === id);
  if (!supplier) return;
  State.editing.supplier = id;
  loadPartnerForm('sup', supplier, 'address', 'supplier');
});

onAction('customer-list', (act, id) => {
  if (act !== 'edit-customer') return;
  const customer = State.cache.customers.find(c => c.id === id);
  if (!customer) return;
  State.editing.customer = id;
  loadPartnerForm('cus', customer, 'shippingAddress', 'customer');
});
document.getElementById('btn-manual-entry').onclick = () => guard(manualEntry);
document.getElementById('btn-move').onclick = () => guard(moveStock);
document.getElementById('btn-create-inbound').onclick = () => guard(createInbound);
document.getElementById('btn-create-order').onclick = () => guard(createOrder);
document.getElementById('btn-create-plan').onclick = () => guard(createCountPlan);
document.getElementById('btn-refresh-txn').onclick = () => guard(loadTransactions);
document.getElementById('btn-plan-putaway').onclick = () => guard(planPutaway);

onAction('inbound-list', async (act, id) => {
  if (act === 'confirm-inbound') { await api('POST', `/api/inbound-orders/${id}/confirm`); toast('Confirmed.'); await loadInbound(); }
  if (act === 'receive') await receiveAll(id);
  if (act === 'create-putaway') await createPutaway(id);
  if (act === 'complete-inbound') { await api('POST', `/api/inbound-orders/${id}/complete`); toast('Order completed.'); await loadInbound(); }
});

onAction('putaway-list', async (act, id) => {
  if (act !== 'complete-putaway') return;
  const select = document.querySelector(`[data-loc-for="${id}"]`);
  await api('POST', `/api/putaway-tasks/${id}/complete`, {
    actualLocationId: select.value, quantity: null, notes: 'Put away from the demo UI'
  });
  toast('Put away.');
  await loadInbound();
});

onAction('order-list', async (act, id) => {
  if (act === 'confirm-order') { await api('POST', `/api/orders/${id}/confirm`); toast('Confirmed.'); await loadOutbound(); }
  if (act === 'release') { await api('POST', `/api/orders/${id}/release`); toast('Released.'); await loadOutbound(); }
  if (act === 'allocate') await allocateOrder(id);
  if (act === 'create-picks') await createPicks(id);
  if (act === 'ship') {
    const shipment = await api('POST', `/api/orders/${id}/ship`, {
      carrier: 'Demo Carrier', serviceLevel: 'Standard',
      trackingNumber: 'TRK-' + Date.now(), notes: null
    });
    toast(`Shipped as ${shipment.shipmentNumber}.`);
    await loadOutbound();
  }
});

onAction('pick-list', async (act, id) => {
  if (act !== 'complete-pick') return;
  const input = document.querySelector(`[data-qty-for="${id}"]`);
  await api('POST', `/api/pick-tasks/${id}/complete`, {
    pickedQuantity: input.value ? Number(input.value) : null,
    notes: 'Picked from the demo UI'
  });
  toast('Pick recorded.');
  await loadOutbound();
});

onAction('count-list', async (act, id) => {
  if (act !== 'submit-count') return;
  const input = document.querySelector(`[data-count-for="${id}"]`);
  if (input.value === '') throw new Error('Enter the counted quantity.');
  const res = await api('POST', `/api/count-tasks/${id}/complete`, {
    countedQuantity: Number(input.value), notes: 'Counted from the demo UI'
  });
  toast(res.hasVariance
    ? `Variance of ${res.variance}. An adjustment was raised for approval.`
    : 'Count matches. Task closed.');
  await loadCounting();
});

onAction('adj-list', async (act, id) => {
  if (act === 'approve-adj') {
    const res = await api('POST', `/api/inventory-adjustments/${id}/approve`, { notes: null });
    toast(`Approved. Stock ${res.quantityBeforeApproval} → ${res.quantityAfterApproval}.`);
  }
  if (act === 'reject-adj') {
    const reason = prompt('Reason for rejection:');
    if (!reason) return;
    await api('POST', `/api/inventory-adjustments/${id}/reject`, { rejectionReason: reason });
    toast('Rejected. No stock changed.');
  }
  await loadCounting();
});

buildNav();

// Resume an existing session if the token is still valid.
if (State.token) {
  startApp().catch(() => signOut());
}
