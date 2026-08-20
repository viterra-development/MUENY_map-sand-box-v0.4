// MUENY map key — a collapsible legend that mirrors exactly what the
// renderers draw. Sections appear only for layers that are currently
// visible. Plain script shared by the Blazor and JS-fallback paths;
// maplibre-deckgl-integration.js calls window.MuenyKey.update(layers)
// whenever layer visibility changes.
//
// IMPORTANT: swatch colors below must stay in sync with the color
// functions in maplibre-deckgl-integration.js (crash severity, risk
// levels, stress tiers, traffic gradient, soil ramps, trip colors).
(function () {
    const rgba = (r, g, b, a = 1) => `rgba(${r},${g},${b},${a})`;

    // ---- section specs -------------------------------------------------
    // match: (layerId) => bool against VISIBLE layers.
    const SECTIONS = [
        {
            match: id => id === 'cris-crashes',
            title: 'Crash points — severity',
            note: 'Dot size grows with crashes at the same location.',
            rows: [
                { swatch: 'dot', color: rgba(139, 0, 0), label: 'Fatal (K)' },
                { swatch: 'dot', color: rgba(255, 69, 0), label: 'Serious injury (A)' },
                { swatch: 'dot', color: rgba(255, 140, 0), label: 'Minor injury (B)' },
                { swatch: 'dot', color: rgba(255, 215, 0), label: 'Possible injury (C)' },
                { swatch: 'dot', color: rgba(50, 205, 50), label: 'No injury (O)' },
                { swatch: 'dot', color: rgba(128, 128, 128), label: 'Unknown' },
            ],
        },
        {
            match: id => id === 'cris-risk-segments',
            title: 'Road crash risk',
            rows: [
                { swatch: 'line', color: rgba(139, 0, 0), label: 'Very high' },
                { swatch: 'line', color: rgba(255, 69, 0), label: 'High' },
                { swatch: 'line', color: rgba(255, 140, 0), label: 'Moderate' },
                { swatch: 'line', color: rgba(255, 215, 0), label: 'Low' },
                { swatch: 'line', color: rgba(50, 205, 50), label: 'Very low' },
            ],
        },
        {
            match: id => id === 'cris-intersections',
            title: 'High-risk intersections',
            rows: [
                { swatch: 'dot', color: rgba(230, 126, 34), label: 'Intersection — size reflects risk score' },
            ],
        },
        {
            match: id => id === 'cris-road-stress',
            title: 'Road stress index (volume vs. capacity)',
            note: 'Thicker lines = higher stress tier.',
            rows: [
                { swatch: 'line', color: '#ef4444', label: 'Critical' },
                { swatch: 'line', color: '#f97316', label: 'High' },
                { swatch: 'line', color: '#eab308', label: 'Moderate' },
                { swatch: 'line', color: '#22c55e', label: 'Low' },
            ],
        },
        {
            match: id => id === 'parker-roads-traffic' || id === 'parker-roads-traffic-phase1' || id === 'traffic-counts',
            title: 'Traffic volume (AADT)',
            note: 'Vehicles per day, log scale. Wider lines carry more traffic.',
            rows: [
                { swatch: 'gradient', from: 'rgb(0,255,0)', via: 'rgb(255,255,0)', to: 'rgb(255,0,0)', label: '10 → 160,000+ veh/day' },
                { swatch: 'line', color: rgba(120, 120, 120, 0.6), label: 'No observed data' },
            ],
        },
        {
            match: id => id.endsWith('-parcels-trips'),
            title: 'Parcel trip generation (daily trips)',
            rows: [
                { swatch: 'box', color: 'rgb(42,56,140)', label: '200+' },
                { swatch: 'box', color: 'rgb(88,108,190)', label: '50–199' },
                { swatch: 'box', color: 'rgb(141,160,220)', label: '10–49' },
                { swatch: 'box', color: 'rgb(199,210,240)', label: '1–9' },
                { swatch: 'box', color: 'rgb(206,208,206)', label: '0 / unknown' },
                { swatch: 'box', color: 'rgb(34,139,34)', label: 'City-owned' },
            ],
        },
        {
            match: id => id === 'soil-clay-visualization',
            title: 'Soil clay content',
            rows: [
                { swatch: 'box', color: 'rgb(245,222,179)', label: '< 10% (sandy)' },
                { swatch: 'box', color: 'rgb(222,184,135)', label: '10–20% (sandy loam)' },
                { swatch: 'box', color: 'rgb(205,133,63)', label: '20–35% (loam)' },
                { swatch: 'box', color: 'rgb(160,82,45)', label: '35–50% (clay loam)' },
                { swatch: 'box', color: 'rgb(101,67,33)', label: '> 50% (clay)' },
            ],
        },
        {
            match: id => id === 'soil-ksat-visualization',
            title: 'Soil permeability (Ksat)',
            note: 'How quickly water drains through the soil.',
            rows: [
                { swatch: 'box', color: 'rgb(0,100,255)', label: '> 10 μm/s (drains fast)' },
                { swatch: 'box', color: 'rgb(0,150,200)', label: '5–10' },
                { swatch: 'box', color: 'rgb(100,200,100)', label: '1–5' },
                { swatch: 'box', color: 'rgb(255,150,0)', label: '0.1–1' },
                { swatch: 'box', color: 'rgb(255,0,0)', label: '< 0.1 (drains poorly)' },
            ],
        },
        {
            match: id => id.startsWith('noaa-rainfall'),
            title: 'Rainfall intensity (NOAA Atlas 14)',
            note: 'Larger, lighter dots = higher design rainfall.',
            rows: [
                { swatch: 'dot', color: 'rgb(100,220,255)', label: 'Higher' },
                { swatch: 'dot', color: 'rgb(50,200,255)', label: '\u2193' },
                { swatch: 'dot', color: 'rgb(0,150,255)', label: '\u2193' },
                { swatch: 'dot', color: 'rgb(0,100,255)', label: 'Lower' },
            ],
        },
        {
            match: id => id === 'txdot-city-boundaries',
            title: 'Boundaries',
            rows: [
                { swatch: 'outline', color: 'rgb(0,100,200)', label: 'City limits' },
            ],
        },
        {
            match: id => id === 'parker-roads-base',
            title: 'Base network',
            rows: [
                { swatch: 'line', color: rgba(120, 120, 120, 0.8), label: 'Road network (TIGER)' },
            ],
        },
    ];

    // ---- rendering ------------------------------------------------------
    let panel = null;
    let collapsed = window.innerWidth < 700;

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function swatchNode(row) {
        const s = el('span', `mueny-key-swatch mueny-key-${row.swatch}`);
        if (row.swatch === 'gradient') {
            s.style.background = `linear-gradient(90deg, ${row.from}, ${row.via}, ${row.to})`;
        } else if (row.swatch === 'outline') {
            s.style.borderColor = row.color;
        } else {
            s.style.background = row.color;
        }
        return s;
    }

    function container() {
        return document.getElementById('map-wrapper')
            || document.querySelector('.map-section')
            || document.body;
    }

    function ensurePanel() {
        if (panel && panel.isConnected) return panel;
        panel = el('aside', 'mueny-key');
        panel.setAttribute('aria-label', 'Map key');

        const head = el('button', 'mueny-key-head');
        head.type = 'button';
        head.setAttribute('aria-expanded', String(!collapsed));
        head.appendChild(el('span', 'mueny-key-title', 'Map key'));
        head.appendChild(el('span', 'mueny-key-chevron', collapsed ? '▴' : '▾'));
        head.addEventListener('click', () => {
            collapsed = !collapsed;
            panel.classList.toggle('collapsed', collapsed);
            head.setAttribute('aria-expanded', String(!collapsed));
            head.querySelector('.mueny-key-chevron').textContent = collapsed ? '▴' : '▾';
        });
        panel.appendChild(head);
        panel.appendChild(el('div', 'mueny-key-body'));
        panel.classList.toggle('collapsed', collapsed);
        container().appendChild(panel);
        return panel;
    }

    function update(layers) {
        const visibleIds = (layers || [])
            .filter(l => l && l.visible !== false && (l.visible === true || l.Visible === true))
            .map(l => l.id || l.Id)
            .filter(Boolean);

        const active = SECTIONS.filter(sec => visibleIds.some(id => sec.match(id)));
        const p = ensurePanel();
        const body = p.querySelector('.mueny-key-body');
        body.replaceChildren();

        if (!active.length) {
            p.style.display = 'none';
            return;
        }
        p.style.display = '';

        active.forEach(sec => {
            const secEl = el('div', 'mueny-key-section');
            secEl.appendChild(el('div', 'mueny-key-sec-title', sec.title));
            sec.rows.forEach(row => {
                const rowEl = el('div', 'mueny-key-row');
                rowEl.appendChild(swatchNode(row));
                rowEl.appendChild(el('span', 'mueny-key-label', row.label));
                secEl.appendChild(rowEl);
            });
            if (sec.note) secEl.appendChild(el('div', 'mueny-key-note', sec.note));
            body.appendChild(secEl);
        });
    }

    window.MuenyKey = { update };
})();
