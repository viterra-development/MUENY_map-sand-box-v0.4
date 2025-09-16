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
        // Create deck.gl layers from config (MapLibre raster layers are handled automatically)
        const deckLayers = createLayersFromConfig(config.layers, maplibreMap);
        
        // Create deck.gl overlay using MapboxOverlay (works with MapLibre)
        deckOverlay = new deck.MapboxOverlay({
            interleaved: false, // Overlaid mode for better compatibility
            layers: deckLayers
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
    if (!mapInstance || !mapInstance.maplibre) {
        console.warn('No integrated map instance available');
        return;
    }
    
    
    try {
        // Handle MapLibre raster layers and get deck.gl layers
        const deckLayers = createLayersFromConfig(layers, mapInstance.maplibre);
        console.log('Created deck layers:', deckLayers);
        console.log('Layer IDs:', deckLayers.map(l => l.id));
        console.log('CRIS crash layer:', deckLayers.find(l => l.id === 'cris-crashes'));

        // Update deck.gl overlay if it exists
        if (mapInstance.deckOverlay) {
            mapInstance.deckOverlay.setProps({ layers: deckLayers });

            // Force a redraw
            setTimeout(() => {
                if (mapInstance.maplibre) {
                    mapInstance.maplibre.triggerRepaint();
                }
            }, 100);
        }
    } catch (error) {
        console.error('Error updating integrated map layers:', error);
        // Continue execution without crashing the app
    }
    
    // Handle layer visibility changes for MapLibre raster layers
    layers.forEach(layerConfig => {
        if (layerConfig.type.toLowerCase() === 'rastertile') {
            updateMapLibreRasterLayerVisibility(mapInstance.maplibre, layerConfig.id, layerConfig.visible);
        }
    });
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

function createLayersFromConfig(layerConfigs, maplibreMap = null) {
    const deckLayers = [];
    const maplibreRasterLayers = [];
    
    layerConfigs.forEach(config => {
        console.log(`Processing layer ${config.id}: visible=${config.visible}, type=${config.type}`);
        if (!config.visible) {
            console.log(`Skipping invisible layer: ${config.id}`);
            return;
        }
        
        // Handle raster tiles through MapLibre GL JS natively
        if (config.type.toLowerCase() === 'rastertile') {
            maplibreRasterLayers.push(config);
            return;
        }
        
        // Special handling for traffic-counts layer (uses TileLayer)
        if (config.id === 'traffic-counts') {
            deckLayers.push(createTrafficCountsTileLayer(config));
            return;
        }
        
        switch (config.type.toLowerCase()) {
            case 'geojson':
                console.log(`Creating GeoJsonLayer for ${config.id} with data URL: ${config.dataUrl}`);
                
                try {
                    deckLayers.push(new deck.GeoJsonLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config)
                    }));
                } catch (error) {
                    console.warn(`Failed to create GeoJsonLayer for ${config.id}:`, error);
                    // Add empty layer to prevent crashes
                    deckLayers.push(new deck.GeoJsonLayer({
                        id: config.id,
                        data: { type: 'FeatureCollection', features: [] },
                        ...getLayerProperties(config)
                    }));
                }
                break;
            case 'path':
                // Special handling for Path layers (like traffic roads)
                console.log('Creating PathLayer with config:', config);
                try {
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
                                console.log(`✅ PathLayer ${config.id} loaded ${data.features.length} features`);
                            }
                        }
                    });
                    deckLayers.push(pathLayer);
                } catch (error) {
                    console.warn(`Failed to create PathLayer for ${config.id}:`, error);
                    // Add empty PathLayer to prevent crashes
                    deckLayers.push(new deck.PathLayer({
                        id: config.id,
                        data: [],
                        getPath: d => d.geometry?.coordinates || [],
                        getColor: [128, 128, 128],
                        getWidth: 1
                    }));
                }
                break;
            case 'greatcircle':
                deckLayers.push(new deck.GreatCircleLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'arc':
                deckLayers.push(new deck.ArcLayer({
                    id: config.id,
                    data: config.dataUrl,
                    ...getLayerProperties(config)
                }));
                break;
            case 'scatterplotlayer':
                // Handle CRIS crash points specifically
                if (config.id === 'cris-crashes') {
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        radiusMinPixels: 3,
                        radiusMaxPixels: 15,
                        radiusUnits: 'pixels',
                        radiusScale: 1,
                        getPosition: d => d.Coordinates,
                        getRadius: d => d.PersonsInvolved || 1,
                        getFillColor: d => getCrashSeverityColor(d.Severity),
                        filled: true,
                        stroked: true,
                        getLineColor: [0, 0, 0, 255], // Black border
                        lineWidthMinPixels: 1,
                        billboard: true,
                        pickable: true,
                        onClick: handleCrashClick,
                        onDataLoad: data => {
                            if (data && data.features) {
                                console.log(`✅ CRIS Crashes loaded ${data.features.length} features`);
                                console.log('Sample crash:', data.features[0]);
                                console.log('Position:', data.features[0].geometry.coordinates);
                                console.log('Properties:', data.features[0].properties);

                                // Check if map viewport includes these coordinates
                                const bounds = data.features.reduce((acc, f) => {
                                    const [lng, lat] = f.geometry.coordinates;
                                    acc.minLng = Math.min(acc.minLng, lng);
                                    acc.maxLng = Math.max(acc.maxLng, lng);
                                    acc.minLat = Math.min(acc.minLat, lat);
                                    acc.maxLat = Math.max(acc.maxLat, lat);
                                    return acc;
                                }, { minLng: Infinity, maxLng: -Infinity, minLat: Infinity, maxLat: -Infinity });

                                console.log('Data bounds:', bounds);
                                console.log('Data center:', {
                                    lng: (bounds.minLng + bounds.maxLng) / 2,
                                    lat: (bounds.minLat + bounds.maxLat) / 2
                                });
                            }
                        }
                    }));
                } else if (config.id === 'cris-intersections') {
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        radiusMinPixels: 6,
                        radiusMaxPixels: 25,
                        getPosition: d => d.Coordinates,
                        getRadius: d => Math.max(8, Math.min(25, Math.sqrt(d.CrashCount || 1) * 8)),
                        getFillColor: d => getRiskLevelColor(d.RiskLevel),
                        stroked: true,
                        getLineColor: [0, 0, 0, 255],
                        lineWidthMinPixels: 1,
                        pickable: true,
                        onClick: handleIntersectionClick,
                        onDataLoad: data => {
                            if (data && data.features) {
                                console.log(`✅ CRIS Intersections loaded ${data.features.length} features`);
                                console.log('Sample intersection:', data.features[0]);
                            }
                        }
                    }));
                } else {
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config),
                        onDataLoad: data => {
                            if (data && data.features) {
                                console.log(`✅ ScatterplotLayer ${config.id} loaded ${data.features.length} features`);
                                console.log('Sample feature:', data.features[0]);
                            }
                        }
                    }));
                }
                break;
            case 'pathlayer':
                // Handle CRIS risk segments specifically
                if (config.id === 'cris-risk-segments') {
                    deckLayers.push(new deck.PathLayer({
                        id: config.id,
                        data: config.dataUrl,
                        getPath: d => d.Coordinates,
                        getWidth: d => Math.max(2, (d.Aadt || 100) / 1000),
                        getColor: d => getRiskLevelColor(d.RiskLevel),
                        widthMinPixels: 2,
                        widthMaxPixels: 20,
                        pickable: true,
                        onClick: handleRiskSegmentClick,
                        onDataLoad: data => {
                            if (data && data.features) {
                                console.log(`✅ CRIS Risk Segments loaded ${data.features.length} features`);
                                console.log('Sample segment:', data.features[0]);
                            }
                        }
                    }));
                } else {
                    deckLayers.push(new deck.PathLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config)
                    }));
                }
                break;
        }
    });
    
    // Add MapLibre raster layers if maplibreMap is provided
    if (maplibreMap && maplibreRasterLayers.length > 0) {
        addMapLibreRasterLayers(maplibreMap, maplibreRasterLayers);
    }
    
    return deckLayers;
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
        case 'soil-clay-visualization':
            properties.filled = true;
            properties.stroked = true;
            properties.getFillColor = getSoilClayColor;
            properties.getLineColor = [139, 69, 19, 255]; // Brown soil boundary
            properties.getLineWidth = 2;
            properties.opacity = 0.8;
            properties.pickable = true;
            properties.autoHighlight = true;
            properties.onClick = handleSoilUnitClick;
            break;
        case 'soil-ksat-visualization':
            properties.filled = true;
            properties.stroked = true;
            properties.getFillColor = getSoilKsatColor;
            properties.getLineColor = [139, 69, 19, 255]; // Brown soil boundary
            properties.getLineWidth = 1;
            properties.opacity = 0.7;
            properties.pickable = true;
            properties.autoHighlight = true;
            properties.onClick = handleSoilUnitClick;
            break;
        case 'cris-crashes':
            properties.radiusMinPixels = 4;
            properties.radiusMaxPixels = 15;
            properties.radiusScale = 100;
            properties.getPosition = d => d.Coordinates;
            properties.getRadius = d => d.PersonsInvolved || 1;
            properties.getFillColor = d => getCrashSeverityColor(d.Severity);
            properties.pickable = true;
            properties.onClick = handleCrashClick;
            break;
        case 'cris-risk-segments':
            properties.getPath = d => d.Coordinates;
            properties.getWidth = d => Math.max(2, (d.Aadt || 100) / 1000);
            properties.getColor = d => getRiskLevelColor(d.RiskLevel);
            properties.widthMinPixels = 2;
            properties.widthMaxPixels = 20;
            properties.pickable = true;
            properties.onClick = handleRiskSegmentClick;
            break;
        case 'cris-intersections':
            properties.radiusMinPixels = 6;
            properties.radiusMaxPixels = 25;
            properties.getPosition = d => d.Coordinates;
            properties.getRadius = d => Math.sqrt(d.CrashCount || 1) * 100;
            properties.getFillColor = d => getRiskLevelColor(d.RiskLevel);
            properties.stroked = true;
            properties.getLineColor = [0, 0, 0, 255];
            properties.lineWidthMinPixels = 1;
            properties.pickable = true;
            properties.onClick = handleIntersectionRiskClick;
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

// MapLibre Raster Layer Management
function addMapLibreRasterLayers(map, rasterLayers) {
    rasterLayers.forEach(config => {
        const sourceId = config.id;
        const layerId = config.id;
        
        console.log(`Adding MapLibre raster layer: ${layerId} with URL: ${config.dataUrl}`);
        
        // Add source if it doesn't exist
        if (!map.getSource(sourceId)) {
            map.addSource(sourceId, {
                type: 'raster',
                tiles: [config.dataUrl],
                tileSize: config.properties.tileSize || 256,
                minzoom: config.properties.minZoom || 0,
                maxzoom: config.properties.maxZoom || 18
            });
            console.log(`Added MapLibre source: ${sourceId}`);
        }
        
        // Add layer if it doesn't exist
        if (!map.getLayer(layerId)) {
            map.addLayer({
                id: layerId,
                type: 'raster',
                source: sourceId,
                paint: {
                    'raster-opacity': config.properties.opacity || 0.75
                }
            });
            console.log(`Added MapLibre layer: ${layerId} with opacity: ${config.properties.opacity || 0.75}`);
        }
    });
}

function updateMapLibreRasterLayerVisibility(map, layerId, visible) {
    if (map.getLayer(layerId)) {
        map.setLayoutProperty(layerId, 'visibility', visible ? 'visible' : 'none');
        console.log(`Updated MapLibre layer visibility: ${layerId} = ${visible}`);
    }
}

function removeMapLibreRasterLayer(map, layerId) {
    if (map.getLayer(layerId)) {
        map.removeLayer(layerId);
        console.log(`Removed MapLibre layer: ${layerId}`);
    }
    if (map.getSource(layerId)) {
        map.removeSource(layerId);
        console.log(`Removed MapLibre source: ${layerId}`);
    }
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
            const aadt = traffic.aadt || null;
            const aadtYear = traffic.aadtYear ? traffic.aadtYear.toString() : null;
            const dhv30 = traffic.dhv30 || null;
            const locationId = traffic.locationId || null;
            const locatedOn = traffic.locatedOn || null;
            
            // Call Blazor component method via DotNet.invokeMethodAsync
            if (window.roadPopupInstance) {
                const roadData = {
                    roadName,
                    roadType,
                    roadTypeName,
                    aadt,
                    aadtYear,
                    dhv30,
                    locationId,
                    locatedOn,
                    coordinates: info.coordinate,
                    linearId: road.properties.LINEARID || null,
                    mtfcc: road.properties.MTFCC || null
                };
                
                window.roadPopupInstance.invokeMethodAsync('ShowPopupFromJS', roadData);
            } else {
                // Fallback to alert if Blazor component not available
                const message = `Traffic Road: ${roadName}
────────────────────────
Road Type: ${roadTypeName} (${roadType})
Traffic Location ID: ${locationId}
Located On: ${locatedOn}

Traffic Data:
• AADT (${aadtYear}): ${aadt ? aadt.toLocaleString() : 'No data'} vehicles/day

Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;
                
                alert(message);
            }
        } else {
            // Fallback for roads without traffic data
            handleRoadClick(info);
        }
    }
}

// Soil visualization color functions (industry-standard soil color schemes)
function getSoilClayColor(feature) {
    const clayPct = feature.properties.soil_clay_pct || 0;
    
    // USDA standard clay content color ramp (browns for clay content)
    if (clayPct < 10) return [245, 222, 179, 200];      // Light wheat (sandy)
    if (clayPct < 20) return [222, 184, 135, 200];      // Burlywood (sandy loam)
    if (clayPct < 35) return [205, 133, 63, 200];       // Peru (loam)
    if (clayPct < 50) return [160, 82, 45, 200];        // Saddle brown (clay loam)
    return [101, 67, 33, 200];                          // Dark brown (clay)
}

function getSoilKsatColor(feature) {
    const ksat = feature.properties.soil_ksat_um_per_s || 0;
    
    // Permeability color ramp (blue = high permeability, red = low)
    if (ksat > 10) return [0, 100, 255, 200];           // High permeability (blue)
    if (ksat > 5) return [0, 150, 200, 200];            // Moderate-high
    if (ksat > 1) return [100, 200, 100, 200];          // Moderate (green)
    if (ksat > 0.1) return [255, 150, 0, 200];          // Low-moderate (orange)
    return [255, 0, 0, 200];                            // Low permeability (red)
}

// Handle soil unit clicks (industry standard popup)
function handleSoilUnitClick(info) {
    if (info.object) {
        const props = info.object.properties;

        const clayPctValue = typeof props.soil_clay_pct === 'number' ? props.soil_clay_pct : null;
        const ksatValue = typeof props.soil_ksat_um_per_s === 'number' ? props.soil_ksat_um_per_s : null;

        // Prefer Blazor interop popup if available
        if (window.soilPopupInstance && typeof window.soilPopupInstance.invokeMethodAsync === 'function') {
            const soilData = {
                musym: props.musym || null,
                muname: props.muname || null,
                mukey: props.mukey || null,
                soilClayPct: clayPctValue,
                soilKsatUmPerS: ksatValue,
                coordinates: info.coordinate
            };
            window.soilPopupInstance.invokeMethodAsync('ShowPopupFromJS', soilData);
            return;
        }

        // Fallback to simple alert
        const clayPct = clayPctValue !== null ? clayPctValue.toFixed(1) : 'N/A';
        const ksat = ksatValue !== null ? ksatValue.toFixed(3) : 'N/A';

        const message = `Soil Map Unit: ${props.musym}
────────────────────────
${props.muname || 'Unknown soil type'}

Soil Properties:
• Clay Content: ${clayPct}%
• Permeability: ${ksat} μm/s

Map Unit Key: ${props.mukey}
Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;
        
        alert(message);
    }
}

// CRIS (Crash Risk Information System) layer functions
// Unified color and severity functions for CRIS layers
function getCrashSeverityColor(severity) {
    switch(severity) {
        case 'K': case 'K_Fatal':
            return [139, 0, 0, 255];      // Dark red for fatal
        case 'A': case 'A_IncapacitatingInjury':
            return [255, 69, 0, 255];     // Red-orange for incapacitating
        case 'B': case 'B_NonIncapacitatingInjury':
            return [255, 140, 0, 255];    // Dark orange for non-incapacitating
        case 'C': case 'C_PossibleInjury':
            return [255, 215, 0, 255];    // Gold for possible injury
        case 'O': case 'O_NoInjury':
            return [50, 205, 50, 255];    // Lime green for no injury
        default:
            return [128, 128, 128, 255];  // Gray for unknown
    }
}

function getRiskLevelColor(riskLevel) {
    switch(riskLevel) {
        case 'VeryHigh': return [139, 0, 0, 255];     // Dark red
        case 'High': return [255, 69, 0, 255];        // Red-orange
        case 'Moderate': return [255, 140, 0, 255];   // Dark orange
        case 'Low': return [255, 215, 0, 255];        // Gold
        case 'VeryLow': return [50, 205, 50, 255];    // Lime green
        default: return [128, 128, 128, 255];         // Gray for unknown
    }
}

// Crash points visualization functions
function getCrashPosition(feature) {
    return [feature.properties.longitude, feature.properties.latitude];
}

function getCrashRadius(feature) {
    const personsInvolved = feature.properties.persons_involved || 1;
    return Math.max(3, Math.min(personsInvolved * 2, 20)); // Scale by persons involved
}

function getCrashColor(feature) {
    return getCrashSeverityColor(feature);
}

function getCrashHeatmapWeight(feature) {
    const severity = feature.properties.severity_code || feature.properties.severity;

    switch(severity) {
        case 'K': case 'K_Fatal': return 10;
        case 'A': case 'A_IncapacitatingInjury': return 8;
        case 'B': case 'B_NonIncapacitatingInjury': return 6;
        case 'C': case 'C_PossibleInjury': return 4;
        case 'O': case 'O_NoInjury': return 1;
        default: return 1;
    }
}

// Risk segments visualization functions
function getRiskSegmentPath(feature) {
    return feature.geometry.coordinates;
}

function getRiskSegmentWidth(feature) {
    const aadt = feature.properties.aadt || 0;
    return Math.max(2, Math.min(aadt / 5000, 12)); // Scale by traffic volume
}

function getRiskSegmentColor(feature) {
    const riskLevel = feature.properties.risk_level;

    switch(riskLevel) {
        case 'VeryHigh': return [139, 0, 0, 255];     // Dark red
        case 'High': return [255, 69, 0, 255];        // Red-orange
        case 'Moderate': return [255, 140, 0, 255];   // Dark orange
        case 'Low': return [255, 215, 0, 255];        // Gold
        case 'VeryLow': return [50, 205, 50, 255];    // Lime green
        default: return [128, 128, 128, 255];         // Gray for unknown
    }
}

// Intersection risks visualization functions
function getIntersectionPosition(feature) {
    return [feature.properties.longitude, feature.properties.latitude];
}

function getIntersectionRadius(feature) {
    const crashCount = feature.properties.crash_count || 1;
    return Math.max(5, Math.min(crashCount * 3, 40)); // Scale by crash count
}

function getIntersectionColor(feature) {
    const riskLevel = feature.properties.risk_level;

    switch(riskLevel) {
        case 'VeryHigh': return [139, 0, 0, 255];     // Dark red
        case 'High': return [255, 69, 0, 255];        // Red-orange
        case 'Moderate': return [255, 140, 0, 255];   // Dark orange
        case 'Low': return [255, 215, 0, 255];        // Gold
        case 'VeryLow': return [50, 205, 50, 255];    // Lime green
        default: return [128, 128, 128, 255];         // Gray for unknown
    }
}

// CRIS click handlers
async function handleCrashClick(info) {
    console.log('handleCrashClick called with:', info);
    console.log('info.object:', info.object);

    if (info.object) {
        const crash = info.object; // Properties are now at root level after dataTransform

        // Create the crash popup data object
        const crashPopupData = {
            crashId: crash.CrashId || 'Unknown',
            crashDate: crash.CrashDate || 'Unknown',
            crashTime: crash.CrashTime || 'Unknown',
            crashDateTime: crash.CrashDateTime || 'Unknown',
            severity: crash.Severity || 'Unknown',
            severityCode: crash.SeverityCode || 'Unknown',
            latitude: crash.Latitude || 0,
            longitude: crash.Longitude || 0,
            personsInvolved: crash.PersonsInvolved || 0,
            vehiclesInvolved: crash.VehiclesInvolved || 0,
            fatalCount: crash.FatalCount || 0,
            injuryCount: crash.InjuryCount || 0,
            weatherCondition: crash.WeatherCondition || 'Unknown',
            lightCondition: crash.LightCondition || 'Unknown',
            surfaceCondition: crash.SurfaceCondition || 'Unknown',
            roadwayId: crash.RoadwayId || '',
            contributingFactors: Array.isArray(crash.ContributingFactors) ? crash.ContributingFactors : []
        };

        // Import and use the crash popup module
        try {
            const crashPopupModule = await import('./crashPopup.js');
            crashPopupModule.showCrashPopup(crashPopupData);
        } catch (error) {
            console.error('Error showing crash popup:', error);
            // Fallback to alert if popup fails
            alert(`Crash Report #${crashPopupData.crashId}
Date: ${crashPopupData.crashDateTime}
Severity: ${crashPopupData.severity} (${crashPopupData.severityCode})
Persons: ${crashPopupData.personsInvolved}, Vehicles: ${crashPopupData.vehiclesInvolved}`);
        }
    }
}

function handleIntersectionClick(info) {
    console.log('handleIntersectionClick called with:', info);

    if (info.object) {
        const intersection = info.object; // Properties are now at root level after dataTransform

        const recentCrashes = intersection.recent_crashes || [];
        const crashesList = recentCrashes.length > 0
            ? recentCrashes.map(c => `• ${c.crash_date}: ${c.severity} (${c.persons_involved} persons)`).join('\n')
            : 'No recent crash details available';

        const message = `Intersection Risk Analysis
━━━━━━━━━━━━━━━━━━━━━━━━━━━━
Risk Level: ${intersection.risk_level}
Risk Score: ${intersection.risk_score?.toFixed(3) || 'N/A'}
Total Crashes: ${intersection.crash_count}

Crash Breakdown:
• Fatal: ${intersection.fatal_crashes || 0}
• Injury: ${intersection.injury_crashes || 0}
• Property Damage: ${intersection.property_damage_crashes || 0}

Recent Crashes:
${crashesList}

Location: ${intersection.latitude?.toFixed(6)}, ${intersection.longitude?.toFixed(6)}`;

        alert(message);
    }
}

function handleRiskSegmentClick(info) {
    console.log('handleRiskSegmentClick called with:', info);

    if (info.object) {
        const segment = info.object; // Direct access to deck.gl data, no .properties
        console.log('Segment data:', segment);

        // Try to use Blazor popup if available
        console.log('Checking for crisRoadSegmentPopupInstance:', window.crisRoadSegmentPopupInstance);
        if (window.crisRoadSegmentPopupInstance) {
            // Convert RiskLevel string to enum integer
            const convertRiskLevel = (riskLevelStr) => {
                switch(riskLevelStr) {
                    case 'VeryLow': return 1;
                    case 'Low': return 2;
                    case 'Moderate': return 3;
                    case 'High': return 4;
                    case 'VeryHigh': return 5;
                    default: return 2; // Default to Low
                }
            };

            // Convert KabcoSeverity string to enum integer
            const convertKabcoSeverity = (severityStr) => {
                switch(severityStr) {
                    case 'Unknown': return 0;
                    case 'K_Fatal': case 'K': return 1;
                    case 'A_IncapacitatingInjury': case 'A': return 2;
                    case 'B_NonIncapacitatingInjury': case 'B': return 3;
                    case 'C_PossibleInjury': case 'C': return 4;
                    case 'O_NoInjury': case 'O': return 5;
                    default: return 0; // Default to Unknown
                }
            };

            // Convert crash records to have proper enum values and DateTime format
            const convertCrashRecords = (crashes) => {
                if (!crashes || !Array.isArray(crashes)) return [];
                return crashes.map(crash => ({
                    ...crash,
                    CrashId: crash.CrashId || '',
                    CrashDateTime: crash.CrashDate || '', // Map CrashDate to CrashDateTime
                    Severity: convertKabcoSeverity(crash.Severity || crash.severity), // Capital S for C# property
                    // Also convert person injury severities if they exist
                    Persons: crash.Persons ? crash.Persons.map(person => ({
                        ...person,
                        InjurySeverity: convertKabcoSeverity(person.InjurySeverity || person.injurySeverity) // Capital I for C# property
                    })) : [],
                    // Map Vehicles properly for C# model
                    Vehicles: crash.Vehicles || []
                }));
            };

            const segmentData = {
                segmentId: segment.SegmentId || 'unknown',
                riskScore: segment.RiskScore || 0,
                riskLevel: convertRiskLevel(segment.RiskLevel),
                crashCount: segment.CrashCount || 0,
                aadt: segment.Aadt || null,
                segmentLength: segment.SegmentLength || 0,
                startLatitude: segment.StartLatitude || info.coordinate[1],
                startLongitude: segment.StartLongitude || info.coordinate[0],
                endLatitude: segment.EndLatitude || info.coordinate[1],
                endLongitude: segment.EndLongitude || info.coordinate[0],
                roadName: segment.RoadName || 'Unknown Road',
                crashesPerMilePerYear: segment.CrashesPerMile || 0,
                fatalCrashCount: segment.FatalCrashes || 0,
                seriousInjuryCrashCount: segment.InjuryCrashes || 0,
                meetsCrashFrequencyThreshold: (segment.CrashesPerMile || 0) > 5.0,
                meetsSeverityThreshold: (segment.FatalCrashes || 0) >= 1 || (segment.InjuryCrashes || 0) >= 3,
                meetsTrafficVolumeThreshold: (segment.Aadt || 0) > 15000,
                hasDrainageRisk: segment.SlopePercentage > 5.0 || false,
                hasEnvironmentalRisk: false, // Will be populated from enhanced data
                slopePercentage: segment.SlopePercentage || 0,
                environmentalFactors: {
                    slopePercentage: segment.SlopePercentage || 0,
                    wetSurfaceCrashes: segment.WetSurfaceCrashes || 0,
                    icySurfaceCrashes: segment.IcySurfaceCrashes || 0,
                    fogRelatedCrashes: segment.FogRelatedCrashes || 0,
                    hydroplaningIncidents: segment.HydroplaningIncidents || 0,
                    hasDrainageIssues: (segment.SlopePercentage || 0) > 5.0 || (segment.HydroplaningIncidents || 0) > 0
                },
                recentCrashes: convertCrashRecords(segment.RecentCrashes || [])
            };

            console.log('Calling Blazor popup with data:', segmentData);
            window.crisRoadSegmentPopupInstance.invokeMethodAsync('ShowPopupFromJS', segmentData)
                .then(() => console.log('Blazor popup called successfully'))
                .catch(error => console.error('Error calling Blazor popup:', error));
        } else {
            // Fallback to alert if Blazor popup not available
            const riskScore = typeof segment.RiskScore === 'number'
                ? segment.RiskScore.toFixed(3)
                : 'N/A';
            const crashCount = segment.CrashCount || 0;
            const aadt = segment.Aadt || 'Unknown';
            const crashesPerMile = typeof segment.CrashesPerMile === 'number'
                ? segment.CrashesPerMile.toFixed(2)
                : 'N/A';

            const message = `Risk Segment Analysis
━━━━━━━━━━━━━━━━━━━
Risk Level: ${segment.RiskLevel}
Risk Score: ${riskScore}
Crash Count: ${crashCount}
AADT: ${aadt}
Crashes/Mile: ${crashesPerMile}

Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;

            alert(message);
        }
    }
}

function handleIntersectionRiskClick(info) {
    if (info.object) {
        const intersection = info.object; // Direct access to deck.gl data, no .properties
        const riskScore = typeof intersection.RiskScore === 'number'
            ? intersection.RiskScore.toFixed(3)
            : 'N/A';
        const crashCount = intersection.CrashCount || 0;
        const roads = intersection.IntersectingRoads || [];
        const roadsText = Array.isArray(roads) && roads.length > 0
            ? roads.join(' & ')
            : 'Unknown roads';

        const message = `High-Risk Intersection
━━━━━━━━━━━━━━━━━━━━
Risk Level: ${intersection.RiskLevel}
Risk Score: ${riskScore}
Crash Count: ${crashCount}
Intersecting Roads: ${roadsText}

Coordinates: ${info.coordinate[1].toFixed(6)}, ${info.coordinate[0].toFixed(6)}`;

        alert(message);
    }
}

// Export for debugging
export function getIntegratedMapInstance() {
    return integratedMapInstance;
}