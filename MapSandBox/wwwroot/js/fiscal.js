// MUENY fiscal metrics — the "does growth pay for itself?" layer family.
//
// Live today (computable entirely from shipped parcel GeoJSON):
//   - Taxable value per acre  (CAD market value ÷ parcel acres)
//   - Improvement share        (improvement value ÷ total value)
//   - Value per daily trip     (total value ÷ ITE daily trips)
//
// Coming soon (blocked on data we don't ship yet — see showComingSoonModal):
//   - NFYA  Net Fiscal Yield per Acre      (needs city tax rates + O&M budgets)
//   - IPP   Infrastructure Payback Period  (needs capital cost inputs)
//   - IBR   Impervious Burden Ratio        (needs impervious cover; CAD
//           imprv_sqft is 0 for all 28,585 shipped parcels, so the roof-print
//           proxy can't run until the Parker CAD bulk request lands)
//
// ES module imported by maplibre-deckgl-integration.js and parcelTripUI.js;
// also registers window.MuenyFiscal for Blazor JS interop and plain scripts.

const SQM_PER_ACRE = 4046.8564224;
const EARTH_RADIUS_M = 6378137;

// ---------------------------------------------------------------------------
// Parcel area — legal_acreage when CAD supplies it (>0), otherwise geodesic
// area from the polygon rings (the ~9,300 parcels where CAD acreage is 0).
// ---------------------------------------------------------------------------
function ringAreaSqm(ring) {
    if (!Array.isArray(ring) || ring.length < 3) return 0;
    let sum = 0;
    for (let i = 0; i < ring.length; i++) {
        const [lon1, lat1] = ring[i];
        const [lon2, lat2] = ring[(i + 1) % ring.length];
        sum += (lon2 - lon1) * (Math.PI / 180) *
            (2 + Math.sin(lat1 * Math.PI / 180) + Math.sin(lat2 * Math.PI / 180));
    }
    return Math.abs(sum * EARTH_RADIUS_M * EARTH_RADIUS_M / 2);
}

function polygonAreaSqm(coordinates) {
    // Outer ring minus holes; clamp so a bad hole ring can't go negative.
    let area = ringAreaSqm(coordinates[0] || []);
    for (let i = 1; i < coordinates.length; i++) area -= ringAreaSqm(coordinates[i]);
    return Math.max(0, area);
}

export function geometryAcres(geometry) {
    if (!geometry || !geometry.coordinates) return null;
    let sqm = 0;
    if (geometry.type === 'Polygon') sqm = polygonAreaSqm(geometry.coordinates);
    else if (geometry.type === 'MultiPolygon') {
        for (const poly of geometry.coordinates) sqm += polygonAreaSqm(poly);
    } else return null;
    const acres = sqm / SQM_PER_ACRE;
    return acres > 0 ? acres : null;
}

// Returns { acres, estimated } — estimated=true when derived from geometry.
// Caches on the feature (deck.gl accessors run once per feature per layer
// rebuild; 28k spherical-area computations per toggle is wasteful).
export function parcelAcres(feature) {
    if (!feature) return { acres: null, estimated: false };
    if (feature.__muenyAcres !== undefined) return feature.__muenyAcres;
    const p = feature.properties || {};
    const legal = p.legal_acres ?? p.legal_acreage;
    let result;
    if (typeof legal === 'number' && legal > 0) {
        result = { acres: legal, estimated: false };
    } else {
        result = { acres: geometryAcres(feature.geometry), estimated: true };
    }
    feature.__muenyAcres = result;
    return result;
}

// ---------------------------------------------------------------------------
// Metrics
// ---------------------------------------------------------------------------
export function valuePerAcre(feature) {
    const p = (feature && feature.properties) || {};
    const val = p.mkt_value ?? p.total_val;
    if (typeof val !== 'number' || val <= 0) return null;
    const { acres } = parcelAcres(feature);
    if (!acres) return null;
    return val / acres;
}

export function improvementShare(properties) {
    const p = properties || {};
    const total = p.mkt_value ?? p.total_val;
    const imprv = p.imprv_val;
    if (typeof total !== 'number' || total <= 0) return null;
    if (typeof imprv !== 'number' || imprv < 0) return null;
    return Math.min(1, imprv / total);
}

export function valuePerDailyTrip(properties) {
    const p = properties || {};
    const total = p.mkt_value ?? p.total_val;
    const trips = p.daily_trips;
    if (typeof total !== 'number' || total <= 0) return null;
    if (typeof trips !== 'number' || trips <= 0) return null;
    return total / trips;
}

// ---------------------------------------------------------------------------
// Value-per-acre color ramp — plum sequential, deliberately distinct from the
// petrol trip ramp, the soil browns, and the reds reserved for crash risk.
// Bin edges tuned to Parker County: vacant land sits under $50k/ac, typical
// single-family lands $250k–$1M/ac, dense commercial exceeds $3M/ac.
// ---------------------------------------------------------------------------
export const VPA_BINS = [
    { min: 3000000, rgba: [58, 28, 84, 245],   css: 'rgb(58,28,84)',    label: '$3M+ / acre' },
    { min: 1000000, rgba: [96, 60, 130, 235],  css: 'rgb(96,60,130)',   label: '$1M–3M' },
    { min: 250000,  rgba: [141, 110, 170, 220], css: 'rgb(141,110,170)', label: '$250k–1M' },
    { min: 50000,   rgba: [186, 168, 205, 190], css: 'rgb(186,168,205)', label: '$50k–250k' },
    { min: 0,       rgba: [226, 222, 232, 130], css: 'rgb(226,222,232)', label: 'Under $50k' },
];
export const VPA_NO_DATA = { rgba: [232, 230, 224, 45], css: 'rgb(232,230,224)', label: 'No value data' };

export function vpaBin(vpa) {
    if (vpa === null || vpa === undefined) return VPA_NO_DATA;
    for (const bin of VPA_BINS) {
        if (vpa >= bin.min) return bin;
    }
    return VPA_NO_DATA;
}

// deck.gl fill accessor for *-parcels-trips layers in fiscal color mode.
export function vpaFillColor(feature) {
    if (feature && feature.properties && feature.properties.city_owned === true) {
        return [34, 139, 34, 200]; // keep the city-owned green: exempt, not productive
    }
    return vpaBin(valuePerAcre(feature)).rgba;
}

// ---------------------------------------------------------------------------
// City-level aggregates for the stats panel.
// ---------------------------------------------------------------------------
export function aggregateFiscal(features) {
    let totalVal = 0, totalImprv = 0, totalAcres = 0, valued = 0, totalTrips = 0;
    for (const f of features || []) {
        const p = f.properties || {};
        const val = p.mkt_value ?? p.total_val;
        if (typeof val !== 'number' || val <= 0) continue;
        const { acres } = parcelAcres(f);
        if (!acres) continue;
        valued++;
        totalVal += val;
        totalAcres += acres;
        if (typeof p.imprv_val === 'number' && p.imprv_val > 0) totalImprv += Math.min(p.imprv_val, val);
        if (typeof p.daily_trips === 'number' && p.daily_trips > 0) totalTrips += p.daily_trips;
    }
    if (!valued || totalAcres <= 0) return null;
    return {
        parcelCount: valued,
        totalValue: totalVal,
        valuePerAcre: totalVal / totalAcres,
        improvementShare: totalVal > 0 ? totalImprv / totalVal : null,
        valuePerDailyTrip: totalTrips > 0 ? totalVal / totalTrips : null,
    };
}

// ---------------------------------------------------------------------------
// Formatting helpers shared by popup + stats panel.
// ---------------------------------------------------------------------------
export function fmtMoney(v, opts) {
    if (v === null || v === undefined) return '—';
    const compact = opts && opts.compact;
    if (compact && v >= 1000000000) return '$' + (v / 1000000000).toFixed(v >= 10000000000 ? 0 : 2) + 'B';
    if (compact && v >= 1000000) return '$' + (v / 1000000).toFixed(v >= 10000000 ? 0 : 1) + 'M';
    if (compact && v >= 10000) return '$' + Math.round(v / 1000) + 'k';
    return '$' + Math.round(v).toLocaleString();
}
export function fmtPct(v) {
    if (v === null || v === undefined) return '—';
    return Math.round(v * 100) + '%';
}

// ---------------------------------------------------------------------------
// Parcel color mode — 'trips' (default) or 'value-acre'. State lives here so
// the deck.gl accessor branch, the map key, and the Blazor sidebar all agree.
// Blazor sets the mode then re-issues UpdateLayers, which rebuilds the layers
// and refreshes the key.
// ---------------------------------------------------------------------------
let parcelColorMode = 'trips';
export function getParcelColorMode() { return parcelColorMode; }
export function setParcelColorMode(mode) {
    parcelColorMode = (mode === 'value-acre') ? 'value-acre' : 'trips';
    return parcelColorMode;
}

// ---------------------------------------------------------------------------
// Coming-soon modal — the roadmap window for the rest of the fiscal suite.
// Reuses the .report-overlay / .report-modal styling from app.css.
// ---------------------------------------------------------------------------
const COMING_SOON = [
    {
        name: 'Net Fiscal Yield per Acre',
        abbr: 'NFYA',
        desc: 'Annual city tax revenue minus the infrastructure cost each parcel consumes, per acre. The "does this land pay for itself?" map.',
        needs: 'Needs city tax rates and street / utility O&M budgets.',
    },
    {
        name: 'Infrastructure Payback Period',
        abbr: 'IPP',
        desc: 'Years until public infrastructure spending on a development is repaid by its net fiscal contribution — with optimistic / base / conservative bands.',
        needs: 'Needs capital cost inputs from city CIPs and bid tabs.',
    },
    {
        name: 'Impervious Burden Ratio',
        abbr: 'IBR',
        desc: 'Impervious surface bought per $1M of taxable value — the bridge between land-use form, stormwater load, and maintenance burden.',
        needs: 'Needs building footprints or impervious cover (Parker CAD bulk request / NLCD).',
    },
];

export function showComingSoonModal() {
    if (document.getElementById('fiscal-soon-overlay')) return;

    const overlay = document.createElement('div');
    overlay.className = 'report-overlay';
    overlay.id = 'fiscal-soon-overlay';

    const items = COMING_SOON.map(m => `
        <div class="fiscal-soon-item">
            <div class="fiscal-soon-name">${m.name} <span class="fiscal-soon-abbr">${m.abbr}</span></div>
            <div class="fiscal-soon-desc">${m.desc}</div>
            <div class="fiscal-soon-needs">${m.needs}</div>
        </div>`).join('');

    overlay.innerHTML = `
      <div class="report-modal fiscal-soon-modal">
        <h3>Fiscal Suite — Coming Soon</h3>
        <p>Taxable value per acre is live on the map today. Three deeper metrics are in the pipeline:</p>
        <div class="fiscal-soon-list">${items}</div>
        <button class="report-cancel" id="fiscal-soon-close">Close</button>
      </div>
    `;
    document.body.appendChild(overlay);

    const close = () => overlay.remove();
    document.getElementById('fiscal-soon-close').addEventListener('click', close);
    overlay.addEventListener('click', (e) => { if (e.target === overlay) close(); });
    const onEsc = (e) => {
        if (e.key === 'Escape') { close(); window.removeEventListener('keydown', onEsc); }
    };
    window.addEventListener('keydown', onEsc);
}

// Global exposure for Blazor JS interop (sidebar controls) and plain scripts
// (map-key.js reads the color mode when rendering the parcel section).
window.MuenyFiscal = {
    getParcelColorMode,
    setParcelColorMode,
    showComingSoonModal,
    valuePerAcre,
    vpaBin,
    VPA_BINS,
    VPA_NO_DATA,
};
