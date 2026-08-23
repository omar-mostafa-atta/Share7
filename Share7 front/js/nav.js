// ===========================================================================
// Share7 Admin Console — Sidebar navigation
// Injects the sidebar + mobile toggle into every dashboard page.
// ===========================================================================

import state from './state.js';
import { signOut, renderUserBadge } from './auth.js';
import { escapeHtml } from './utils.js';

const NAV_ITEMS = [
  { section: 'Content' },
  { id: 'curriculum', label: 'Curriculum',  icon: 'bi-diagram-3',               href: 'curriculum.html' },
  { section: 'Engagement' },
  { id: 'objectives', label: 'Objectives',  icon: 'bi-trophy',                  href: 'objectives.html' },
  { section: 'Commerce' },
  { id: 'games',      label: 'Games',       icon: 'bi-controller',              href: 'games.html' },
  { id: 'shop',       label: 'Shop',        icon: 'bi-bag',                     href: 'shop.html' },
  { id: 'offers',     label: 'Offers',      icon: 'bi-tag',                     href: 'offers.html' },
  { id: 'currencies', label: 'Currencies',  icon: 'bi-coin',                    href: 'currencies.html' },
];

/**
 * Inject the sidebar and mobile toggle into the page.
 * @param {string} activeId - The id of the current page's nav item to highlight.
 */
export function initNav(activeId) {
  // Build nav links
  let navHtml = '';
  for (const item of NAV_ITEMS) {
    if (item.section) {
      navHtml += `<div class="sidebar-section-label">${escapeHtml(item.section)}</div>`;
      continue;
    }
    const isActive = item.id === activeId;
    navHtml += `
      <a href="${item.href}" class="sidebar-link ${isActive ? 'active' : ''}" data-nav="${item.id}">
        <i class="bi ${item.icon}"></i>
        <span>${escapeHtml(item.label)}</span>
      </a>`;
  }

  // Build sidebar
  const sidebar = document.createElement('aside');
  sidebar.className = 'sidebar';
  sidebar.id = 'sidebar';
  sidebar.innerHTML = `
    <div class="sidebar-brand">
      <div class="sidebar-brand-icon"><i class="bi bi-shield-lock"></i></div>
      <div>
        <div class="sidebar-brand-text">Share7</div>
        <span class="sidebar-brand-sub">Admin Console</span>
      </div>
    </div>

    <nav class="sidebar-nav">
      ${navHtml}
    </nav>

    <div class="sidebar-footer">
      <div class="sidebar-user">
        <div class="sidebar-avatar" id="sidebar-avatar">A</div>
        <div>
          <div class="sidebar-user-name" id="sidebar-user-name">Admin</div>
          <div class="sidebar-user-role" id="sidebar-user-role">—</div>
        </div>
        <button class="sidebar-logout" id="btn-logout" title="Sign out">
          <i class="bi bi-box-arrow-right"></i>
        </button>
      </div>
    </div>`;

  // Build overlay
  const overlay = document.createElement('div');
  overlay.className = 'sidebar-overlay';
  overlay.id = 'sidebar-overlay';

  // Build mobile toggle
  const toggle = document.createElement('button');
  toggle.className = 'sidebar-toggle';
  toggle.id = 'sidebar-toggle';
  toggle.innerHTML = '<i class="bi bi-list"></i>';

  // Insert into DOM
  document.body.prepend(overlay);
  document.body.prepend(sidebar);
  document.body.appendChild(toggle);

  // Event: toggle sidebar
  toggle.addEventListener('click', () => {
    sidebar.classList.toggle('open');
    overlay.classList.toggle('show');
  });

  // Event: close on overlay click
  overlay.addEventListener('click', () => {
    sidebar.classList.remove('open');
    overlay.classList.remove('show');
  });

  // Event: logout
  document.getElementById('btn-logout').addEventListener('click', (e) => {
    e.preventDefault();
    if (confirm('Sign out?')) signOut();
  });

  // Render user info
  renderUserBadge();
}
