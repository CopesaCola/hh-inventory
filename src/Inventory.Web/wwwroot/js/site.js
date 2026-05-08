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

// Global search suggestions in the top-bar input.
(function () {
    const escapeHtml = s => String(s ?? '').replace(/[&<>"']/g, c =>
        ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));

    const form = document.querySelector('.global-search');
    if (!form) { console.warn('[global-suggest] no .global-search form found'); return; }
    const input = form.querySelector('input[name="q"]');
    if (!input) { console.warn('[global-suggest] no input[name="q"] in form'); return; }
    console.log('[global-suggest] wired');

    const dropdown = document.createElement('div');
    dropdown.className = 'global-suggest';
    form.appendChild(dropdown);

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
        const total = (data.devices?.length || 0) + (data.users?.length || 0) + (data.sites?.length || 0);
        if (total === 0) {
            dropdown.innerHTML = `<div class="suggest-empty">No matches for "${escapeHtml(q)}"</div>`;
        } else {
            const html =
                section('Devices', data.devices, it => `/Devices/Details/${it.id}`) +
                section('Users', data.users, it => `/Users/Details/${it.id}`) +
                section('Sites', data.sites, it => `/Sites/Details/${it.id}`);
            dropdown.innerHTML = html +
                `<a class="suggest-all" href="/Search?q=${encodeURIComponent(q)}">See all results &rarr;</a>`;
        }
        dropdown.classList.add('open');
    }

    async function fetchSuggest(q) {
        if (!q || q.trim().length < 2) {
            dropdown.classList.remove('open');
            dropdown.innerHTML = '';
            return;
        }
        const fullUrl = `/api/search/suggest?q=${encodeURIComponent(q.trim())}`;
        try {
            const r = await fetch(fullUrl, { credentials: 'same-origin' });
            if (!r.ok) {
                console.error('[global-suggest] fetch failed', r.status, r.statusText, fullUrl);
                return;
            }
            const data = await r.json();
            render(data, q.trim());
        } catch (e) {
            console.error('[global-suggest] error', e);
        }
    }

    input.addEventListener('input', () => {
        clearTimeout(timer);
        timer = setTimeout(() => fetchSuggest(input.value), 200);
    });

    input.addEventListener('focus', () => {
        if (input.value.trim().length >= 2 && dropdown.innerHTML) {
            dropdown.classList.add('open');
        }
    });

    document.addEventListener('mousedown', (e) => {
        if (!form.contains(e.target)) dropdown.classList.remove('open');
    });
})();
