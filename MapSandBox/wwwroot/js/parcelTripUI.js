/**
 * Parcel Trip Generation UI Components
 * - Color legend (bottom-left of map)
 * - Click/hover tooltip (floating card)
 * - Summary stats panel (collapsible, bottom-right)
 */
import { parcelAcres, valuePerAcre, vpaBin, improvementShare, valuePerDailyTrip, aggregateFiscal, fmtMoney, fmtPct } from './fiscal.js';

// Escape data-derived strings before HTML interpolation. GeoJSON property
// values come from external pipelines (TIGER, CAD, TxDOT, SSURGO) and must
// never reach setHTML/innerHTML unescaped.
function escapeHtml(value) {
    return String(value ?? '')
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
}

// Normalize a CAD situs string: drop empty comma components and the
// trailing "TX 00000" filler; return null when nothing meaningful remains.
function cleanSitusAddress(raw) {
    if (!raw || typeof raw !== 'string') return null;
    let s = raw
        .replace(/,\s*(?=,|$)/g, '')                 // empty comma components
        .replace(/,?\s*TX\s*0*(\s|$)/i, ' ')       // ", TX 000000" filler
        .replace(/\s{2,}/g, ' ')
        .replace(/^[,\s]+|[,\s]+$/g, '')
        .trim();
    if (!s || /^0+$/.test(s) || /^\d+$/.test(s)) return null;   // bare/zero house number
    return s;
}

// ============================================
// 1. COLOR LEGEND
// ============================================
let legendElement = null;

export function createLegend() {
    if (legendElement) return;

    legendElement = document.createElement('div');
    legendElement.id = 'trip-legend';
    legendElement.innerHTML = `
        <div class="trip-legend-title">Daily Trips</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(23,77,88)"></span> 200+</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(66,125,128)"></span> 50–199</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(148,177,175)"></span> 10–49</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(204,203,195)"></span> 1–9</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(200,200,200)"></span> 0 / Unknown</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(34,139,34)"></span> City-Owned</div>
    `;
    const mapContainer = document.getElementById('map-wrapper')
        || document.querySelector('.map-section')
        || document.body;
    mapContainer.appendChild(legendElement);
}

export function removeLegend() {
    if (legendElement) {
        legendElement.remove();
        legendElement = null;
    }
}

// ============================================
// 2. PARCEL DETAIL (renders into #trip-panel, above stats)
// ============================================
let parcelDetailElement = null;

function ensureParcelDetail() {
    if (parcelDetailElement) return parcelDetailElement;
    parcelDetailElement = document.createElement('div');
    parcelDetailElement.id = 'trip-parcel-detail';
    return parcelDetailElement;
}

export function showParcelTooltip(properties, x, y, feature) {
    const container = document.getElementById('trip-panel');
    if (!container) return;

    const detail = ensureParcelDetail();
    const p = properties;
    // Field-name fallbacks accept both the current TxGIO output schema and any legacy variants.
    // CAD situs strings arrive as raw concatenations ("0   , , TX 000000") —
    // strip the empty components and fall back to the parcel id.
    const addr = cleanSitusAddress(p.address || p.situs_addr || p.situs_street)
        || (p.parcel_id ? 'Parcel ' + p.parcel_id : 'Unaddressed parcel');
    const landUse = p.ite_land_use || p.classification || 'Unclassified';
    const iteCode = p.ite_code || null;
    const daily = p.daily_trips || 0;
    const am = p.am_peak_trips || 0;
    const pm = p.pm_peak_trips || 0;
    // Acreage: CAD legal acreage when present, geodesic estimate from the
    // parcel polygon otherwise (fiscal.js caches this on the feature).
    const acresRaw = p.legal_acres ?? p.legal_acreage;
    const acreInfo = feature ? parcelAcres(feature)
        : { acres: (typeof acresRaw === 'number' && acresRaw > 0) ? acresRaw : null, estimated: false };
    const acres = acreInfo.acres ? acreInfo.acres.toFixed(2) + (acreInfo.estimated ? ' (est.)' : '') : '\u2014';
    const mktRaw = p.mkt_value ?? p.total_val;
    const mkt = (typeof mktRaw === 'number' && mktRaw > 0) ? '$' + Number(mktRaw).toLocaleString() : '\u2014';
    const stateCd = p.state_cd || 'N/A';

    // Fiscal snapshot — the live subset of the fiscal suite (NFYA/IPP/IBR
    // are pending external inputs; see fiscal.js).
    const vpa = feature ? valuePerAcre(feature) : null;
    const vpaColor = vpaBin(vpa).css;
    const imprvShare = improvementShare(p);
    const valPerTrip = valuePerDailyTrip(p);
    const fiscalHtml = (vpa !== null || imprvShare !== null || valPerTrip !== null) ? `
            <div class="trip-tip-divider"></div>
            <div class="trip-tip-section-label">Fiscal snapshot <span class="trip-tip-beta">beta</span></div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Value per acre</span>
                <span><span class="fiscal-chip" style="background:${vpaColor}"></span>${vpa !== null ? escapeHtml(fmtMoney(vpa, { compact: true }) + ' /ac') : '—'}</span>
            </div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Improvement share</span>
                <span>${escapeHtml(fmtPct(imprvShare))}</span>
            </div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Value per daily trip</span>
                <span>${valPerTrip !== null ? escapeHtml(fmtMoney(valPerTrip, { compact: true })) : '—'}</span>
            </div>
            <div class="trip-tip-fine">From CAD market value, parcel geometry &amp; ITE trip estimates. Net fiscal yield &amp; payback — coming soon.</div>` : '';

    // Ground conditions (joined from USDA SSURGO at build time)
    const soilName = p.soil_muname || null;
    const clayPct = (typeof p.soil_clay_pct === 'number') ? p.soil_clay_pct.toFixed(1) + '%' : null;
    const ksat = (typeof p.soil_ksat_um_per_s === 'number') ? p.soil_ksat_um_per_s : null;
    const drainage = ksat === null ? null
        : ksat > 10 ? { label: 'Drains well', color: '#2a7a3b' }
        : ksat > 5  ? { label: 'Moderate drainage', color: '#5c8a3a' }
        : ksat > 1  ? { label: 'Fair drainage', color: '#b07a26' }
        : ksat > 0.1 ? { label: 'Poor drainage', color: '#c05238' }
        : { label: 'Very poor drainage', color: '#b3362b' };
    const groundHtml = soilName ? `
            <div class="trip-tip-divider"></div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Soil</span>
                <span>${escapeHtml(soilName)}</span>
            </div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Clay content</span>
                <span>${escapeHtml(clayPct || 'N/A')}</span>
            </div>
            ${drainage ? `<div class="trip-tip-row">
                <span class="trip-tip-label">Drainage</span>
                <span style="color:${drainage.color};font-weight:600;">${escapeHtml(drainage.label)}</span>
            </div>` : ''}` : '';

    let tripColor = '#c8c8c8';
    if (daily >= 200) tripColor = '#063542';
    else if (daily >= 50) tripColor = '#1e6a71';
    else if (daily >= 10) tripColor = '#76a5a1';
    else if (daily > 0) tripColor = '#d1cfc6';

    detail.innerHTML = `
        <div class="trip-tip-header">
            <div class="trip-tip-addr">${escapeHtml(addr)}</div>
            <button class="trip-detail-close" aria-label="Close">&times;</button>
        </div>
        <div class="trip-tip-body">
            <div class="trip-tip-row">
                <span class="trip-tip-label">Land Use</span>
                <span>${escapeHtml(landUse)}${iteCode ? ` <span class="trip-tip-ite">(ITE ${escapeHtml(iteCode)})</span>` : ''}</span>
            </div>
            ${(p.state_cd || '').trim() ? `<div class="trip-tip-row">
                <span class="trip-tip-label">CAD Code</span>
                <span>${escapeHtml(p.state_cd)}</span>
            </div>` : ''}
            <div class="trip-tip-divider"></div>
            <div class="trip-tip-trips">
                <div class="trip-tip-daily" style="border-left: 4px solid ${tripColor}">
                    <span class="trip-tip-big">${daily}</span>
                    <span class="trip-tip-small">daily trips</span>
                </div>
                <div class="trip-tip-peaks">
                    <div><span class="trip-tip-peak-val">${am}</span> <span class="trip-tip-small">AM peak</span></div>
                    <div><span class="trip-tip-peak-val">${pm}</span> <span class="trip-tip-small">PM peak</span></div>
                </div>
            </div>
            ${groundHtml}
            ${fiscalHtml}
            <div class="trip-tip-divider"></div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Acreage</span>
                <span>${escapeHtml(acres)}</span>
            </div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Market Value</span>
                <span>${mkt}</span>
            </div>
        </div>
    `;

    // Insert at the top of the panel (before stats)
    if (!detail.parentElement) {
        container.insertBefore(detail, container.firstChild);
    }
    detail.style.display = 'block';

    // Close button
    detail.querySelector('.trip-detail-close').addEventListener('click', () => {
        hideParcelTooltip();
    });

    // Scroll panel to top to show the detail
    container.scrollTop = 0;
}

export function hideParcelTooltip() {
    if (parcelDetailElement) {
        parcelDetailElement.style.display = 'none';
    }
}

export function markTooltipShown() {
    // No-op — kept for API compatibility
}

// ============================================
// 3. SUMMARY STATS PANEL (renders into #trip-panel right panel)
// ============================================
let statsElement = null;

export function createStatsPanel(geojsonData) {
    const container = document.getElementById('trip-panel');
    if (!container) {
        console.warn('[parcelTripUI] #trip-panel container not found');
        return;
    }

    // Compute summary stats from the GeoJSON
    const features = geojsonData.features || [];
    const totalParcels = features.length;
    let totalDaily = 0;
    let totalAM = 0;
    let totalPM = 0;
    const landUseCounts = {};
    const landUseTrips = {};

    features.forEach(f => {
        const p = f.properties || {};
        const daily = p.daily_trips || 0;
        const am = p.am_peak_trips || 0;
        const pm = p.pm_peak_trips || 0;
        const lu = p.ite_land_use || 'Unknown';

        totalDaily += daily;
        totalAM += am;
        totalPM += pm;

        if (!landUseCounts[lu]) {
            landUseCounts[lu] = 0;
            landUseTrips[lu] = 0;
        }
        landUseCounts[lu]++;
        landUseTrips[lu] += daily;
    });

    // City-level fiscal rollup (null when no parcel has usable value + area)
    const fiscal = aggregateFiscal(features);
    const fiscalRows = fiscal ? `
            <div class="trip-stats-divider"></div>
            <div class="trip-stats-section-title">Fiscal Snapshot <span class="trip-tip-beta">beta</span></div>
            <div class="trip-stats-fiscal">
                <div class="trip-stats-fiscal-row">
                    <span>Taxable value</span>
                    <b>${escapeHtml(fmtMoney(fiscal.totalValue, { compact: true }))}</b>
                </div>
                <div class="trip-stats-fiscal-row">
                    <span>Value per acre</span>
                    <b>${escapeHtml(fmtMoney(fiscal.valuePerAcre, { compact: true }))} /ac</b>
                </div>
                <div class="trip-stats-fiscal-row">
                    <span>Improvement share</span>
                    <b>${escapeHtml(fmtPct(fiscal.improvementShare))}</b>
                </div>
                <div class="trip-stats-fiscal-row">
                    <span>Value per daily trip</span>
                    <b>${fiscal.valuePerDailyTrip !== null ? escapeHtml(fmtMoney(fiscal.valuePerDailyTrip, { compact: true })) : '—'}</b>
                </div>
                <div class="trip-tip-fine">${fiscal.parcelCount.toLocaleString()} parcels with CAD value &amp; area. Net fiscal yield &amp; infrastructure payback — coming soon.</div>
            </div>` : '';

    // Sort land uses by trip count descending
    const sortedLU = Object.keys(landUseTrips).sort((a, b) => landUseTrips[b] - landUseTrips[a]);

    // Build breakdown rows (top 8)
    const breakdownRows = sortedLU.slice(0, 8).map(lu => {
        const pct = totalDaily > 0 ? ((landUseTrips[lu] / totalDaily) * 100).toFixed(1) : '0.0';
        return `
            <div class="trip-stats-breakdown-row">
                <span class="trip-stats-lu-name">${escapeHtml(lu)}</span>
                <span class="trip-stats-lu-count">${landUseCounts[lu]} parcels</span>
                <span class="trip-stats-lu-trips">${Math.round(landUseTrips[lu]).toLocaleString()} trips</span>
                <span class="trip-stats-lu-pct">${pct}%</span>
            </div>`;
    }).join('');

    statsElement = document.createElement('div');
    statsElement.id = 'trip-stats-panel';
    statsElement.innerHTML = `
        <div class="trip-stats-header">
            <span>Trip Generation Summary</span>
        </div>
        <div class="trip-stats-content">
            <div class="trip-stats-totals">
                <div class="trip-stats-total-item">
                    <div class="trip-stats-total-val">${totalParcels.toLocaleString()}</div>
                    <div class="trip-stats-total-label">Parcels</div>
                </div>
                <div class="trip-stats-total-item">
                    <div class="trip-stats-total-val">${Math.round(totalDaily).toLocaleString()}</div>
                    <div class="trip-stats-total-label">Daily Trips</div>
                </div>
                <div class="trip-stats-total-item">
                    <div class="trip-stats-total-val">${Math.round(totalAM).toLocaleString()}</div>
                    <div class="trip-stats-total-label">AM Peak</div>
                </div>
                <div class="trip-stats-total-item">
                    <div class="trip-stats-total-val">${Math.round(totalPM).toLocaleString()}</div>
                    <div class="trip-stats-total-label">PM Peak</div>
                </div>
            </div>
            ${fiscalRows}
            <div class="trip-stats-divider"></div>
            <div class="trip-stats-section-title">Breakdown by Land Use</div>
            <div class="trip-stats-breakdown">
                ${breakdownRows}
            </div>
        </div>
    `;

    container.innerHTML = '';
    container.appendChild(statsElement);
    container.classList.add('trip-panel-visible');
}

export function removeStatsPanel() {
    const container = document.getElementById('trip-panel');
    if (container) {
        container.classList.remove('trip-panel-visible');
        container.classList.remove('mobile-open');
        container.innerHTML = '';
    }
    statsElement = null;
}
