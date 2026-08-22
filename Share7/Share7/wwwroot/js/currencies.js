// ===========================================================================
// Share7 Admin Console — Currencies page logic
// ===========================================================================

import state from './state.js';
import { api, toast, loadLanguages } from './api.js';
import { escapeHtml } from './utils.js';
import { guardAuth } from './auth.js';
import { initNav } from './nav.js';

let currencies = [];

// ---------------------------------------------------------------------------
// Currencies
// ---------------------------------------------------------------------------
async function loadCurrencies() {
  const data = await api('GET', '/api/currencies');
  const list = data.currencies || [];
  currencies = list;

  document.getElementById('currencyList').innerHTML = list.length
    ? `<table class="table table-sm table-hover align-middle mb-0">
         <thead><tr><th>Key</th><th>Name</th><th>Description</th><th class="text-center">Status</th></tr></thead>
         <tbody>${list.map(c => `<tr>
           <td><code>${escapeHtml(c.key)}</code></td>
           <td>${escapeHtml(c.name)}</td>
           <td class="muted-sm">${escapeHtml(c.description || '—')}</td>
           <td class="text-center">${c.enabled
                ? '<span class="badge text-bg-success">active</span>'
                : '<span class="badge text-bg-secondary">retired</span>'}</td>
         </tr>`).join('')}</tbody></table>`
    : '<div class="empty"><i class="bi bi-coin"></i>No currencies defined yet.</div>';

  // Populate the grant currency dropdown
  const grantCurrencyEl = document.getElementById('grantCurrency');
  if (grantCurrencyEl) {
    grantCurrencyEl.innerHTML = list.map(c =>
      `<option value="${c.currencyId}">${escapeHtml(c.key)}</option>`).join('');
  }
}

async function createCurrency() {
  const key = document.getElementById('curKey').value.trim();
  const name = document.getElementById('curName').value.trim();

  if (!key) { toast('Key required', 'Lowercase letters, digits and underscores.'); return; }
  if (!name) { toast('Name required', 'A human-readable name for the currency.'); return; }

  await api('POST', '/api/currencies', {
    key,
    name,
    description: document.getElementById('curDesc').value.trim() || null
  });

  // Clear form
  document.getElementById('curKey').value = '';
  document.getElementById('curName').value = '';
  document.getElementById('curDesc').value = '';

  toast('Currency created', `"${key}" is now available.`, 'success');
  await loadCurrencies();
}

// ---------------------------------------------------------------------------
// Balances
// ---------------------------------------------------------------------------
async function loadBalances() {
  const data = await api('GET', '/api/commerce/balances');
  const list = data.balances || [];

  document.getElementById('balanceList').innerHTML = list.length
    ? `<div class="row g-2">${list.map(b => `
        <div class="col-sm-6">
          <div class="balance-card">
            <div class="balance-label">${escapeHtml(b.currency)}</div>
            <div class="balance-amount">${b.amount.toLocaleString()}</div>
          </div>
        </div>`).join('')}</div>`
    : '<div class="empty"><i class="bi bi-wallet2"></i>No balances yet.</div>';
}

async function grantCurrency() {
  const currencyId = document.getElementById('grantCurrency').value;
  const amount = Number(document.getElementById('grantAmount').value);

  if (!currencyId) { toast('Select a currency', ''); return; }
  if (!amount) { toast('Amount required', 'Positive to credit, negative to debit.'); return; }

  await api('POST', '/api/currencies/grant', {
    currencyId,
    amount,
    reason: document.getElementById('grantReason').value.trim() || null
  });

  toast('Balance updated', `${amount > 0 ? '+' : ''}${amount} credited.`, 'success');
  document.getElementById('grantReason').value = '';
  await loadBalances();
}

// ---------------------------------------------------------------------------
// Init
// ---------------------------------------------------------------------------
export async function initCurrencies() {
  if (!guardAuth()) return;
  initNav('currencies');

  window.createCurrency = createCurrency;
  window.grantCurrency  = grantCurrency;
  window.loadCurrencies = loadCurrencies;
  window.loadBalances   = loadBalances;

  try {
    await loadLanguages([]);
    await loadCurrencies();
    await loadBalances();
  } catch (e) { /* already toasted */ }
}
