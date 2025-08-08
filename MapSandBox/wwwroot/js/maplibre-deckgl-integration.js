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
            case 'path':
                // Special handling for Path layers (like traffic roads)
                console.log('Creating PathLayer with config:', config);
                const pathLayer = new deck.PathLayer({
                    id: config.id,
                    data: config.dataUrl,
                    dataTransform: d => {
                        if (d && d.features) {
                            return d.features; // Extract features from GeoJSON FeatureCollection
                        }
                        return d;
                    },
                    getPath: d => {
                        return d.geometry.coordinates;
                    },
                    getColor: d => {
                        const color = getTrafficGradientColor(d);
                        return color;
                    },
                    getWidth: d => {
                        const width = getTrafficWidth(d);
                        return width;
                    },
                    // Simplified configuration to match deck.gl docs
                    widthScale: 1,
                    widthMinPixels: 2,
                    widthMaxPixels: 50,
                    rounded: true,
                    pickable: true,
                    autoHighlight: true,
                    onClick: handleTrafficRoadClick,
                    onDataLoad: data => {
                        if (data && data.features) {
                        }
                    }
                });
                layers.push(pathLayer);
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
        case 'parker-roads-base':
            properties.stroked = true;
            properties.filled = false;
            properties.lineWidthMinPixels = 1;
            properties.opacity = 0.6;
            properties.getLineColor = [120, 120, 120, 128]; // Gray for base roads
            properties.getLineWidth = 1;
            properties.pickable = true;
            properties.onClick = handleRoadClick;
            break;
        case 'parker-roads-traffic':
            // Path layer properties are handled in createLayersFromConfig
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
    console.log('Creating TileLayer for traffic-counts with config:', config);
    const tileLayer = new deck.TileLayer({
        id: config.id,
        data: '/tiles/traffic-counts/{z}/{x}/{y}.geojson',
        minZoom: 12,
        maxZoom: 16,
        tileSize: 512,
        maxCacheSize: 10 * 1024 * 1024, // 10MB cache
        maxCacheByteSize: 50 * 1024 * 1024, // 50MB total
        refinementStrategy: 'never',
        
        // Enable picking on the TileLayer itself
        pickable: true,
        
        // TileLayer-level event handlers (deck.gl routes sublayer events here)
        onHover: (info) => {
            if (info.object) {
                // Change cursor to indicate clickable
                document.body.style.cursor = 'pointer';
            } else {
                document.body.style.cursor = 'default';
            }
            return true;
        },
        
        onClick: (info) => {
            if (info.object) {
                return handleTrafficCountClick(info);
            } else {
                return false;
            }
        },
        
        // Add data loading callbacks for debugging
        onTileLoad: (tile) => {
            console.log('Tile loaded:', tile.index, 'data:', tile.data);
        },
        onTileError: (error) => {
            console.error('Tile loading error:', error);
        },
        
        renderSubLayers: props => {
            // Strict zoom enforcement using correct tile structure
            const tileZ = props.tile?.index?.z;
            if (tileZ < 12 || tileZ > 16) {
                return null;
            }
            
            // Skip rendering if no data is available yet
            if (!props.data) {
                return null;
            }
            
            return new deck.GeoJsonLayer({
                ...props,
                id: `${props.id}-geojson`,
                
                // Styling properties - Enhanced for debugging
                stroked: true,
                filled: true,
                pointRadiusMinPixels: 4,
                pointRadiusMaxPixels: 50,
                opacity: 0.9,
                
                // Dynamic styling functions
                getPointRadius: (feature) => {
                    const radius = getTrafficRadius(feature);
                    return radius;
                },
                getFillColor: (feature) => {
                    const color = getTrafficColor(feature);
                    return color;
                },
                
                // Interactions - Let TileLayer handle events (deck.gl routes them up)
                pickable: true,
                autoHighlight: true,
                
                // Performance optimizations
                updateTriggers: {
                    getPointRadius: [],
                    getFillColor: []
                }
            });
        }
    });
    
    return tileLayer;
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

// Unified log scale parameters for consistent station and road styling
const TRAFFIC_LOG_MIN = 1.0;  // log₁₀(10) - practical minimum
const TRAFFIC_LOG_MAX = 5.2;  // log₁₀(158,869) - observed maximum

// Traffic count styling functions
function getTrafficRadius(feature) {
    const aadt = feature.properties.latestAadt;
    if (!aadt || aadt < 10) return 5; // Default size for no/low data
    
    // Scale radius using unified log10 scale
    const minRadius = 5;
    const maxRadius = 25;
    const logAadt = Math.log10(aadt);
    
    const normalizedValue = Math.min(Math.max((logAadt - TRAFFIC_LOG_MIN) / (TRAFFIC_LOG_MAX - TRAFFIC_LOG_MIN), 0), 1);
    return minRadius + (maxRadius - minRadius) * normalizedValue;
}

function getTrafficColor(feature) {
    const aadt = feature.properties.latestAadt;
    const active = feature.properties.active;
    
    // Different colors for inactive locations
    if (active === 'No') {
        return [128, 128, 128, 180]; // Gray for inactive
    }
    
    if (!aadt || aadt < 10) {
        return [200, 200, 200, 180]; // Light gray for no/low data
    }
    
    // Continuous log scale color matching road gradient: Green → Yellow → Red
    const logAadt = Math.log10(aadt);
    const ratio = Math.min(Math.max((logAadt - TRAFFIC_LOG_MIN) / (TRAFFIC_LOG_MAX - TRAFFIC_LOG_MIN), 0), 1);
    
    const red = Math.floor(255 * ratio);
    const green = Math.floor(255 * (1 - ratio));
    
    return [red, green, 0, 200];
}

function handleTrafficCountClick(info) {
    console.log('Traffic count clicked:', info);
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

// Traffic gradient styling functions for roads with traffic data
function getTrafficGradientColor(feature) {
    const aadt = feature.properties.traffic?.aadt || 0;
    if (aadt < 10) return [120, 120, 120, 128]; // Gray for very low/no data
    
    const logAADT = Math.log10(aadt);
    
    // Normalize using unified log scale parameters
    const ratio = Math.min(Math.max((logAADT - TRAFFIC_LOG_MIN) / (TRAFFIC_LOG_MAX - TRAFFIC_LOG_MIN), 0), 1);
    
    // Smooth color interpolation: Green → Yellow → Red
    const red = Math.floor(255 * ratio);
    const green = Math.floor(255 * (1 - ratio));
    
    return [red, green, 0, 180];
}

function getTrafficWidth(feature) {
    const aadt = feature.properties.traffic?.aadt || 0;
    if (aadt < 10) return 1; // Minimal width for very low traffic
    
    const logAADT = Math.log10(aadt);
    
    // Normalize using unified log scale parameters and scale to width range
    const ratio = Math.min(Math.max((logAADT - TRAFFIC_LOG_MIN) / (TRAFFIC_LOG_MAX - TRAFFIC_LOG_MIN), 0), 1);
    return 2 + (ratio * 8); // 2-10 pixel width range
}

function handleTrafficRoadClick(info) {
    if (info.object) {
        const road = info.object;
        const roadName = road.properties.FULLNAME || 'Unnamed Road';
        const roadType = road.properties.RTTYP || 'Unknown';
        const roadTypeName = getRoadTypeName(roadType);
        
        const traffic = road.properties.traffic;
        if (traffic) {
            const aadt = traffic.aadt || 'No data';
            const aadtYear = traffic.aadtYear || '';
            const locationId = traffic.locationId || 'Unknown';
            const locatedOn = traffic.locatedOn || 'Unknown';
            
            const message = `Traffic Road: ${roadName}
────────────────────────
Road Type: ${roadTypeName} (${roadType})
Traffic Location ID: ${locationId}
Located On: ${locatedOn}

Traffic Data:
• AADT (${aadtYear}): ${aadt.toLocaleString()} vehicles/day

Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;
            
            alert(message);
        } else {
            // Fallback for roads without traffic data
            handleRoadClick(info);
        }
    }
}

// Export for debugging
export function getIntegratedMapInstance() {
    return integratedMapInstance;
}