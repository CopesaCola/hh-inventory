console.log('[inventory] site.js loaded');

// Clickable rows: any <tr data-href="..."> navigates on click.
(function () {
    function rowClick(e) {
        const row = e.target.closest('tr[data-href]');
        if (!row) return;
        if (e.target.closest('a, button, input, select, textarea, label, form')) return;
        const href = row.getAttribute('data-href');
        if (!href) return;
        if (e.ctrlKey || e.metaKey || e.shiftKey || e.button === 1) {
            window.open(href, '_blank');
        } else if (e.button === 0) {
            window.location.href = href;
        }
    }
    document.addEventListener('click', rowClick);
    document.addEventListener('auxclick', rowClick);
})();

// Combobox typeahead.
(function () {
    const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function setupCombobox(box) {
        const input = box.querySelector('.combobox-input');
        const hidden = box.querySelector('.combobox-id');
        const results = box.querySelector('.combobox-results');
        const clearBtn = box.querySelector('.combobox-clear');
        const url = box.dataset.searchUrl;
        if (!input || !hidden || !results || !url) {
            console.warn('[combobox] skipping (missing parts)', { input: !!input, hidden: !!hidden, results: !!results, url });
            return;
        }
        console.log('[combobox] wired', url);

        let timer = null;
        let activeIndex = -1;
        let items = [];

        function close() {
            results.classList.remove('open');
            results.innerHTML = '';
            activeIndex = -1;
        }

        function render() {
            results.innerHTML = items.map((it, i) =>
                `<div class="combobox-item${i === activeIndex ? ' active' : ''}"
                      data-id="${escapeHtml(it.id)}" data-name="${escapeHtml(it.name)}">
                   <span>${escapeHtml(it.name)}</span>
                   ${it.site ? `<span class="muted"> — ${escapeHtml(it.site)}</span>` : ''}
                 </div>`).join('');
            results.classList.toggle('open', items.length > 0);
        }

        async function search(q) {
            if (!q || !q.trim()) { items = []; close(); return; }
            const fullUrl = `${url}?q=${encodeURIComponent(q.trim())}`;
            try {
                const r = await fetch(fullUrl, { credentials: 'same-origin' });
                if (!r.ok) {
                    console.error('[combobox] fetch failed', r.status, r.statusText, fullUrl);
                    return;
                }
                items = await r.json();
                activeIndex = items.length > 0 ? 0 : -1;
                render();
            } catch (e) {
                console.error('[combobox] error', e);
            }
        }

        function pick(it) {
            input.value = it.name;
            hidden.value = it.id;
            close();
        }

        input.addEventListener('input', () => {
            hidden.value = '';
            clearTimeout(timer);
            timer = setTimeout(() => search(input.value), 180);
        });

        input.addEventListener('keydown', (e) => {
            if (e.key === 'ArrowDown') {
                e.preventDefault();
                if (items.length === 0) return;
                activeIndex = Math.min(items.length - 1, activeIndex + 1);
                render();
            } else if (e.key === 'ArrowUp') {
                e.preventDefault();
                if (items.length === 0) return;
                activeIndex = Math.max(0, activeIndex - 1);
                render();
            } else if (e.key === 'Enter') {
                if (activeIndex >= 0 && items[activeIndex]) {
                    e.preventDefault();
                    pick(items[activeIndex]);
                }
            } else if (e.key === 'Escape') {
                close();
            }
        });

        results.addEventListener('mousedown', (e) => {
            const el = e.target.closest('.combobox-item');
            if (!el) return;
            e.preventDefault();
            pick({ id: el.dataset.id, name: el.dataset.name });
        });

        if (clearBtn) {
            clearBtn.addEventListener('click', (e) => {
                e.preventDefault();
                input.value = '';
                hidden.value = '';
                close();
                input.focus();
            });
        }

        document.addEventListener('mousedown', (e) => {
            if (!box.contains(e.target)) close();
        });
    }

    const boxes = document.querySelectorAll('.combobox');
    console.log(`[combobox] found ${boxes.length} combobox(es) on page`);
    boxes.forEach(setupCombobox);
})();

// Search suggestions: powers the top-bar global search AND the search inputs on
// Devices/Users/Sites index pages. Same API (/api/search/suggest), same UI; each
// caller passes a `scope` so each page suggests only the entity type that fits.
(function () {
    const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    function wireSuggest(container, opts) {
        const input = container.querySelector('input[name="q"]');
        if (!input) { console.warn('[suggest] no input[name="q"] in container', container); return; }
        const scope = opts.scope || 'all';                            // 'all' | 'devices' | 'users' | 'sites'
        const showSeeAll = !!opts.showSeeAll;
        const dropdownClass = opts.dropdownClass || 'entity-suggest-dropdown';
        const minLen = opts.minLen ?? 2;

        const dropdown = document.createElement('div');
        dropdown.className = dropdownClass;
        container.appendChild(dropdown);

        let timer = null;

        function section(title, items, hrefFn) {
            if (!items || items.length === 0) return '';
            const rows = items.map(it =>
                `<a class="suggest-item" href="${hrefFn(it)}">
                   <span>${escapeHtml(it.name)}</span>
                   ${it.subtitle ? `<span class="muted">${escapeHtml(it.subtitle)}</span>` : ''}
                 </a>`).join('');
            return `<div class="suggest-section">
                      <div class="suggest-section-title">${title}</div>${rows}
                    </div>`;
        }

        function render(data, q) {
            const includeD  = scope === 'all' || scope === 'devices';
            const includeU  = scope === 'all' || scope === 'users';
            const includeS  = scope === 'all' || scope === 'sites';
            const includeSu = scope === 'all' || scope === 'suites';
            const total =
                (includeD  ? (data.devices?.length || 0) : 0) +
                (includeU  ? (data.users?.length   || 0) : 0) +
                (includeS  ? (data.sites?.length   || 0) : 0) +
                (includeSu ? (data.suites?.length  || 0) : 0);

            if (total === 0) {
                dropdown.innerHTML = `<div class="suggest-empty">No matches for "${escapeHtml(q)}"</div>`;
            } else {
                let html = '';
                if (includeD)  html += section('Devices', data.devices, it => `/Devices/Details/${it.id}`);
                if (includeU)  html += section('Users',   data.users,   it => `/Users/Details/${it.id}`);
                if (includeSu) html += section('Suites',  data.suites,  it => `/Suites/Details/${it.id}`);
                if (includeS)  html += section('Sites',   data.sites,   it => `/Sites/Details/${it.id}`);
                if (showSeeAll) {
                    html += `<a class="suggest-all" href="/Search?q=${encodeURIComponent(q)}">See all results &rarr;</a>`;
                }
                dropdown.innerHTML = html;
            }
            dropdown.classList.add('open');
        }

        async function fetchSuggest(q) {
            if (!q || q.trim().length < minLen) {
                dropdown.classList.remove('open');
                dropdown.innerHTML = '';
                return;
            }
            const fullUrl = `/api/search/suggest?q=${encodeURIComponent(q.trim())}`;
            try {
                const r = await fetch(fullUrl, { credentials: 'same-origin' });
                if (!r.ok) {
                    console.error('[suggest] fetch failed', r.status, r.statusText, fullUrl);
                    return;
                }
                render(await r.json(), q.trim());
            } catch (e) {
                console.error('[suggest] error', e);
            }
        }

        input.addEventListener('input', () => {
            clearTimeout(timer);
            timer = setTimeout(() => fetchSuggest(input.value), 200);
        });

        input.addEventListener('focus', () => {
            if (input.value.trim().length >= minLen && dropdown.innerHTML) {
                dropdown.classList.add('open');
            }
        });

        document.addEventListener('mousedown', (e) => {
            if (!container.contains(e.target)) dropdown.classList.remove('open');
        });
    }

    // Top-bar global search (all entity types, with "See all results" footer).
    const globalForm = document.querySelector('.global-search');
    if (globalForm) {
        wireSuggest(globalForm, { scope: 'all', showSeeAll: true, dropdownClass: 'global-suggest' });
        console.log('[suggest] global wired');
    } else {
        console.warn('[suggest] no .global-search form found');
    }

    // Entity index pages: <div class="entity-suggest" data-search-scope="devices|users|sites">.
    const entityBoxes = document.querySelectorAll('.entity-suggest[data-search-scope]');
    entityBoxes.forEach(c => {
        wireSuggest(c, {
            scope: c.dataset.searchScope,
            showSeeAll: false,
            dropdownClass: 'entity-suggest-dropdown'
        });
    });
    console.log(`[suggest] ${entityBoxes.length} entity-scoped input(s) wired`);
})();

// Show/hide the "Grant or Dept name" text input based on the IsGrantFunded
// select on the Devices Create/Edit forms. Element with [data-grant-toggle]
// is the select; element with [data-grant-detail] is the wrapper to toggle.
(function () {
    const sel = document.querySelector('[data-grant-toggle]');
    const detail = document.querySelector('[data-grant-detail]');
    if (!sel || !detail) return;
    const update = () => { detail.hidden = sel.value !== 'true'; };
    update();
    sel.addEventListener('change', update);
})();

// Filter the Suite dropdown on User Create/Edit by the currently-selected Site.
// Suite <option> elements carry data-site-id; we hide the ones that don't match.
// If the currently-selected suite belongs to a different site, the select is
// reset to "(none)" so the server doesn't receive a mismatched pair.
(function () {
    const site  = document.querySelector('[data-site-select]');
    const suite = document.querySelector('[data-suite-select]');
    if (!site || !suite) return;

    const update = () => {
        const siteId = site.value;
        let selectionStillValid = false;
        for (const opt of suite.options) {
            if (!opt.value) { opt.hidden = false; continue; } // keep "(none)"
            const matches = !siteId || opt.dataset.siteId === siteId;
            opt.hidden = !matches;
            if (opt.selected && matches) selectionStillValid = true;
        }
        if (!selectionStillValid && suite.value) suite.value = '';
    };
    update();
    site.addEventListener('change', update);
})();

// Filters panel toggle on entity index pages.
// A button with [data-toggle-filters="<panelId>"] toggles the matching panel's
// hidden state. If the panel has data-active="true" (server marked any filter
// already applied in the URL), it starts expanded so the user sees the active
// filter without an extra click.
(function () {
    document.querySelectorAll('[data-toggle-filters]').forEach(btn => {
        const panel = document.getElementById(btn.getAttribute('data-toggle-filters'));
        if (!panel) return;
        const setOpen = open => {
            panel.hidden = !open;
            btn.setAttribute('aria-expanded', String(open));
            btn.classList.toggle('active', open);
        };
        if (panel.dataset.active === 'true') setOpen(true);
        btn.addEventListener('click', () => setOpen(panel.hidden));
    });
})();
