// ===========================================================================
// Share7 Admin Console — Offers page logic
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages, translationFields, collectTranslations } from './api.js';
import { escapeHtml, textFor, missingLanguages } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

let offers = [];
let products = [];
let kinds = [];
let currencies = [];
let serverSkewMs = 0;
let offerModalInstance = null;

function getLangId() { return state.selectedLangId; }

// ---------------------------------------------------------------------------
// Server time
// ---------------------------------------------------------------------------
async function loadServerTime() {
  const data = await api('GET', '/api/time');
  serverSkewMs = new Date(data.utcNow).getTime() - Date.now();
  document.getElementById('serverTime').textContent = data.utcNow;
}

function serverNow() {
  return new Date(Date.now() + serverSkewMs);
}

function setExpiryFromServer(hours) {
  const when = new Date(serverNow().getTime() + hours * 3600 * 1000);
  document.getElementById('offerExpires').value = when.toISOString().slice(0, 16);
}

function clearExpiry() {
  document.getElementById('offerExpires').value = '';
}

// ---------------------------------------------------------------------------
// Load data
// ---------------------------------------------------------------------------
async function loadOffers() {
  const data = await api('GET', '/api/admin/offers');
  offers = data.offers || [];

  document.getElementById('offerList').innerHTML = offers.length
    ? `<div class="table-responsive"><table class="table table-sm table-hover align-middle mb-0">
         <thead><tr><th>Name</th><th>Price</th><th>Products</th><th class="text-center">Limit</th>
                    <th class="text-center">Purchases</th><th>Expires</th><th class="text-center">State</th>
                    <th class="text-center">Sort</th><th></th></tr></thead>
         <tbody>${offers.map(o => `<tr>
           <td>${escapeHtml(o.name || '—')}
               ${o.description
                 ? `<div class="muted-sm text-truncate" style="max-width:18rem">${escapeHtml(o.description)}</div>` : ''}</td>
           <td class="text-nowrap"><strong>${o.price}</strong> <code>${escapeHtml(o.currency)}</code>
               ${o.originalPrice != null ? `<div class="muted-sm"><s>${o.originalPrice}</s></div>` : ''}</td>
           <td>${o.products.map(p => `<span class="mono muted-sm d-block">${escapeHtml(p.key)}${
                 p.grantCount ? '' : ' <span class="badge text-bg-warning">no grants</span>'}</span>`).join('')}</td>
           <td class="text-center">${o.purchaseLimit == null ? '<span class="muted-sm">∞</span>' : o.purchaseLimit}</td>
           <td class="text-center">${o.purchaseCount
                ? `<span class="badge text-bg-primary" title="cannot be deleted">${o.purchaseCount}</span>`
                : '<span class="muted-sm">0</span>'}</td>
           <td class="muted-sm text-nowrap">${o.expiresAtUtc ? escapeHtml(o.expiresAtUtc.replace('T', ' ').slice(0, 16)) : '—'}</td>
           <td class="text-center">${
                o.availability !== 'AVAILABLE' ? '<span class="badge text-bg-secondary">off</span>'
              : o.expired ? '<span class="badge text-bg-danger">expired</span>'
              : '<span class="badge text-bg-success">live</span>'}</td>
           <td class="text-center muted-sm">${o.sortOrder}</td>
           <td class="text-end text-nowrap">
             <button class="btn btn-sm btn-outline-danger" title="Delete"
                     onclick="deleteOffer('${o.offerId}')"><i class="bi bi-trash"></i></button>
           </td>
         </tr>`).join('')}</tbody></table></div>`
    : '<div class="empty"><i class="bi bi-tag"></i>Nothing on sale yet.</div>';
}

async function loadProducts() {
  const data = await api('GET', '/api/admin/products');
  products = data.products || [];
}

async function loadProductKinds() {
  const data = await api('GET', '/api/admin/product-kinds');
  kinds = data.productKinds || [];
}

async function loadCurrencies() {
  const data = await api('GET', '/api/currencies');
  currencies = data.currencies || [];
}

// ---------------------------------------------------------------------------
// Create
// ---------------------------------------------------------------------------
function openOfferModal() {
  if (!products.length) { alert('Create a product first — an offer has to sell something.'); return; }
  if (!currencies.length) { alert('Create a currency first — an offer has to be priced in something.'); return; }

  document.getElementById('offerTitle').textContent = 'Add offer';
  document.getElementById('offerPrice').value = 100;
  document.getElementById('offerOriginal').value = '';
  document.getElementById('offerAvailability').value = 'AVAILABLE';
  document.getElementById('offerLimit').value = '';
  document.getElementById('offerSort').value = 0;
  document.getElementById('offerBadge').value = '';
  document.getElementById('offerExpires').value = '';

  document.getElementById('offerCurrency').innerHTML = currencies.map(c =>
    `<option value="${c.currencyId}">${escapeHtml(c.key)}${c.enabled ? '' : ' (retired)'}</option>`).join('');

  document.getElementById('offerProducts').innerHTML = products.map(p => `
    <label class="list-group-item d-flex align-items-center gap-2">
      <input class="form-check-input m-0 offer-product" type="checkbox" value="${p.productId}" />
      <span class="mono">${escapeHtml(p.key)}</span>
      <span class="kind-token">${escapeHtml(p.kind)}</span>
      <span class="text-truncate">${escapeHtml(textFor(p.translations, getLangId()) || '')}</span>
      ${p.grants.length ? `<span class="badge text-bg-light border ms-auto">${p.grants.length} grant(s)</span>`
                        : '<span class="badge text-bg-warning ms-auto">grants nothing</span>'}
      ${p.active ? '' : '<span class="badge text-bg-secondary">retired</span>'}
    </label>`).join('');

  translationFields('offerTranslations', []);
  offerModalInstance.show();
}

async function submitOffer() {
  const translations = collectTranslations('offerTranslations');
  const missing = missingLanguages(translations);
  if (missing.length) { toast('A name is required in every language', `Missing: ${missing.join(', ')}.`); return; }

  const productIds = [...document.querySelectorAll('.offer-product:checked')].map(i => i.value);
  if (!productIds.length) { toast('An offer must sell something', 'Tick at least one product.'); return; }

  const expires = document.getElementById('offerExpires').value;
  const original = document.getElementById('offerOriginal').value;
  const limit = document.getElementById('offerLimit').value;

  const body = {
    translations,
    productIds,
    currencyId: document.getElementById('offerCurrency').value,
    price: Number(document.getElementById('offerPrice').value),
    originalPrice: original === '' ? null : Number(original),
    availability: document.getElementById('offerAvailability').value,
    purchaseLimit: limit === '' ? null : Number(limit),
    expiresAtUtc: expires === '' ? null : `${expires}:00Z`,
    sortOrder: Number(document.getElementById('offerSort').value),
    badgeKey: document.getElementById('offerBadge').value.trim() || null
  };

  try {
    await api('POST', '/api/admin/offers', body);
  } catch { return; }

  offerModalInstance.hide();
  await loadOffers();
}

// ---------------------------------------------------------------------------
// Delete
// ---------------------------------------------------------------------------
async function deleteOffer(offerId) {
  const offer = offers.find(o => o.offerId === offerId);
  if (!confirm(`Delete offer "${offer.name || offerId}"? This cannot be undone.`)) return;

  try {
    await api('DELETE', `/api/admin/offers/${offerId}`);
  } catch (e) {
    const count = e.payload && e.payload.details ? e.payload.details.transactionCount : null;
    if (count != null) {
      toast('Offer has history',
        `${count} transaction(s) reference it. Set availability to UNAVAILABLE to take it off sale instead.`);
    }
    return;
  }

  await loadOffers();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initOffers() {
  if (!guardAuth()) return;
  initNav('offers');

  offerModalInstance = new bootstrap.Modal(document.getElementById('offerModal'));

  window.openOfferModal = openOfferModal;
  window.submitOffer = submitOffer;
  window.deleteOffer = deleteOffer;
  window.loadOffers = loadOffers;
  window.loadServerTime = loadServerTime;
  window.setExpiryFromServer = setExpiryFromServer;
  window.clearExpiry = clearExpiry;

  try {
    await loadLanguages([]);
    await loadServerTime();
    await loadCurrencies();
    await loadProducts();
    await loadProductKinds();
    await loadOffers();
  } catch (e) { /* already toasted */ }
}
