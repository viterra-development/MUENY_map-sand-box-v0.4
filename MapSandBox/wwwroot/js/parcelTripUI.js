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
    `;
    document.body.appendChild(legendElement);
}

export function removeLegend() {
    if (legendElement) {
        legendElement.remove();
        legendElement = null;
    }
}

// ============================================
// 2. TOOLTIP (replaces alert popup)
// ============================================
let tooltipElement = null;

function ensureTooltip() {
    if (tooltipElement) return tooltipElement;
    tooltipElement = document.createElement('div');
    tooltipElement.id = 'trip-tooltip';
    tooltipElement.style.display = 'none';
    document.body.appendChild(tooltipElement);
    return tooltipElement;
}

export function showParcelTooltip(properties, x, y) {
    const tip = ensureTooltip();
    const p = properties;
    const addr = p.address || 'No address';
    const landUse = p.ite_land_use || 'Unknown';
    const iteCode = p.ite_code || 'N/A';
    const daily = p.daily_trips || 0;
    const am = p.am_peak_trips || 0;
    const pm = p.pm_peak_trips || 0;
    const acres = p.legal_acres || 'N/A';
    const mkt = p.mkt_value ? '$' + Number(p.mkt_value).toLocaleString() : 'N/A';
    const owner = p.owner || 'Unknown';
    const stateCd = p.state_cd || 'N/A';

    // Color badge based on trip count
    let tripColor = '#c8c8c8';
    if (daily >= 200) tripColor = '#d32f2f';
    else if (daily >= 50) tripColor = '#ff7043';
    else if (daily >= 10) tripColor = '#ffb74d';
    else if (daily > 0) tripColor = '#41b6c4';

    tip.innerHTML = `
        <div class="trip-tip-header">
            <div class="trip-tip-addr">${addr}</div>
            <div class="trip-tip-owner">${owner}</div>
        </div>
        <div class="trip-tip-body">
            <div class="trip-tip-row">
                <span class="trip-tip-label">Land Use</span>
                <span>${landUse} <span style="color:#888">(ITE ${iteCode})</span></span>
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

    // Position tooltip near click, but keep it on screen
    const pad = 16;
    let left = x + pad;
    let top = y + pad;
    tip.style.display = 'block';

    // Adjust if it would go offscreen
    const rect = tip.getBoundingClientRect();
    if (left + 320 > window.innerWidth) left = x - 320 - pad;
    if (top + rect.height > window.innerHeight) top = y - rect.height - pad;

    tip.style.left = left + 'px';
    tip.style.top = top + 'px';
}

export function hideParcelTooltip() {
    if (tooltipElement) {
        tooltipElement.style.display = 'none';
    }
}

// Close tooltip on map click elsewhere
document.addEventListener('click', (e) => {
    if (tooltipElement && !tooltipElement.contains(e.target)) {
        hideParcelTooltip();
    }
});

// ============================================
// 3. SUMMARY STATS PANEL
// ============================================
let statsElement = null;
let statsCollapsed = false;

export function createStatsPanel(geojsonData) {
    if (statsElement) statsElement.remove();

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
        <div class="trip-stats-header" id="trip-stats-toggle">
            <span>Trip Generation Summary</span>
            <span class="trip-stats-arrow">▼</span>
        </div>
        <div class="trip-stats-content" id="trip-stats-content">
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
    document.body.appendChild(statsElement);

    // Toggle collapse
    document.getElementById('trip-stats-toggle').addEventListener('click', () => {
        statsCollapsed = !statsCollapsed;
        const content = document.getElementById('trip-stats-content');
        const arrow = statsElement.querySelector('.trip-stats-arrow');
        content.style.display = statsCollapsed ? 'none' : 'block';
        arrow.textContent = statsCollapsed ? '▲' : '▼';
    });
}

export function removeStatsPanel() {
    if (statsElement) {
        statsElement.remove();
        statsElement = null;
    }
}
