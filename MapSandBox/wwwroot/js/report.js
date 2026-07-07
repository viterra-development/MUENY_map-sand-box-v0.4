// MUENY Report Generation
// Modal-driven PDF/print report — reads current stats from the DOM if present,
// otherwise fetches fresh from the underlying geojson files. Callable from both
// the JS fallback panel (index.html) and the Blazor UI (via JS interop).

const CITY_STAT_SOURCES = {
    // Which parcel file the active city trip-gen layer maps to. Extendable.
    'wp-parcels-trips':          { name: 'Willow Park',    url: '/willow-park-parcels-with-trips.geojson' },
    'aledo-parcels-trips':       { name: 'Aledo',          url: '/aledo-parcels-with-trips.geojson' },
    'azle-parcels-trips':        { name: 'Azle',           url: '/azle-parcels-with-trips.geojson' },
    'midlothian-parcels-trips':  { name: 'Midlothian',     url: '/midlothian-parcels-with-trips.geojson' },
    'mineral-wells-parcels-trips': { name: 'Mineral Wells', url: '/mineral-wells-parcels-with-trips.geojson' },
    'reno-parcels-trips':        { name: 'Reno',           url: '/reno-parcels-with-trips.geojson' },
    'springtown-parcels-trips':  { name: 'Springtown',     url: '/springtown-parcels-with-trips.geojson' },
    'weatherford-parcels-trips': { name: 'Weatherford',    url: '/weatherford-parcels-with-trips.geojson' },
};

function detectActiveCity() {
    // Prefer the trip-stats panel's tracked layer id (set by maplibre-deckgl-integration.js).
    const panel = document.getElementById('trip-stats-panel');
    if (panel && panel.dataset && panel.dataset.layerId && CITY_STAT_SOURCES[panel.dataset.layerId]) {
        return CITY_STAT_SOURCES[panel.dataset.layerId];
    }
    // Fallback: Willow Park (matches historical default when only WP was wired).
    return CITY_STAT_SOURCES['wp-parcels-trips'];
}

async function readStat(id, fallback) {
    const el = document.getElementById(id);
    if (el && el.textContent && el.textContent !== '--') return el.textContent;
    return fallback();
}

async function fetchStats(city) {
    const stats = { crashes: '--', parcels: '--', trips: '--', roads: '--' };

    stats.crashes = await readStat('stat-crashes', async () => {
        try {
            const r = await fetch('/cris-data/parker-county-crashes-clustered-deckgl.json');
            if (!r.ok) return '--';
            const d = await r.json();
            const count = d.features ? d.features.length : (Array.isArray(d) ? d.length : '--');
            return count.toLocaleString();
        } catch { return '--'; }
    });

    stats.roads = await readStat('stat-roads', async () => {
        try {
            const r = await fetch('/parker-roads-with-traffic-phase1.geojson');
            if (!r.ok) return '--';
            const d = await r.json();
            const features = d.features || [];
            const withAadt = features.filter(f => f.properties?.aadt > 0 || f.properties?.AADT > 0).length;
            return withAadt.toLocaleString();
        } catch { return '--'; }
    });

    // Parcel + trip stats are city-scoped
    const parcelsRaw = await readStat('stat-parcels', () => Promise.resolve(null));
    const tripsRaw = await readStat('stat-trips', () => Promise.resolve(null));
    if (parcelsRaw && tripsRaw) {
        stats.parcels = parcelsRaw;
        stats.trips = tripsRaw;
    } else {
        try {
            const r = await fetch(city.url);
            if (r.ok) {
                const d = await r.json();
                const features = d.features || [];
                stats.parcels = features.length.toLocaleString();
                const totalTrips = features.reduce((sum, f) => sum + (f.properties?.daily_trips || 0), 0);
                stats.trips = Math.round(totalTrips).toLocaleString();
            }
        } catch { /* keep -- */ }
    }
    return stats;
}

function captureMapImage() {
    try {
        // deck.gl overlay canvas OR MapLibre canvas — try both, prefer the visible one.
        const canvas = document.querySelector('#maplibre-container canvas')
                    || document.querySelector('#map-wrapper canvas')
                    || document.querySelector('.maplibregl-canvas');
        return canvas ? canvas.toDataURL('image/png') : null;
    } catch (e) {
        console.warn('[report] Could not capture map screenshot:', e);
        return null;
    }
}

function buildReportHtml(reportType, city, stats, mapImageDataUrl) {
    const titles = {
        safety:  `${city.name} — Safety Summary`,
        traffic: `${city.name} — Traffic Estimation`,
        full:    `${city.name} — Municipal Intelligence Summary`
    };
    const title = titles[reportType] || `${city.name} — MUENY Report`;
    const date = new Date().toLocaleDateString('en-US', { year: 'numeric', month: 'long', day: 'numeric' });

    return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<title>${title}</title>
<style>
  body { font-family: 'Helvetica Neue', Arial, sans-serif; margin: 0; padding: 32px; background: #fff; color: #1a1a2e; }
  .report-header { border-bottom: 3px solid #2E4A6B; padding-bottom: 16px; margin-bottom: 24px; display: flex; justify-content: space-between; align-items: flex-end; }
  .report-brand { font-size: 24px; font-weight: 700; letter-spacing: 0.15em; color: #0B1F2E; }
  .report-brand span { color: #2E4A6B; }
  .report-meta { font-size: 11px; color: #888; text-align: right; }
  .report-title { font-size: 18px; font-weight: 700; color: #0B1F2E; margin-bottom: 4px; }
  .report-subtitle { font-size: 12px; color: #666; }
  .stats-row { display: flex; gap: 16px; margin: 24px 0; }
  .stat-box { flex: 1; background: #f0f4f8; border-radius: 6px; padding: 16px; text-align: center; }
  .stat-box .val { font-size: 28px; font-weight: 700; color: #2E4A6B; }
  .stat-box .lbl { font-size: 11px; color: #888; text-transform: uppercase; letter-spacing: 0.08em; margin-top: 4px; }
  .map-section { margin: 24px 0; }
  .map-section h3 { font-size: 13px; font-weight: 600; text-transform: uppercase; letter-spacing: 0.1em; color: #444; margin-bottom: 12px; }
  .map-img { width: 100%; border-radius: 6px; border: 1px solid #ddd; }
  .notes { margin-top: 24px; padding: 16px; background: #f8f9fa; border-radius: 6px; font-size: 11px; color: #666; line-height: 1.6; }
  .footer { margin-top: 32px; padding-top: 16px; border-top: 1px solid #ddd; font-size: 10px; color: #aaa; display: flex; justify-content: space-between; }
</style>
</head>
<body>
  <div class="report-header">
    <div>
      <div class="report-brand">MUENY <span>Municipal Intelligence</span></div>
      <div style="font-size:11px; color:#888; margin-top:4px;">Viterra Group</div>
    </div>
    <div class="report-meta">
      <div class="report-title">${title}</div>
      <div>Generated: ${date}</div>
    </div>
  </div>

  <div class="stats-row">
    <div class="stat-box"><div class="val">${stats.crashes}</div><div class="lbl">Crashes (5yr)</div></div>
    <div class="stat-box"><div class="val">${stats.parcels}</div><div class="lbl">Parcels Analyzed</div></div>
    <div class="stat-box"><div class="val">${stats.trips}</div><div class="lbl">Est. Daily Trips</div></div>
    <div class="stat-box"><div class="val">${stats.roads}</div><div class="lbl">Roads w/ AADT</div></div>
  </div>

  ${mapImageDataUrl ? `<div class="map-section"><h3>Map View — ${city.name}</h3><img class="map-img" src="${mapImageDataUrl}" /></div>` : ''}

  <div class="notes">
    <strong>Data Sources:</strong> TxDOT CRIS crash records · Parker County Appraisal District parcel data · ITE Trip Generation Manual (11th Edition) · TxDOT AADT traffic counts · TIGER/Line road network<br><br>
    <strong>Methodology:</strong> Trip generation uses ITE land use codes mapped from Parker County CAD state property type codes. Crash risk segments derived from CRIS crash point spatial analysis. Traffic volumes estimated via IDW interpolation from observed TxDOT counts.<br><br>
    <em>This report is a screening tool, not a final engineering assessment. Data is provided for planning and grant support purposes. Field verification recommended for engineering decisions.</em>
  </div>

  <div class="footer">
    <div>MUENY Municipal Intelligence Platform · Viterra Group</div>
    <div>${date}</div>
  </div>
</body>
</html>`;
}

export async function generateReport(reportType) {
    const city = detectActiveCity();
    const stats = await fetchStats(city);
    const mapImageDataUrl = captureMapImage();
    const html = buildReportHtml(reportType, city, stats, mapImageDataUrl);

    const reportWindow = window.open('', '_blank', 'width=900,height=700');
    if (!reportWindow) {
        console.warn('[report] Popup blocked. Enable popups for this site.');
        alert('Report window was blocked. Please allow popups for this site and try again.');
        return;
    }
    reportWindow.document.write(html);
    reportWindow.document.close();
    reportWindow.onload = () => {
        setTimeout(() => reportWindow.print(), 500);
    };
}

export function showReportModal() {
    // Avoid duplicates if user clicks twice quickly
    if (document.getElementById('report-overlay')) return;

    const overlay = document.createElement('div');
    overlay.className = 'report-overlay';
    overlay.id = 'report-overlay';
    overlay.innerHTML = `
      <div class="report-modal">
        <h3>Complete Regional Reports</h3>
        <p style="font-size: 14px; color: rgba(255,255,255,0.75); margin: 12px 0 24px 0;">Coming Soon</p>
        <button class="report-cancel" id="report-cancel-btn">Close</button>
      </div>
    `;
    document.body.appendChild(overlay);

    const closeModal = () => overlay.remove();
    document.getElementById('report-cancel-btn').addEventListener('click', closeModal);
    // Escape closes the modal
    const onEsc = (e) => {
        if (e.key === 'Escape') { closeModal(); window.removeEventListener('keydown', onEsc); }
    };
    window.addEventListener('keydown', onEsc);
    // Click on the scrim (outside the modal) closes
    overlay.addEventListener('click', (e) => {
        if (e.target === overlay) closeModal();
    });
}

// Legacy global exposure — index.html JS fallback panel + Blazor JS interop
// both call this via window.MuenyReport.showReportModal().
window.MuenyReport = { showReportModal, generateReport };
