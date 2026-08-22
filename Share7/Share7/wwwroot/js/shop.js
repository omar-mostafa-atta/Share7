// ===========================================================================
// Share7 Admin Console — Shop page logic
// Product kinds, products, and grants management.
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, slugify, toWire, textFor, missingLanguages } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

let kinds = [];
let products = [];
let grantsCache = [];
let selectedProduct = null;
let editingKindId = null;
let editingProductId = null;
let editingGrantId = null;
let kindModalInstance = null;
let productModalInstance = null;
let grantModalInstance = null;

function getLangId() { return state.selectedLangId; }

// ---------------------------------------------------------------------------
// Product kinds
// ---------------------------------------------------------------------------
async function loadProductKinds() {
  const data = await api('GET', '/api/admin/product-kinds');
  kinds = data.productKinds || [];

  document.getElementById('kindList').innerHTML = kinds.length
    ? `<table class="table table-sm table-hover align-middle mb-0">
         <thead><tr><th>Label</th><th>Token</th><th class="text-center">Used</th><th></th></tr></thead>
         <tbody>${kinds.map(k => `<tr>
           <td>${escapeHtml(textFor(k.translations, getLangId()) || k.name)}${textFor(k.translations, getLangId(), 'description')
                ? `<div class="muted-sm text-truncate" style="max-width:15rem">${escapeHtml(textFor(k.translations, getLangId(), 'description'))}</div>` : ''}</td>
           <td><span class="kind-token">${escapeHtml(k.kind)}</span></td>
           <td class="text-center">${k.productCount}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-secondary" title="Edit"
                     onclick="openKindModal('${k.productKindId}')"><i class="bi bi-pencil"></i></button>
             <button class="btn btn-sm btn-outline-danger" title="Delete"
                     onclick="deleteKind('${k.productKindId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table>`
    : '<div class="empty"><i class="bi bi-tags"></i>No kinds yet. Add one before creating a product.</div>';
}

function previewKind() {
  const wire = toWire(document.getElementById('kindName').value);
  document.getElementById('kindPreview').textContent = wire || '—';
}

function openKindModal(kindId) {
  editingKindId = kindId || null;
  const kind = kindId ? kinds.find(k => k.productKindId === kindId) : null;

  document.getElementById('kindTitle').textContent = kind ? `Edit "${kind.name}"` : 'Add product kind';
  document.getElementById('kindName').value = kind ? kind.name : '';
  translationFields('kindTranslations', kind ? kind.translations : []);
  previewKind();

  kindModalInstance.show();
}

async function submitKind() {
  const name = document.getElementById('kindName').value.trim();
  const translations = collectTranslations('kindTranslations');
  const missing = missingLanguages(translations);

  if (!name) { toast('Machine name required', 'It is what Unity reads as the grant kind.'); return; }
  if (missing.length) { toast('A label is required in every language', `Missing: ${missing.join(', ')}.`); return; }

  const body = { name, translations };

  try {
    if (editingKindId) {
      if (!confirm(`Save as "${name}"? Every product of this kind reports ${toWire(name)} to Unity.`)) return;
      await api('PUT', `/api/admin/product-kinds/${editingKindId}`, body);
    } else {
      await api('POST', '/api/admin/product-kinds', body);
    }
  } catch { return; }

  kindModalInstance.hide();
  await loadShop();
}

async function deleteKind(kindId) {
  const kind = kinds.find(k => k.productKindId === kindId);
  if (!confirm(`Delete kind "${kind.name}"?`)) return;

  try {
    await api('DELETE', `/api/admin/product-kinds/${kindId}`);
  } catch (e) {
    const count = e.payload && e.payload.details ? e.payload.details.productCount : null;
    if (count != null) toast('Kind still in use', `${count} product(s) use it. Re-categorise them first.`);
    return;
  }

  await loadShop();
}

// ---------------------------------------------------------------------------
// Products
// ---------------------------------------------------------------------------
async function loadProducts() {
  const data = await api('GET', '/api/admin/products');
  products = data.products || [];

  document.getElementById('productList').innerHTML = products.length
    ? `<div class="table-responsive"><table class="table table-sm align-middle mb-0">
         <thead><tr><th></th><th>Key</th><th>Name</th><th>Kind</th><th class="text-center">Grants</th>
                    <th class="text-center">Owners</th><th class="text-center">State</th><th></th></tr></thead>
         <tbody>${products.map(p => `<tr class="row-pick ${selectedProduct && selectedProduct.productId === p.productId ? 'picked' : ''}"
                                        data-id="${p.productId}">
           <td>${p.imageUrl
                ? `<img class="thumb" src="${escapeHtml(p.imageUrl)}" alt=""
                        onerror="this.replaceWith(Object.assign(document.createElement('span'),
                                 {className:'thumb thumb-empty',innerHTML:'<i class=\\'bi bi-image\\'></i>'}))" />`
                : '<span class="thumb thumb-empty"><i class="bi bi-image"></i></span>'}</td>
           <td class="mono">${escapeHtml(p.key)}</td>
           <td>${escapeHtml(textFor(p.translations, getLangId()) || '—')}</td>
           <td><span class="kind-token">${escapeHtml(p.kind)}</span></td>
           <td class="text-center">${p.grants.length}</td>
           <td class="text-center">${p.ownerCount
                ? `<span class="badge text-bg-primary" title="grants frozen, cannot be deleted">${p.ownerCount}</span>`
                : '<span class="muted-sm">0</span>'}</td>
           <td class="text-center">${p.active
                ? '<span class="badge text-bg-success">active</span>'
                : '<span class="badge text-bg-secondary">retired</span>'}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-secondary" title="Edit"
                     onclick="event.stopPropagation(); openProductModal('${p.productId}')"><i class="bi bi-pencil"></i></button>
             <button class="btn btn-sm btn-outline-danger" title="Delete"
                     onclick="event.stopPropagation(); deleteProduct('${p.productId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table></div>`
    : '<div class="empty"><i class="bi bi-box-seam"></i>No products yet.</div>';

  document.querySelectorAll('#productList tr.row-pick').forEach(row => {
    row.onclick = () => selectProduct(products.find(p => p.productId === row.dataset.id));
  });

  if (selectedProduct && !products.some(p => p.productId === selectedProduct.productId)) {
    selectedProduct = null;
  }
  renderGrantsHeader();
}

async function selectProduct(product) {
  selectedProduct = product;
  document.querySelectorAll('#productList tr.row-pick').forEach(row =>
    row.classList.toggle('picked', row.dataset.id === product.productId));
  renderGrantsHeader();
  await loadGrants();
}

function renderGrantsHeader() {
  document.getElementById('grantsFor').textContent = selectedProduct
    ? `— ${selectedProduct.key}`
    : '— pick a product above';

  if (!selectedProduct) {
    document.getElementById('grantList').innerHTML =
      '<div class="empty"><i class="bi bi-hand-index"></i>Pick a product to see what it hands over.</div>';
  }
}

function normaliseKeyInput() {
  const input = document.getElementById('prodKey');
  const cleaned = slugify(input.value);
  if (cleaned !== input.value) input.value = cleaned;
}

function openProductModal(productId) {
  if (!kinds.length) { alert('Create a product kind first — a product cannot exist without one.'); return; }

  editingProductId = productId || null;
  const product = productId ? products.find(p => p.productId === productId) : null;

  document.getElementById('productTitle').textContent = product ? `Edit "${product.key}"` : 'Add product';
  document.getElementById('prodKey').value = product ? product.key : '';
  document.getElementById('prodKey').disabled = !!product;
  document.getElementById('prodImage').value = product && product.imageUrl ? product.imageUrl : '';
  document.getElementById('prodActive').checked = product ? product.active : true;
  document.getElementById('prodKind').innerHTML = kinds.map(k =>
    `<option value="${k.productKindId}" ${product && product.productKindId === k.productKindId ? 'selected' : ''}>
       ${escapeHtml(textFor(k.translations, getLangId()) || k.name)} — ${escapeHtml(k.kind)}</option>`).join('');
  translationFields('prodTranslations', product ? product.translations : []);

  productModalInstance.show();
}

async function submitProduct() {
  const translations = collectTranslations('prodTranslations');
  const missing = missingLanguages(translations);

  if (missing.length) { toast('A name is required in every language', `Missing: ${missing.join(', ')}.`); return; }

  const body = {
    translations,
    imageUrl: document.getElementById('prodImage').value.trim() || null,
    productKindId: document.getElementById('prodKind').value,
    active: document.getElementById('prodActive').checked
  };

  try {
    if (editingProductId) {
      await api('PUT', `/api/admin/products/${editingProductId}`, body);
    } else {
      normaliseKeyInput();
      body.key = document.getElementById('prodKey').value.trim();
      if (!body.key) {
        toast('A product needs a key', 'Lowercase letters, digits and underscores, starting with a letter.');
        return;
      }
      await api('POST', '/api/admin/products', body);
    }
  } catch { return; }

  productModalInstance.hide();
  await loadProducts();
  if (selectedProduct) {
    selectedProduct = products.find(p => p.productId === selectedProduct.productId) || null;
    renderGrantsHeader();
  }
}

async function deleteProduct(productId) {
  const product = products.find(p => p.productId === productId);
  if (!confirm(`Delete product "${product.key}" and its grants? This cannot be undone.`)) return;

  try {
    await api('DELETE', `/api/admin/products/${productId}`);
  } catch (e) {
    const owners = e.payload && e.payload.details ? e.payload.details.ownerCount : null;
    if (owners != null) {
      toast('Product is owned',
        `${owners} account(s) own it and their entitlements resolve through it. Retire it instead — untick Active.`);
    }
    return;
  }

  if (selectedProduct && selectedProduct.productId === productId) selectedProduct = null;
  await loadProducts();
}

// ---------------------------------------------------------------------------
// Grants
// ---------------------------------------------------------------------------
async function loadGrants() {
  if (!selectedProduct) { renderGrantsHeader(); return; }

  const data = await api('GET', `/api/admin/product-grants?productId=${selectedProduct.productId}`);
  const list = data.grants || [];
  const frozen = selectedProduct.ownerCount > 0;

  document.getElementById('grantList').innerHTML = list.length
    ? `${frozen ? `<div class="grants-frozen-banner">
           <i class="bi bi-lock me-1"></i><strong>Frozen.</strong> ${selectedProduct.ownerCount}
           account(s) own this product, so its grants can no longer be added to, edited or removed.
         </div>` : ''}
       <table class="table table-sm align-middle mb-0">
         <thead><tr><th>Kind</th><th>Reference</th><th class="text-center">Quantity</th><th></th></tr></thead>
         <tbody>${list.map(g => `<tr>
           <td><span class="kind-token">${escapeHtml(g.kind)}</span></td>
           <td class="mono">${escapeHtml(g.reference)}</td>
           <td class="text-center">${g.quantity}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-secondary" title="Edit" ${frozen ? 'disabled' : ''}
                     onclick="openGrantModal('${g.grantId}')"><i class="bi bi-pencil"></i></button>
             <button class="btn btn-sm btn-outline-danger" title="Delete" ${frozen ? 'disabled' : ''}
                     onclick="deleteGrant('${g.grantId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table>`
    : `<div class="empty"><i class="bi bi-gift"></i>
         <strong>${escapeHtml(selectedProduct.key)}</strong> hands over nothing yet.</div>`;

  grantsCache = list;
}

function openGrantModal(grantId) {
  if (!selectedProduct) { alert('Pick a product first.'); return; }
  if (selectedProduct.ownerCount > 0) {
    alert('This product is owned by ' + selectedProduct.ownerCount + ' account(s), so its grants are frozen.');
    return;
  }

  editingGrantId = grantId || null;
  const grant = grantId ? grantsCache.find(g => g.grantId === grantId) : null;

  document.getElementById('grantTitle').textContent = grant ? 'Edit grant' : 'Add grant';
  document.getElementById('grantProduct').innerHTML =
    `Handed over by <strong>${escapeHtml(selectedProduct.key)}</strong>, read by Unity as `
    + `<span class="kind-token">${escapeHtml(selectedProduct.kind)}</span>.`;
  document.getElementById('grantRef').value = grant ? grant.reference : '';
  document.getElementById('grantQty').value = grant ? grant.quantity : 1;

  grantModalInstance.show();
}

async function submitGrant() {
  const reference = document.getElementById('grantRef').value.trim();
  const quantity = Number(document.getElementById('grantQty').value);

  if (!reference) { toast('A grant needs a reference', "It is the client's own id for the thing."); return; }
  if (!(quantity >= 1)) { toast('Quantity must be at least 1', `Got "${quantity}".`); return; }

  try {
    if (editingGrantId) {
      await api('PUT', `/api/admin/product-grants/${editingGrantId}`, { reference, quantity });
    } else {
      await api('POST', '/api/admin/product-grants', {
        productId: selectedProduct.productId, reference, quantity
      });
    }
  } catch { return; }

  grantModalInstance.hide();
  await loadProducts();
  selectedProduct = products.find(p => p.productId === selectedProduct.productId) || null;
  await loadGrants();
}

async function deleteGrant(grantId) {
  const grant = grantsCache.find(g => g.grantId === grantId);
  if (!confirm(`Stop "${selectedProduct.key}" granting "${grant.reference}"?`)) return;

  try {
    await api('DELETE', `/api/admin/product-grants/${grantId}`);
  } catch { return; }

  await loadProducts();
  selectedProduct = products.find(p => p.productId === selectedProduct.productId) || null;
  await loadGrants();
}

// ---------------------------------------------------------------------------
// Load all
// ---------------------------------------------------------------------------
async function loadShop() {
  await loadProductKinds();
  await loadProducts();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initShop() {
  if (!guardAuth()) return;
  initNav('shop');

  kindModalInstance    = new bootstrap.Modal(document.getElementById('kindModal'));
  productModalInstance = new bootstrap.Modal(document.getElementById('productModal'));
  grantModalInstance   = new bootstrap.Modal(document.getElementById('grantModal'));

  // Expose to inline onclick handlers
  window.openKindModal    = openKindModal;
  window.submitKind       = submitKind;
  window.deleteKind       = deleteKind;
  window.loadProductKinds = loadProductKinds;
  window.openProductModal = openProductModal;
  window.submitProduct    = submitProduct;
  window.deleteProduct    = deleteProduct;
  window.loadProducts     = loadProducts;
  window.normaliseKeyInput = normaliseKeyInput;
  window.previewKind      = previewKind;
  window.openGrantModal   = openGrantModal;
  window.submitGrant      = submitGrant;
  window.deleteGrant      = deleteGrant;
  window.loadGrants       = loadGrants;

  renderGrantsHeader();

  try {
    await loadLanguages([]);
    await loadShop();
  } catch (e) { /* already toasted */ }
}
