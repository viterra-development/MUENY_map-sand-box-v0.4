// JS fallback bootstrap (extracted from index.html so the CSP can forbid inline scripts).
import './report.js';   // registers window.MuenyReport = { showReportModal, generateReport }

const BLAZOR_TIMEOUT_MS = 6000;
let blazorStarted = false;
let mapInitialized = false;

// Layer config mirrored from MapLibreService.cs
const defaultConfig = {
  latitude: 32.7365,
  longitude: -97.6506,
  zoom: 13,
  bearing: 0,
  pitch: 0,
  baseMap: {
    style: 'https://basemaps.cartocdn.com/gl/voyager-gl-style/style.json',
    showControls: true,
    showAttribution: true,
    name: 'Voyager'
  },
  layers: [
    { id: 'parker-roads-base', type: 'GeoJson', dataUrl: '/parker-county-roads.geojson', visible: false,
      properties: { stroked: true, filled: false, getLineColor: [120,120,120,128], getLineWidth: 1, lineWidthMinPixels: 1, opacity: 0.6, pickable: true, onClick: 'handleRoadClick' }},
    { id: 'parker-roads-traffic', type: 'Path', dataUrl: '/parker-roads-with-traffic.geojson', visible: false,
      properties: { getPath: 'getCoordinates', getColor: 'getTrafficGradientColor', getWidth: 'getTrafficWidth', widthMinPixels: 1, widthMaxPixels: 6, capRounded: true, jointRounded: true, opacity: 0.9, pickable: true, autoHighlight: true, onClick: 'handleTrafficRoadClick' }},
    { id: 'parker-roads-traffic-phase1', type: 'Path', dataUrl: '/parker-roads-with-traffic-phase1.geojson', visible: false,
      properties: { getPath: 'getCoordinates', getColor: 'getTrafficGradientColor', getWidth: 'getTrafficWidth', widthMinPixels: 1, widthMaxPixels: 6, capRounded: true, jointRounded: true, opacity: 0.9, pickable: true, autoHighlight: true, onClick: 'handleTrafficRoadClick' }},
    { id: 'cris-risk-segments', type: 'PathLayer', dataUrl: '/cris-data/parker-county-risk-segments-traffic-deckgl.json', visible: true,
      properties: { widthMinPixels: 1, widthMaxPixels: 8 }},
    { id: 'cris-crashes', type: 'ScatterplotLayer', dataUrl: '/cris-data/parker-county-crashes-clustered-deckgl.json', visible: true,
      properties: { radiusMinPixels: 4, radiusMaxPixels: 25, stroked: false, pickable: true, autoHighlight: true }},
    { id: 'cris-intersections', type: 'ScatterplotLayer', dataUrl: '/cris-data/parker-county-intersection-risks-deckgl.json', visible: false,
      properties: { radiusMinPixels: 5, radiusMaxPixels: 12 }},
    { id: 'txdot-city-boundaries', type: 'GeoJson', dataUrl: '/txdot-city-boundaries.geojson', visible: true,
      properties: { filled: true, stroked: true, getFillColor: [100,150,200,30], getLineColor: [0,100,200,255], getLineWidth: 2, lineWidthMinPixels: 1, lineWidthMaxPixels: 3, opacity: 0.6, pickable: true, autoHighlight: true, onClick: 'handleCityBoundaryClick' }},
    { id: 'wp-parcels-trips', type: 'GeoJson', dataUrl: '/willow-park-parcels-with-trips.geojson', visible: false,
      properties: { filled: true, stroked: true, getLineColor: [50,50,50,200], getLineWidth: 1, opacity: 0.75, pickable: true, autoHighlight: true }},
    { id: 'aledo-parcels-trips', type: 'GeoJson', dataUrl: '/aledo-parcels-with-trips.geojson', visible: false,
      properties: { filled: true, stroked: true, getLineColor: [50,50,50,200], getLineWidth: 1, opacity: 0.75, pickable: true, autoHighlight: true }},
    { id: 'midlothian-parcels-trips', type: 'GeoJson', dataUrl: '/midlothian-parcels-with-trips.geojson', visible: false,
      properties: { filled: true, stroked: true, getLineColor: [50,50,50,200], getLineWidth: 1, opacity: 0.75, pickable: true, autoHighlight: true }},
    { id: 'soil-clay-visualization', type: 'GeoJson', dataUrl: '/soil-data/parker-county-clay.geojson', visible: false,
      properties: { filled: true, stroked: true, getFillColor: 'getSoilClayColor', getLineColor: [139,69,19,255], getLineWidth: 2, opacity: 0.8, pickable: true, autoHighlight: true, onClick: 'handleSoilUnitClick' }},
    { id: 'soil-ksat-visualization', type: 'GeoJson', dataUrl: '/soil-data/parker-county-ksat.geojson', visible: false,
      properties: { filled: true, stroked: true, getFillColor: 'getSoilKsatColor', getLineColor: [139,69,19,255], getLineWidth: 1, opacity: 0.7, pickable: true, autoHighlight: true, onClick: 'handleSoilUnitClick' }}
  ]
};

// Layer display names for the controls panel
const layerNames = {
  'cris-risk-segments':          'Crash Risk Segments',
  'cris-crashes':                'Crash Points',
  'cris-intersections':          'High-Risk Intersections',
  'parker-roads-base':           'Road Network (Base)',
  'parker-roads-traffic':        'Road Traffic Volumes',
  'parker-roads-traffic-phase1': 'Road Traffic Volumes (IDW)',
  'txdot-city-boundaries':       'City Boundaries',
  'wp-parcels-trips':            'Willow Park',
  'aledo-parcels-trips':         'Aledo',
  'midlothian-parcels-trips':    'Midlothian',
  'soil-clay-visualization':     'Soil Clay Content (%)',
  'soil-ksat-visualization':     'Soil Permeability (Ksat)',
  'noaa-rainfall':               'Rainfall Intensity'
};

// Layer group definitions — sidebar organized by domain + sub-category
const layerGroups = [
  {
    id: 'safety',
    title: 'Safety',
    icon: '🔴',
    layerIds: ['cris-risk-segments', 'cris-crashes', 'cris-intersections']
  },
  {
    id: 'trip-generation',
    title: 'Trip Generation',
    icon: '📍',
    layerIds: ['wp-parcels-trips', 'aledo-parcels-trips', 'midlothian-parcels-trips']
  },
  {
    id: 'road-network',
    title: 'Road Network',
    icon: '🛣',
    layerIds: ['parker-roads-traffic-phase1', 'parker-roads-traffic', 'parker-roads-base']
  },
  {
    id: 'boundaries',
    title: 'Boundaries',
    icon: '🗺',
    layerIds: ['txdot-city-boundaries']
  },
  {
    id: 'environment',
    title: 'Environment',
    icon: '🌱',
    layerIds: ['soil-clay-visualization', 'soil-ksat-visualization']
  }
];

const baseMapStyles = [
  { id: 'voyager', name: 'Voyager', url: 'https://basemaps.cartocdn.com/gl/voyager-gl-style/style.json' },
  { id: 'light', name: 'Light', url: 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json' },
  { id: 'dark', name: 'Dark', url: 'https://basemaps.cartocdn.com/gl/dark-matter-gl-style/style.json' },
  { id: 'osm', name: 'OpenStreetMap', url: 'https://tiles.openfreemap.org/styles/liberty' }
];

async function initMapDirectly() {
  if (mapInitialized) return;
  mapInitialized = true;

  console.log('[MUENY] Blazor unavailable — initializing map directly via JS fallback');

  // Hide the loading spinner and error UI
  const appDiv = document.getElementById('app');
  const errorUi = document.getElementById('blazor-error-ui');
  if (errorUi) errorUi.style.display = 'none';

  // Replace app div content with sidebar + map layout
  appDiv.innerHTML = `
    <aside id="layer-sidebar" class="layer-sidebar">
      <div class="sidebar-content" id="map-controls-panel"></div>
    </aside>
    <div id="map-wrapper">
      <div id="maplibre-container" style="width: 100%; height: 100%;"></div>
      <div class="zoom-indicator">
        Zoom: <span id="maplibre-container-zoom">--</span>
      </div>
    </div>
    <aside id="trip-panel" class="trip-panel"></aside>
  `;

  // Build layer controls
  buildLayerControls();

  // Import the map module and create the map
  try {
    const mapModule = await import('./maplibre-deckgl-integration.js');
    const mapInstance = mapModule.createIntegratedMap('maplibre-container', defaultConfig);
    window._muenyMapModule = mapModule;
    window._muenyMapInstance = mapInstance;
    console.log('[MUENY] Map initialized successfully via JS fallback');
  } catch (err) {
    console.error('[MUENY] Failed to initialize map:', err);
    appDiv.innerHTML = '<div style="color:white;padding:40px;text-align:center;">Failed to load map. Please reload the page.</div>';
  }
}

function buildLayerControls() {
  const panel = document.getElementById('map-controls-panel');
  if (!panel) return;

  let html = '';

  // Willow Park Stats Banner
  html += `<div class="wp-stats-banner">
    <div class="stats-title">Willow Park</div>
    <div class="stats-grid">
      <div class="stat-card"><div class="stat-value" id="stat-crashes">--</div><div class="stat-label">Crashes (5yr)</div></div>
      <div class="stat-card"><div class="stat-value" id="stat-parcels">--</div><div class="stat-label">Parcels</div></div>
      <div class="stat-card"><div class="stat-value" id="stat-trips">--</div><div class="stat-label">Daily Trips Est.</div></div>
      <div class="stat-card"><div class="stat-value" id="stat-roads">--</div><div class="stat-label">Roads w/ AADT</div></div>
    </div>
  </div>`;

  // Generate Report button
  html += `<button class="report-btn" id="generate-report-btn">
    <svg width="14" height="14" viewBox="0 0 16 16" fill="none"><path d="M3 2h7l3 3v9H3V2z" stroke="white" stroke-width="1.2" fill="none"/><path d="M9 2v4h4" stroke="white" stroke-width="1.2" fill="none"/><line x1="5" y1="8" x2="11" y2="8" stroke="white" stroke-width="1.2"/><line x1="5" y1="11" x2="9" y2="11" stroke="white" stroke-width="1.2"/></svg>
    Generate Report
  </button>`;

  // Base map selector
  html += '<div class="control-section"><h3>Base Map</h3><select id="basemap-select">';
  baseMapStyles.forEach(s => {
    const sel = s.id === 'voyager' ? ' selected' : '';
    html += `<option value="${s.id}"${sel}>${s.name}</option>`;
  });
  html += '</select></div>';

  // Grouped layer controls
  layerGroups.forEach(group => {
    const groupLayers = group.layerIds
      .map(id => defaultConfig.layers.find(l => l.id === id))
      .filter(Boolean);
    if (!groupLayers.length) return;

    html += `<div class="layer-group" id="group-${group.id}">
      <div class="layer-group-header" data-group="${group.id}">
        <span class="layer-group-title">${group.icon} ${group.title}</span>
        <span class="layer-group-chevron" id="chevron-${group.id}">▾</span>
      </div>
      <div class="layer-group-content" id="content-${group.id}">`;
    groupLayers.forEach(l => {
      const checked = l.visible ? ' checked' : '';
      html += `<label class="layer-toggle"><input type="checkbox" data-layer-id="${l.id}"${checked} /> ${layerNames[l.id] || l.id}</label>`;
    });
    html += `</div></div>`;
  });

  // Search Location
  html += `<div class="control-section">
    <h3>Search Location</h3>
    <div class="search-inputs">
      <input type="number" step="any" placeholder="Latitude (32.7365)" id="search-lat" class="coord-input" />
      <input type="number" step="any" placeholder="Longitude (-97.6506)" id="search-lng" class="coord-input" />
      <button id="search-btn" class="search-btn">📍 Go to Location</button>
    </div>
  </div>`;

  panel.innerHTML = html;

  // Wire layer toggle checkboxes
  panel.querySelectorAll('input[type="checkbox"]').forEach(cb => {
    cb.addEventListener('change', (e) => {
      const layerId = e.target.dataset.layerId;
      const visible = e.target.checked;
      const layer = defaultConfig.layers.find(l => l.id === layerId);
      if (layer) {
        layer.visible = visible;
        if (window._muenyMapModule && window._muenyMapInstance) {
          window._muenyMapModule.updateIntegratedMapLayers(window._muenyMapInstance, defaultConfig.layers);
        }
      }
    });
  });

  // Wire basemap selector
  document.getElementById('basemap-select').addEventListener('change', (e) => {
    const style = baseMapStyles.find(s => s.id === e.target.value);
    if (style && window._muenyMapModule && window._muenyMapInstance) {
      window._muenyMapModule.updateBaseMapStyle(window._muenyMapInstance, style.url);
    }
  });

  // Wire search
  document.getElementById('search-btn').addEventListener('click', () => {
    const lat = parseFloat(document.getElementById('search-lat').value);
    const lng = parseFloat(document.getElementById('search-lng').value);
    if (!isNaN(lat) && !isNaN(lng) && window._muenyMapModule && window._muenyMapInstance) {
      window._muenyMapModule.flyToLocation(window._muenyMapInstance, lat, lng, 16);
    }
  });

  // Wire collapsible group headers
  panel.querySelectorAll('.layer-group-header').forEach(header => {
    header.addEventListener('click', () => {
      const groupId = header.dataset.group;
      const content = document.getElementById(`content-${groupId}`);
      const chevron = document.getElementById(`chevron-${groupId}`);
      if (!content) return;
      const isCollapsed = content.classList.contains('collapsed');
      if (isCollapsed) {
        content.classList.remove('collapsed');
        content.style.maxHeight = content.scrollHeight + 200 + 'px';
        chevron.textContent = '▾';
        chevron.classList.remove('collapsed');
      } else {
        content.classList.add('collapsed');
        content.style.maxHeight = '0';
        chevron.textContent = '▸';
        chevron.classList.add('collapsed');
      }
    });
  });

  // Set initial maxHeight after DOM paints — use setTimeout to ensure scrollHeight is available
  setTimeout(() => {
    panel.querySelectorAll('.layer-group-content').forEach(content => {
      content.style.maxHeight = content.scrollHeight + 200 + 'px';
    });
  }, 50);

  // Wire report button — delegates to the report.js module
  document.getElementById('generate-report-btn').addEventListener('click', () => window.MuenyReport.showReportModal());

  // Load stats asynchronously
  loadWillowParkStats();
}

// Load Willow Park stats from GeoJSON files
async function loadWillowParkStats() {
  try {
    // Crashes count
    const crashRes = await fetch('/cris-data/parker-county-crashes-clustered-deckgl.json');
    if (crashRes.ok) {
      const crashData = await crashRes.json();
      const count = crashData.features ? crashData.features.length : (Array.isArray(crashData) ? crashData.length : '?');
      const el = document.getElementById('stat-crashes');
      if (el) el.textContent = count.toLocaleString();
    }
  } catch(e) { console.warn('[MUENY] Could not load crash stats'); }

  try {
    // Parcel + trip stats
    const parcelRes = await fetch('/willow-park-parcels-with-trips.geojson');
    if (parcelRes.ok) {
      const parcelData = await parcelRes.json();
      const features = parcelData.features || [];
      const parcelCount = features.length;
      const totalTrips = features.reduce((sum, f) => sum + (f.properties?.daily_trips || 0), 0);
      const parcelEl = document.getElementById('stat-parcels');
      const tripEl = document.getElementById('stat-trips');
      if (parcelEl) parcelEl.textContent = parcelCount.toLocaleString();
      if (tripEl) tripEl.textContent = Math.round(totalTrips).toLocaleString();
    }
  } catch(e) { console.warn('[MUENY] Could not load parcel stats'); }

  try {
    // Roads with AADT
    const roadRes = await fetch('/parker-roads-with-traffic-phase1.geojson');
    if (roadRes.ok) {
      const roadData = await roadRes.json();
      const features = roadData.features || [];
      const withAadt = features.filter(f => f.properties?.aadt > 0 || f.properties?.AADT > 0).length;
      const el = document.getElementById('stat-roads');
      if (el) el.textContent = withAadt.toLocaleString();
    }
  } catch(e) { console.warn('[MUENY] Could not load road stats'); }
}

// Report generation lives in js/report.js as MuenyReport.showReportModal/generateReport.

// Try to start Blazor, fall back to direct JS init
async function tryBlazorThenFallback() {
  try {
    if (typeof Blazor !== 'undefined' && Blazor.start) {
      const blazorPromise = Blazor.start();
      const timeoutPromise = new Promise((_, reject) =>
        setTimeout(() => reject(new Error('Blazor timeout')), BLAZOR_TIMEOUT_MS)
      );
      await Promise.race([blazorPromise, timeoutPromise]);
      blazorStarted = true;
      console.log('[MUENY] Blazor started successfully');
    } else {
      throw new Error('Blazor not available');
    }
  } catch (err) {
    console.warn('[MUENY] Blazor failed or timed out:', err.message, '— using JS fallback');
    await initMapDirectly();
  }
}

tryBlazorThenFallback();

// Sidebar toggle for mobile (works for both JS fallback and Blazor paths)
function getActiveSidebar() {
  return document.getElementById('layer-sidebar') ||
         document.getElementById('blazor-layer-sidebar') ||
         document.querySelector('.layer-sidebar');
}

document.getElementById('sidebar-toggle').addEventListener('click', () => {
  const sidebar = getActiveSidebar();
  const backdrop = document.getElementById('sidebar-backdrop');
  if (sidebar) {
    const isOpen = sidebar.classList.toggle('open');
    backdrop.classList.toggle('visible', isOpen);
  }
});

document.getElementById('sidebar-backdrop').addEventListener('click', () => {
  const sidebar = getActiveSidebar();
  const backdrop = document.getElementById('sidebar-backdrop');
  if (sidebar) {
    sidebar.classList.remove('open');
    backdrop.classList.remove('visible');
  }
  // Also close trip panel mobile overlay
  const tripPanel = document.getElementById('trip-panel');
  if (tripPanel) tripPanel.classList.remove('mobile-open');
});

// Trip panel mobile toggle
document.getElementById('trip-panel-toggle').addEventListener('click', () => {
  const tripPanel = document.getElementById('trip-panel');
  const backdrop = document.getElementById('sidebar-backdrop');
  if (tripPanel && tripPanel.classList.contains('trip-panel-visible')) {
    const isOpen = tripPanel.classList.toggle('mobile-open');
    backdrop.classList.toggle('visible', isOpen);
  }
});
