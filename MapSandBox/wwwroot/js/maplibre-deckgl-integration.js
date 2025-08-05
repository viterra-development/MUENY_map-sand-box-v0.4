// MapLibre + deck.gl integration for Blazor
let integratedMapInstance = null;
let maplibreMap = null;
let deckOverlay = null;

export function createIntegratedMap(containerId, config) {
    console.log('Creating integrated MapLibre + deck.gl map with config:', config);
    
    // Initialize MapLibre base map
    maplibreMap = new maplibregl.Map({
        container: containerId,
        style: config.baseMap?.style || 'https://basemaps.cartocdn.com/gl/positron-gl-style/style.json',
        center: [config.longitude, config.latitude],
        zoom: config.zoom,
        bearing: config.bearing || 0,
        pitch: config.pitch || 0,
        antialias: true
    });

    // Add navigation controls if enabled
    if (config.baseMap?.showControls !== false) {
        maplibreMap.addControl(new maplibregl.NavigationControl());
    }
    
    // Update zoom level indicator
    function updateZoomIndicator() {
        const zoom = maplibreMap.getZoom().toFixed(1);
        const zoomElement = document.getElementById(`${containerId}-zoom`);
        if (zoomElement) {
            zoomElement.textContent = zoom;
        }
    }
    
    // Initial zoom update
    maplibreMap.on('load', updateZoomIndicator);
    
    // Update zoom on zoom changes
    maplibreMap.on('zoom', updateZoomIndicator);
    maplibreMap.on('zoomend', updateZoomIndicator);

    // Wait for map to load before adding deck.gl overlay
    maplibreMap.on('load', () => {
        // Create deck.gl layers from config
        const layers = createLayersFromConfig(config.layers);
        
        // Create deck.gl overlay using MapboxOverlay (works with MapLibre)
        deckOverlay = new deck.MapboxOverlay({
            interleaved: false, // Overlaid mode for better compatibility
            layers: layers
        });

        // Add deck.gl overlay to MapLibre map
        maplibreMap.addControl(deckOverlay);
        
        // Update the instance reference with the overlay
        integratedMapInstance.deckOverlay = deckOverlay;
        
        console.log('MapLibre + deck.gl integration complete');
    });

    // Store reference for later use (deckOverlay will be added after map loads)
    integratedMapInstance = {
        maplibre: maplibreMap,
        deckOverlay: null // Will be set after map loads
    };

    return integratedMapInstance;
}

export function updateIntegratedMapLayers(mapInstance, layers) {
    console.log('updateIntegratedMapLayers called with:', layers);
    if (!mapInstance || !mapInstance.deckOverlay) {
        console.warn('No integrated map instance available');
        return;
    }
    
    const deckLayers = createLayersFromConfig(layers);
    console.log('Created deck layers:', deckLayers);
    mapInstance.deckOverlay.setProps({ layers: deckLayers });
}

export function updateBaseMapStyle(mapInstance, styleUrl) {
    if (mapInstance && mapInstance.maplibre && styleUrl) {
        mapInstance.maplibre.setStyle(styleUrl);
    }
}

export function disposeIntegratedMap(mapInstance) {
    if (mapInstance) {
        if (mapInstance.deckOverlay) {
            // Remove deck.gl overlay
            mapInstance.maplibre.removeControl(mapInstance.deckOverlay);
        }
        if (mapInstance.maplibre) {
            mapInstance.maplibre.remove();
        }
    }
    integratedMapInstance = null;
    maplibreMap = null;
    deckOverlay = null;
}

function createLayersFromConfig(layerConfigs) {
    const layers = [];
    
    layerConfigs.forEach(config => {
        if (!config.visible) return;
        
        // Special handling for traffic-counts layer (uses TileLayer)
        if (config.id === 'traffic-counts') {
            layers.push(createTrafficCountsTileLayer(config));
            return;
        }
        
        switch (config.type.toLowerCase()) {
            case 'geojson':
                layers.push(new deck.GeoJsonLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'greatcircle':
                layers.push(new deck.GreatCircleLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'arc':
                layers.push(new deck.ArcLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
        }
    });
    
    return layers;
}

function getLayerProperties(config) {
    const properties = { ...config.properties };
    
    // Handle function properties based on layer type
    switch (config.id) {
        case 'countries':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 3;
            properties.opacity = 1.0;
            properties.getLineColor = [60, 60, 60];
            properties.getFillColor = [200, 200, 200];
            break;
        case 'rivers':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 1;
            properties.opacity = 0.6;
            properties.getLineColor = [100, 150, 255];
            properties.getFillColor = [100, 150, 255];
            break;
        case 'airports':
            properties.getPointRadius = f => (11 - f.properties.scalerank);
            properties.onClick = info => info.object && alert(`${info.object.properties.name} (${info.object.properties.abbrev})`);
            break;
        case 'flight-paths':
            properties.dataTransform = d => d.features.filter(f => f.properties.scalerank < 4);
            properties.getSourcePosition = f => [-0.4531566, 51.4709959]; // London
            properties.getTargetPosition = f => f.geometry.coordinates;
            break;
        case 'parker-roads':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 1;
            properties.lineWidthMaxPixels = 4;
            properties.opacity = 0.8;
            properties.getLineColor = getRoadColor;
            properties.getLineWidth = getRoadWidth;
            properties.pickable = true;
            properties.onClick = handleRoadClick;
            break;
        case 'traffic-counts':
            // Use TileLayer for progressive loading instead of direct GeoJsonLayer
            return createTrafficCountsTileLayer(config);
            break;
    }
    
    return properties;
}

// Traffic Counts TileLayer factory
function createTrafficCountsTileLayer(config) {
    return new deck.TileLayer({
        id: config.id,
        data: '/tiles/traffic-counts/{z}/{x}/{y}.geojson',
        minZoom: 12,
        maxZoom: 16,
        tileSize: 512,
        maxCacheSize: 10 * 1024 * 1024, // 10MB cache
        maxCacheByteSize: 50 * 1024 * 1024, // 50MB total
        refinementStrategy: 'never',
        
        renderSubLayers: props => {
            // Strict zoom enforcement
            if (props.tile?.z < 12 || props.tile?.z > 16) {
                return null;
            }
            
            return new deck.GeoJsonLayer({
                ...props,
                id: `${props.id}-geojson`,
                
                // Styling properties
                stroked: true,
                filled: true,
                pointRadiusMinPixels: 3,
                pointRadiusMaxPixels: 50,
                opacity: 0.9,
                
                // Dynamic styling functions
                getPointRadius: getTrafficRadius,
                getFillColor: getTrafficColor,
                getLineColor: [0, 0, 0, 255], // black outline
                getLineWidth: 2,
                
                // Interactions
                pickable: true,
                onClick: handleTrafficCountClick,
                
                // Performance optimizations
                updateTriggers: {
                    getPointRadius: [],
                    getFillColor: []
                }
            });
        }
    });
}

// Road styling functions
function getRoadColor(feature) {
    const rttyp = feature.properties.RTTYP;
    switch(rttyp) {
        case 'I': return [255, 0, 0];    // Red for Interstate
        case 'U': return [0, 0, 255];    // Blue for US Highway
        case 'S': return [0, 128, 0];    // Green for State Highway
        case 'M': return [128, 128, 128]; // Gray for Minor roads
        default: return [100, 100, 100]; // Default gray for other roads
    }
}

function getRoadWidth(feature) {
    const rttyp = feature.properties.RTTYP;
    switch(rttyp) {
        case 'I': return 4; // Interstate
        case 'U': return 3; // US Highway
        case 'S': return 2; // State Highway
        case 'M': return 1; // Minor roads
        default: return 1;  // Default width
    }
}

function handleRoadClick(info) {
    if (info.object) {
        const road = info.object;
        const name = road.properties.FULLNAME || 'Unnamed Road';
        const type = road.properties.RTTYP || 'Unknown';
        const roadType = getRoadTypeName(type);
        
        alert(`Road: ${name}\nType: ${roadType}\nRoute Type Code: ${type}`);
    }
}

function getRoadTypeName(rttyp) {
    switch(rttyp) {
        case 'I': return 'Interstate';
        case 'U': return 'US Highway';
        case 'S': return 'State Highway';
        case 'M': return 'Minor Road';
        default: return 'Other';
    }
}

// Traffic count styling functions
function getTrafficRadius(feature) {
    const aadt = feature.properties.latestAadt;
    if (!aadt) return 5; // Default size for no data
    
    // Scale radius based on AADT value (logarithmic scale)
    // AADT ranges from ~90 to ~8400 in the data
    const minRadius = 5;
    const maxRadius = 25;
    const logAadt = Math.log(Math.max(aadt, 1));
    const logMin = Math.log(90);
    const logMax = Math.log(8400);
    
    const normalizedValue = (logAadt - logMin) / (logMax - logMin);
    return minRadius + (maxRadius - minRadius) * Math.max(0, Math.min(1, normalizedValue));
}

function getTrafficColor(feature) {
    const aadt = feature.properties.latestAadt;
    const active = feature.properties.active;
    
    // Different colors for inactive locations
    if (active === 'No') {
        return [128, 128, 128, 180]; // Gray for inactive
    }
    
    if (!aadt) {
        return [200, 200, 200, 180]; // Light gray for no data
    }
    
    // Color scale based on AADT value (traffic volume)
    if (aadt >= 5000) {
        return [255, 0, 0, 200];     // Red for high traffic (5000+)
    } else if (aadt >= 1000) {
        return [255, 165, 0, 200];   // Orange for medium-high traffic (1000-5000)
    } else if (aadt >= 500) {
        return [255, 255, 0, 200];   // Yellow for medium traffic (500-1000)
    } else if (aadt >= 100) {
        return [0, 255, 0, 200];     // Green for low traffic (100-500)
    } else {
        return [0, 0, 255, 200];     // Blue for very low traffic (<100)
    }
}

function handleTrafficCountClick(info) {
    if (info.object) {
        const location = info.object.properties;
        const locationId = location.locationId || 'Unknown';
        const locatedOn = location.locatedOn || 'Unknown';
        const aadt = location.latestAadt || 'No data';
        const aadtYear = location.latestAadtYear || '';
        const volumeCount = location.latestVolumeCount || 'No data';
        const volumeDate = location.latestVolumeDate || '';
        const category = location.category || 'Unknown';
        const active = location.active || 'Unknown';
        
        const message = `Traffic Count Location
────────────────────────
Location ID: ${locationId}
Located On: ${locatedOn}
Category: ${category}
Status: ${active}

Latest Traffic Data:
• AADT (${aadtYear}): ${aadt.toLocaleString()} vehicles/day
• Volume Count (${volumeDate}): ${volumeCount.toLocaleString()} vehicles
• Functional Class: ${location.fnctClass || 'Unknown'}

Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;
        
        alert(message);
    }
}

// Export for debugging
export function getIntegratedMapInstance() {
    return integratedMapInstance;
}