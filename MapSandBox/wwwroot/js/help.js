// MUENY help & instructions — a "?" button in the header opens a guide
// covering navigation, layers, the map key, reports, and a glossary.
// Opens automatically on a visitor's first session (localStorage flag).
// Plain script shared by the Blazor and JS-fallback paths.
(function () {
    const SEEN_KEY = 'mueny_help_seen';
    let overlay = null;

    function el(tag, className, text) {
        const node = document.createElement(tag);
        if (className) node.className = className;
        if (text !== undefined) node.textContent = text;
        return node;
    }

    function section(title, listItems, ordered) {
        const sec = el('div', 'mueny-help-section');
        sec.appendChild(el('h3', null, title));
        const list = el(ordered ? 'ol' : 'ul');
        listItems.forEach(text => list.appendChild(el('li', null, text)));
        sec.appendChild(list);
        return sec;
    }

    function glossary(entries) {
        const sec = el('div', 'mueny-help-section');
        sec.appendChild(el('h3', null, 'Glossary'));
        const dl = el('dl', 'mueny-help-gloss');
        entries.forEach(([term, def]) => {
            dl.appendChild(el('dt', null, term));
            dl.appendChild(el('dd', null, def));
        });
        sec.appendChild(dl);
        return sec;
    }

    function close() {
        if (overlay) { overlay.remove(); overlay = null; }
        document.removeEventListener('keydown', escToClose);
    }
    function escToClose(e) { if (e.key === 'Escape') close(); }

    function open() {
        close();
        try { localStorage.setItem(SEEN_KEY, '1'); } catch (_) {}

        overlay = el('div', 'mueny-help-overlay');
        overlay.addEventListener('click', (e) => { if (e.target === overlay) close(); });

        const card = el('div', 'mueny-help-card');
        card.setAttribute('role', 'dialog');
        card.setAttribute('aria-modal', 'true');
        card.setAttribute('aria-label', 'How to use MUENY');

        const closeBtn = el('button', 'mueny-help-close', '×');
        closeBtn.setAttribute('aria-label', 'Close');
        closeBtn.addEventListener('click', close);
        card.appendChild(closeBtn);

        const header = el('div', 'mueny-help-header');
        header.appendChild(el('div', 'mueny-auth-logo', 'MUENY'));
        header.appendChild(el('h2', null, 'How to use the map'));
        header.appendChild(el('p', null,
            'Crash risk, traffic, growth, and ground conditions for Parker County — on one live map.'));
        card.appendChild(header);

        const body = el('div', 'mueny-help-body');

        body.appendChild(section('Getting started', [
            'Turn layers on and off in the left panel (tap the ☰ button on mobile).',
            'Under Trip Generation, pick a city to see every parcel colored by the daily trips it produces.',
            'Click any road, crash point, intersection, parcel, or soil area for its details.',
            'The Map key (bottom-right of the map) explains every color and symbol currently shown.',
        ], true));

        body.appendChild(section('Reading the safety layers', [
            'Crash Points: each dot is a crash location, colored by the most severe outcome there.',
            'Risk Segments: roads scored by crash history, traffic volume, grade, and surface — red needs attention first.',
            'High-Risk Intersections: ranked crossings; click one for its crash record.',
            'Road Stress Index: where growth-driven traffic is outpacing road capacity.',
        ]));

        body.appendChild(section('Reports, accounts, and saved views', [
            'Generate Report builds a briefing-ready PDF summary for the selected city — sign in first (free).',
            'Your account also saves map views, so a setup you like is one click away next visit.',
            'Use the base map selector (Voyager, Light, Dark, OpenStreetMap) to match your presentation.',
        ]));

        body.appendChild(glossary([
            ['AADT', 'Annual Average Daily Traffic — vehicles per day on a road segment.'],
            ['KABCO', 'Crash severity scale: K fatal, A serious injury, B minor injury, C possible injury, O no injury.'],
            ['Risk score', 'Composite 0–1 score from crash frequency, severity, traffic exposure, and road conditions.'],
            ['Trip generation', 'Estimated daily vehicle trips a parcel produces, based on its land use (ITE method).'],
            ['Road stress', 'How close a road is to its practical capacity, given current and modeled traffic.'],
            ['Ksat', 'Saturated hydraulic conductivity — how fast water drains through soil (drainage risk).'],
        ]));

        const note = el('div', 'mueny-help-section');
        note.appendChild(el('h3', null, 'About the data'));
        note.appendChild(el('p', null,
            'Every layer derives from the public record: TxDOT crash files and traffic counts, county parcels ' +
            '(owner information removed), USDA soil surveys, NOAA rainfall, and OpenStreetMap. ' +
            'Estimates are labeled as estimates.'));
        body.appendChild(note);

        card.appendChild(body);

        const footer = el('div', 'mueny-help-footer');
        const done = el('button', null, 'Got it — show me the map');
        done.addEventListener('click', close);
        footer.appendChild(done);
        card.appendChild(footer);

        overlay.appendChild(card);
        document.body.appendChild(overlay);
        closeBtn.focus();
        document.addEventListener('keydown', escToClose);
    }

    function mountButton() {
        const header = document.getElementById('terravex-header');
        if (!header || document.getElementById('mueny-help-btn')) return;
        const btn = el('button', 'mueny-help-btn', '?');
        btn.id = 'mueny-help-btn';
        btn.title = 'How to use the map';
        btn.setAttribute('aria-label', 'Help and instructions');
        btn.addEventListener('click', open);
        header.appendChild(btn);
    }

    function init() {
        mountButton();
        let seen = null;
        try { seen = localStorage.getItem(SEEN_KEY); } catch (_) {}
        if (!seen) {
            // First visit: give the map a beat to appear, then offer the guide.
            setTimeout(open, 1200);
        }
    }

    window.MuenyHelp = { open };

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }
})();
