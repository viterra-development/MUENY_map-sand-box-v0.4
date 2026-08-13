/**
 * Parcel Trip Generation UI Components
 * - Color legend (bottom-left of map)
 * - Click/hover tooltip (floating card)
 * - Summary stats panel (collapsible, bottom-right)
 */

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
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(211,47,47)"></span> 200+</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(255,112,67)"></span> 50–199</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(255,183,77)"></span> 10–49</div>
        <div class="trip-legend-row"><span class="trip-swatch" style="background:rgb(65,182,196)"></span> 1–9</div>
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

export function showParcelTooltip(properties, x, y) {
    const container = document.getElementById('trip-panel');
    if (!container) return;

    const detail = ensureParcelDetail();
    const p = properties;
    // Field-name fallbacks accept both the current TxGIO output schema and any legacy variants.
    const addr = p.address || p.situs_addr || p.situs_street || 'No address';
    const landUse = p.ite_land_use || 'Unknown';
    const iteCode = p.ite_code || 'N/A';
    const daily = p.daily_trips || 0;
    const am = p.am_peak_trips || 0;
    const pm = p.pm_peak_trips || 0;
    const acresRaw = p.legal_acres ?? p.legal_acreage;
    const acres = (acresRaw != null && acresRaw !== '') ? acresRaw : 'N/A';
    const mktRaw = p.mkt_value ?? p.total_val;
    const mkt = mktRaw ? '$' + Number(mktRaw).toLocaleString() : 'N/A';
    const stateCd = p.state_cd || 'N/A';

    let tripColor = '#c8c8c8';
    if (daily >= 200) tripColor = '#d32f2f';
    else if (daily >= 50) tripColor = '#ff7043';
    else if (daily >= 10) tripColor = '#ffb74d';
    else if (daily > 0) tripColor = '#41b6c4';

    detail.innerHTML = `
        <div class="trip-tip-header">
            <div class="trip-tip-addr">${addr}</div>
            <button class="trip-detail-close" aria-label="Close">&times;</button>
        </div>
        <div class="trip-tip-body">
            <div class="trip-tip-row">
                <span class="trip-tip-label">Land Use</span>
                <span>${landUse} <span class="trip-tip-ite">(ITE ${iteCode})</span></span>
            </div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">CAD Code</span>
                <span>${stateCd}</span>
            </div>
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
            <div class="trip-tip-divider"></div>
            <div class="trip-tip-row">
                <span class="trip-tip-label">Acreage</span>
                <span>${acres}</span>
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

    // Sort land uses by trip count descending
    const sortedLU = Object.keys(landUseTrips).sort((a, b) => landUseTrips[b] - landUseTrips[a]);

    // Build breakdown rows (top 8)
    const breakdownRows = sortedLU.slice(0, 8).map(lu => {
        const pct = totalDaily > 0 ? ((landUseTrips[lu] / totalDaily) * 100).toFixed(1) : '0.0';
        return `
            <div class="trip-stats-breakdown-row">
                <span class="trip-stats-lu-name">${lu}</span>
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
