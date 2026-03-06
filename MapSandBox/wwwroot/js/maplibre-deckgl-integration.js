// MapLibre + deck.gl integration for Blazor
import { logError, measurePerformance, initializeErrorTracking } from './error-reporting.js';
import { createLegend, removeLegend, showParcelTooltip, hideParcelTooltip, markTooltipShown, createStatsPanel, removeStatsPanel } from './parcelTripUI.js';

let integratedMapInstance = null;
let maplibreMap = null;
let deckOverlay = null;

// Initialize error tracking when module loads
initializeErrorTracking();


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
            layers: deckLayers,
            getCursor: ({isDragging, isHovering}) => {
                let cursor;
                if (isDragging) {
                    cursor = 'grabbing';
                } else if (isHovering) {
                    cursor = 'pointer';
                } else {
                    cursor = 'grab';
                }

                // Manually set cursor on MapLibre canvas (Solution from GitHub discussion #5893)
                if (maplibreMap && maplibreMap.getCanvas) {
                    maplibreMap.getCanvas().style.cursor = cursor;
                }

                return cursor;
            }
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
    return measurePerformance('updateIntegratedMapLayers', () => {
        console.log('[MapLibre-JS] updateIntegratedMapLayers called with:', layers);

        try {
        // Comprehensive validation
        if (!mapInstance) {
            console.error('[MapLibre-JS] No map instance provided');
            return;
        }

        if (!mapInstance.maplibre) {
            console.error('[MapLibre-JS] MapLibre instance not available in map instance');
            return;
        }

        if (!Array.isArray(layers)) {
            console.error('[MapLibre-JS] Layers is not an array:', typeof layers);
            return;
        }

        console.log(`[MapLibre-JS] Processing ${layers.length} layers for update`);

        // Handle MapLibre raster layers and get deck.gl layers
        const deckLayers = createLayersFromConfig(layers, mapInstance.maplibre);
        console.log(`[MapLibre-JS] Created ${deckLayers.length} deck.gl layers:`, deckLayers.map(l => `${l.id}(${l.constructor.name})`));

        // Update deck.gl overlay if it exists
        if (mapInstance.deckOverlay) {
            console.log('[MapLibre-JS] Updating deck.gl overlay with new layers');

            // Validate deck.gl overlay before updating
            if (typeof mapInstance.deckOverlay.setProps !== 'function') {
                console.error('[MapLibre-JS] deck.gl overlay setProps method not available');
                return;
            }

            mapInstance.deckOverlay.setProps({ layers: deckLayers });
            console.log('[MapLibre-JS] deck.gl overlay updated successfully');

            // Force a redraw with validation
            setTimeout(() => {
                try {
                    if (mapInstance.maplibre && typeof mapInstance.maplibre.triggerRepaint === 'function') {
                        mapInstance.maplibre.triggerRepaint();
                        console.log('[MapLibre-JS] Map repaint triggered');
                    }
                } catch (repaintError) {
                    console.error('[MapLibre-JS] Error triggering map repaint:', repaintError);
                }
            }, 100);
        } else {
            console.warn('[MapLibre-JS] deck.gl overlay not available, skipping deck layer update');
        }

        // Handle layer visibility changes for MapLibre raster layers
        console.log('[MapLibre-JS] Processing raster tile layers...');
        let rasterLayerCount = 0;
        layers.forEach(layerConfig => {
            try {
                if (layerConfig.type && layerConfig.type.toLowerCase() === 'rastertile') {
                    console.log(`[MapLibre-JS] Updating raster layer '${layerConfig.id}' visibility: ${layerConfig.visible}`);
                    updateMapLibreRasterLayerVisibility(mapInstance.maplibre, layerConfig.id, layerConfig.visible);
                    rasterLayerCount++;
                }
            } catch (rasterError) {
                console.error(`[MapLibre-JS] Error updating raster layer '${layerConfig.id}':`, rasterError);
            }
        });
        console.log(`[MapLibre-JS] Processed ${rasterLayerCount} raster tile layers`);

        // Toggle trip generation UI components based on layer visibility
        const tripLayer = layers.find(l => l.id === 'wp-parcels-trips');
        if (tripLayer) {
            if (tripLayer.visible) {
                createLegend();
                // Fetch GeoJSON and create stats panel if not already showing
                if (!document.getElementById('trip-stats-panel')) {
                    fetch(tripLayer.dataUrl || '/willow-park-parcels-with-trips.geojson')
                        .then(r => r.json())
                        .then(data => createStatsPanel(data))
                        .catch(err => console.error('Failed to load trip stats:', err));
                }
            } else {
                removeLegend();
                removeStatsPanel();
                hideParcelTooltip();
            }
        }

        console.log('[MapLibre-JS] Layer update completed successfully');
        } catch (error) {
            console.error('[MapLibre-JS] Critical error updating integrated map layers:', error);
            console.error('[MapLibre-JS] Error stack:', error.stack);

            // Log structured error for debugging
            logError('updateIntegratedMapLayers', error, {
                layerCount: layers?.length,
                mapInstanceValid: !!mapInstance,
                maplibreValid: !!(mapInstance?.maplibre),
                deckOverlayValid: !!(mapInstance?.deckOverlay)
            });

            // Continue execution without crashing the app
            console.log('[MapLibre-JS] Continuing execution after error');
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
    console.log('[MapLibre-JS] createLayersFromConfig starting...');

    const deckLayers = [];
    const maplibreRasterLayers = [];

    try {
        if (!Array.isArray(layerConfigs)) {
            console.error('[MapLibre-JS] layerConfigs is not an array:', typeof layerConfigs);
            return deckLayers;
        }

        console.log(`[MapLibre-JS] Processing ${layerConfigs.length} layer configurations`);
        console.log(`[MapLibre-JS] Layer IDs:`, layerConfigs.map(l => l.id));

        layerConfigs.forEach((config, index) => {
            try {
                // Validate layer config
                if (!config) {
                    console.warn(`[MapLibre-JS] Layer config at index ${index} is null/undefined`);
                    return;
                }

                if (!config.id) {
                    console.warn(`[MapLibre-JS] Layer config at index ${index} missing ID:`, config);
                    return;
                }

                if (!config.type) {
                    console.warn(`[MapLibre-JS] Layer '${config.id}' missing type:`, config);
                    return;
                }

                console.log(`[MapLibre-JS] Processing layer ${config.id}: visible=${config.visible}, type=${config.type}`);

                // Special debug for NOAA layers
                if (config.id.includes('noaa-rainfall')) {
                    console.log(`[MapLibre-JS] NOAA layer found: ${config.id}, visible: ${config.visible}, type: ${config.type}`);
                }

                if (!config.visible) {
                    console.log(`[MapLibre-JS] Skipping invisible layer: ${config.id}`);
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

                const layerType = config.type.toLowerCase();
                console.log(`[MapLibre-JS] Processing visible layer ${config.id} with type: ${layerType}`);

                switch (layerType) {
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
                        getWidth: (d, {hovered}) => {
                            const baseWidth = getTrafficWidth(d);
                            return hovered ? baseWidth * 1.5 : baseWidth;
                        },
                        // Simplified configuration to match deck.gl docs
                        widthScale: 1,
                        widthMinPixels: 1,
                        widthMaxPixels: 6, // Reduced for less prominent display
                        rounded: true,
                        pickable: true,
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
            case 'pathlayer':
                // Handle CRIS risk segments specifically
                if (config.id === 'cris-risk-segments') {
                    deckLayers.push(new deck.PathLayer({
                        id: config.id,
                        data: config.dataUrl,
                        getPath: d => d.Coordinates,
                        getWidth: d => Math.max(2, (d.Aadt || 100) / 1000),
                        getColor: d => getRiskLevelColor(d.RiskLevel),
                        widthMinPixels: 1,
                        widthMaxPixels: 8,
                        pickable: true,
                        autoHighlight: true,
                        highlightColor: [255, 255, 255, 128],
                        onHover: (info) => {
                            console.log('CRIS risk segments hover:', !!info.object);
                            return true;
                        },
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
            case 'scatterplotlayer':
                // Handle CRIS crash clusters with ScatterplotLayer
                if (config.id === 'cris-crashes') {
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        dataTransform: (data) => {
                            try {
                                console.log('Processing clustered crash data for ScatterplotLayer...');

                                // Data is already in cluster format
                                const clusters = data.map(cluster => ({
                                    ...cluster,
                                    position: cluster.Position,
                                    crashCount: cluster.CrashCount,
                                    maxSeverity: cluster.MaxSeverity,
                                    totalPersonsInvolved: cluster.TotalPersonsInvolved
                                }));

                                console.log(`Processed ${clusters.length} crash clusters for ScatterplotLayer`);
                                return clusters;
                            } catch (error) {
                                console.error('Error processing clustered crash data:', error);
                                return [];
                            }
                        },
                        getPosition: d => d.position,
                        getRadius: d => Math.max(4, Math.sqrt(d.crashCount) * 4), // Size based on crash count
                        getFillColor: d => getSeverityColor(d.maxSeverity), // Color based on max severity
                        getLineColor: [0, 0, 0, 255], // Black outline
                        getLineWidth: 1,
                        radiusMinPixels: 4,
                        radiusMaxPixels: 25,
                        stroked: true,
                        pickable: true,
                        autoHighlight: true,

                        // Click handling for clusters
                        onClick: handleCrashClusterClick
                    }));
                } else {
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config)
                    }));
                }
                break;
            case 'gridlayer':
                // Handle CRIS crash points with GridLayer aggregation
                if (config.id === 'cris-crashes') {
                    deckLayers.push(new deck.HexagonLayer({
                        id: config.id,
                        data: config.dataUrl,
                        dataTransform: (data) => {
                            try {
                                console.log('Processing unique crash data for grid aggregation...');

                                // Data is already unique and in deck.gl JSON format - just add position field
                                const crashes = data.map(crash => ({
                                    ...crash,
                                    position: crash.Coordinates, // deck.gl format already has Coordinates
                                    persons_involved: crash.PersonsInvolved || 1
                                }));

                                console.log(`✅ CRIS Unique Crashes processed for grid: ${crashes.length} crashes`);
                                return crashes;
                            } catch (error) {
                                console.error('Error processing unique crash data for grid:', error);
                                return [];
                            }
                        },
                        getPosition: d => d.position,
                        radius: 25, // Half the size of previous grid cells (25 meters)
                        gpuAggregation: false, // Use CPU aggregation to get source points
                        extruded: true,
                        pickable: true,
                        autoHighlight: true,

                        // Aggregation settings
                        getColorWeight: d => {
                            // Assign severity weights: K=5, A=4, B=3, C=2, O=1
                            const severityWeights = { 'K': 5, 'A': 4, 'B': 3, 'C': 2, 'O': 1 };
                            const weight = severityWeights[d.SeverityCode] || 1;
                            console.log(`Crash ${d.CrashId}: Severity ${d.SeverityCode} → Weight ${weight}`);
                            return weight;
                        },
                        getElevationWeight: d => d.persons_involved || 1, // Height based on persons involved
                        colorAggregation: 'MAX', // Use highest severity in cell
                        elevationAggregation: 'SUM', // Sum persons involved per cell

                        // Styling - use quantize for discrete severity levels
                        elevationRange: [0, 10], // Cap maximum height at 10 units
                        colorScale: 'quantize',
                        colorDomain: [1, 5], // Map severity weights 1-5
                        colorRange: [
                            [40, 167, 69],    // Green - O (No injury) = 1 (#28a745)
                            [23, 162, 184],   // Teal - C (Possible) = 2 (#17a2b8)
                            [255, 193, 7],    // Yellow - B (Non-incap) = 3 (#ffc107)
                            [253, 126, 20],   // Orange - A (Incap) = 4 (#fd7e14)
                            [220, 53, 69]     // Red - K (Fatal) = 5 (#dc3545)
                        ],

                        onHover: (info) => {
                            if (info.object) {
                                const severityNames = { 1: 'No Injury', 2: 'Possible', 3: 'Non-Incap', 4: 'Incapacitating', 5: 'Fatal' };
                                console.log('Grid cell hover:', {
                                    crashCount: info.object.points?.length || 0,
                                    highestSeverity: severityNames[info.object.colorValue] || 'Unknown',
                                    severityValue: info.object.colorValue,
                                    personsInvolved: info.object.elevationValue,
                                    position: info.object.position
                                });
                            }
                            return true;
                        },

                        onClick: (info) => {
                            if (info.object && info.object.colorValue > 0) {
                                console.log('Grid cell clicked - full object:', info.object);
                                console.log('Points in cell:', info.object.points);
                                console.log('Point indices:', info.object.pointIndices);

                                // With CPU aggregation, we get the source points directly!
                                const crashesInCell = info.object.points || [];

                                if (crashesInCell.length > 0) {
                                    console.log('Sample crash object:', crashesInCell[0]);
                                    console.log('All crash IDs:', crashesInCell.map(c => c.CrashId || c.crash_id));

                                    // Calculate cell center from the first point or use centroid
                                    const cellCenter = info.object.position || [
                                        crashesInCell.reduce((sum, p) => sum + p.position[0], 0) / crashesInCell.length,
                                        crashesInCell.reduce((sum, p) => sum + p.position[1], 0) / crashesInCell.length
                                    ];

                                    console.log(`Found ${crashesInCell.length} crashes in grid cell`);

                                    // Convert to cluster popup format - data is already processed
                                    const clusterData = {
                                        crashes: crashesInCell.map((crash, index) => {
                                            console.log(`Processing crash ${index}:`, crash);
                                            return {
                                                crashId: crash.CrashId || crash.crash_id || `unknown-${index}`,
                                                crashDate: crash.CrashDate || crash.crash_date || '',
                                                crashTime: crash.CrashTime || crash.crash_time || '',
                                                crashDateTime: crash.CrashDateTime || crash.crash_datetime || '',
                                                severity: crash.Severity || crash.severity || '',
                                                severityCode: crash.SeverityCode || crash.severity_code || '',
                                                latitude: crash.position[1],
                                                longitude: crash.position[0],
                                                personsInvolved: crash.PersonsInvolved || crash.persons_involved || 0,
                                                vehiclesInvolved: crash.VehiclesInvolved || crash.vehicles_involved || 0,
                                                fatalCount: crash.FatalCount || crash.fatal_count || 0,
                                                injuryCount: crash.InjuryCount || crash.injury_count || 0,
                                                weatherCondition: crash.WeatherCondition || crash.weather_condition || '',
                                                lightCondition: crash.LightCondition || crash.light_condition || '',
                                                surfaceCondition: crash.SurfaceCondition || crash.surface_condition || '',
                                                roadwayId: crash.RoadwayId || crash.roadway_id || '',
                                                contributingFactors: crash.ContributingFactors || crash.contributing_factors || []
                                            };
                                        }),
                                        latitude: cellCenter[1],
                                        longitude: cellCenter[0]
                                    };

                                    console.log('Final cluster data:', clusterData);

                                    // Show the crash cluster popup
                                    if (window.showCrashCluster) {
                                        window.showCrashCluster(clusterData);
                                    }
                                }
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
                        autoHighlight: true,
                        highlightColor: [255, 255, 255, 128],
                        onClick: handleIntersectionClick,
                        onDataLoad: data => {
                            if (data && data.features) {
                                console.log(`✅ CRIS Intersections loaded ${data.features.length} features`);
                                console.log('Sample intersection:', data.features[0]);
                            }
                        }
                    }));
                } else if (config.id === 'noaa-rainfall-parker-points') {
                    console.log(`⚡ ENTERING NOAA ScatterplotLayer case for: ${config.id}`);
                    console.log(`🔍 NOAA ScatterplotLayer config.dataUrl: ${config.dataUrl}`);
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        getPosition: d => d.geometry.coordinates,
                        getRadius: d => Math.max(3, (d.properties.rainfall - 458) * 2), // Scale 458-473 to 0-30
                        getFillColor: d => {
                            // Blue gradient based on rainfall
                            const rainfall = d.properties.rainfall;
                            if (rainfall < 463) return [0, 100, 255, 180]; // Dark blue - low
                            if (rainfall < 467) return [0, 150, 255, 180]; // Medium blue
                            if (rainfall < 470) return [50, 200, 255, 180]; // Light blue
                            return [100, 220, 255, 180]; // Very light blue - high
                        },
                        radiusMinPixels: 2,
                        radiusMaxPixels: 8,
                        pickable: true,
                        stroked: true,
                        getLineColor: [0, 0, 0, 128],
                        lineWidthMinPixels: 1,
                        onDataLoad: data => {
                            console.log(`📊 NOAA ScatterplotLayer data load event:`, typeof data, data);
                            if (data && data.features) {
                                console.log(`✅ ScatterplotLayer ${config.id} loaded ${data.features.length} features`);
                                console.log('Sample rainfall point:', data.features[0]);
                                const minRainfall = Math.min(...data.features.map(f => f.properties.rainfall));
                                const maxRainfall = Math.max(...data.features.map(f => f.properties.rainfall));
                                console.log(`Rainfall range: ${minRainfall} - ${maxRainfall}`);
                            } else {
                                console.error(`❌ ScatterplotLayer ${config.id} data load failed or no features:`, data);
                            }
                        },
                        onError: error => {
                            console.error(`❌ ScatterplotLayer ${config.id} error:`, error);
                        }
                    }));
                } else {
                    console.log(`⚡ ENTERING Generic ScatterplotLayer case for: ${config.id}`);
                    console.log(`🔍 Generic ScatterplotLayer case hit for: ${config.id}, dataUrl: ${config.dataUrl}`);
                    deckLayers.push(new deck.ScatterplotLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config),
                        onDataLoad: data => {
                            console.log(`📊 Generic ScatterplotLayer data load event for ${config.id}:`, typeof data, data);
                            if (data && data.features) {
                                console.log(`✅ ScatterplotLayer ${config.id} loaded ${data.features.length} features`);
                                console.log('Sample feature:', data.features[0]);
                            } else {
                                console.error(`❌ Generic ScatterplotLayer ${config.id} data load failed:`, data);
                            }
                        },
                        onError: error => {
                            console.error(`❌ Generic ScatterplotLayer ${config.id} error:`, error);
                        }
                    }));
                }
                break;
            case 'heatmaplayer':
                // Handle NOAA rainfall heatmap
                if (config.id === 'noaa-rainfall-parker-heatmap') {
                    console.log(`🔍 NOAA HeatmapLayer config.dataUrl: ${config.dataUrl}`);
                    deckLayers.push(new deck.HeatmapLayer({
                        id: config.id,
                        data: config.dataUrl,
                        getPosition: d => d.geometry.coordinates,
                        getWeight: d => (d.properties.rainfall - 450) / 50, // Normalize 450-500 to 0-1
                        radiusMeters: 1000, // Use world-space radius (1km) for consistent heatmap at all zoom levels
                        intensity: 2,
                        threshold: 0.01,
                        colorRange: [
                            [0, 162, 255, 255],   // Blue
                            [0, 200, 255, 255],   // Light blue
                            [30, 230, 255, 255],  // Cyan
                            [80, 255, 220, 255],  // Light cyan
                            [150, 255, 180, 255], // Light green
                            [255, 255, 150, 255]  // Light yellow
                        ],
                        pickable: false,
                        opacity: 0.8,
                        onDataLoad: data => {
                            console.log(`🔥 NOAA HeatmapLayer data load event:`, typeof data, data);
                            if (data && data.features) {
                                console.log(`✅ HeatmapLayer ${config.id} loaded ${data.features.length} features`);
                                console.log('Sample rainfall point:', data.features[0]);
                                const sample = data.features[0];
                                const weight = (sample.properties.rainfall - 450) / 50;
                                console.log(`Weight calculation: ${sample.properties.rainfall} → ${weight}`);
                            } else {
                                console.error(`❌ HeatmapLayer ${config.id} data load failed or no features:`, data);
                            }
                        },
                        onError: error => {
                            console.error(`❌ HeatmapLayer ${config.id} error:`, error);
                        }
                    }));
                } else {
                    deckLayers.push(new deck.HeatmapLayer({
                        id: config.id,
                        data: config.dataUrl,
                        ...getLayerProperties(config)
                    }));
                }
                break;
                }
            } catch (layerError) {
                console.error(`[MapLibre-JS] Error processing layer config at index ${index}:`, layerError);
                console.error(`[MapLibre-JS] Problematic config:`, config);
            }
        });

        // Add MapLibre raster layers if maplibreMap is provided
        if (maplibreMap && maplibreRasterLayers.length > 0) {
            addMapLibreRasterLayers(maplibreMap, maplibreRasterLayers);
        }

        console.log(`[MapLibre-JS] createLayersFromConfig completed: ${deckLayers.length} deck layers, ${maplibreRasterLayers.length} raster layers`);
        return deckLayers;
    } catch (error) {
        console.error('[MapLibre-JS] Critical error in createLayersFromConfig:', error);
        console.error('[MapLibre-JS] Error stack:', error.stack);
        return deckLayers; // Return whatever we managed to create
    }
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
            properties.autoHighlight = true;
            properties.highlightColor = [255, 255, 255, 150];
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
            properties.autoHighlight = true;
            properties.highlightColor = [255, 255, 255, 150];
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
            properties.highlightColor = [255, 255, 255, 100];
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
            properties.highlightColor = [255, 255, 255, 100];
            properties.onClick = handleSoilUnitClick;
            break;
        case 'cris-crashes':
            properties.radiusMinPixels = 5;
            properties.radiusMaxPixels = 20;
            properties.radiusScale = 100;
            properties.getPosition = d => d.Coordinates;
            properties.getRadius = d => d.PersonsInvolved || 1;
            properties.getFillColor = d => getCrashSeverityColor(d.Severity);
            properties.filled = true;
            properties.stroked = false;
            properties.pickable = true;
            properties.onClick = handleCrashClick;
            break;
        case 'cris-risk-segments':
            properties.getPath = d => d.Coordinates;
            properties.getWidth = d => Math.max(2, (d.Aadt || 100) / 1000);
            properties.getColor = d => getRiskLevelColor(d.RiskLevel);
            properties.widthMinPixels = 1;
            properties.widthMaxPixels = 8;
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
            properties.autoHighlight = true;
            properties.highlightColor = [255, 255, 255, 128];
            properties.onClick = handleIntersectionRiskClick;
            break;
        case 'txdot-city-boundaries':
            properties.filled = true;
            properties.stroked = true;
            properties.getFillColor = [100, 150, 200, 30]; // Light blue, semi-transparent
            properties.getLineColor = [0, 100, 200, 255];  // Darker blue border
            properties.getLineWidth = 2;
            properties.lineWidthMinPixels = 1;
            properties.lineWidthMaxPixels = 3;
            properties.opacity = 0.6;
            properties.pickable = true;
            properties.autoHighlight = true;
            properties.onHover = handleCityBoundaryHover;
            break;
        case 'wp-parcels-trips':
            properties.filled = true;
            properties.stroked = true;
            properties.getFillColor = d => {
                const trips = (d.properties && d.properties.daily_trips) || 0;
                if (trips === 0) return [200, 200, 200, 80];      // Gray - no trips
                if (trips < 10) return [65, 182, 196, 160];       // Teal - residential
                if (trips < 50) return [255, 183, 77, 180];       // Orange - moderate
                if (trips < 200) return [255, 112, 67, 200];      // Red-Orange - high
                return [211, 47, 47, 220];                         // Red - very high
            };
            properties.getLineColor = [50, 50, 50, 200];
            properties.getLineWidth = 1;
            properties.lineWidthMinPixels = 1;
            properties.opacity = 0.75;
            properties.pickable = true;
            properties.autoHighlight = true;
            properties.highlightColor = [255, 255, 255, 150];
            properties.onClick = handleParcelTripClick;
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
        maxZoom: 14,  // Match actual tile coverage (tiles exist for zoom 12-14 only)
        tileSize: 512,
        maxCacheSize: 10 * 1024 * 1024, // 10MB cache
        maxCacheByteSize: 50 * 1024 * 1024, // 50MB total
        refinementStrategy: 'never',
        
        // Enable picking on the TileLayer itself
        pickable: true,
        
        // TileLayer-level event handlers (deck.gl routes sublayer events here)
        
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
            if (tileZ < 12 || tileZ > 14) {  // Match actual tile coverage
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
        const props = road.properties;
        const name = props.fullName || props.FULLNAME || 'Unknown';
        const type = props.roadType || props.RTTYP || 'Unknown';
        const roadType = getRoadTypeName(type);

        alert(`Road: ${name}\nType: ${roadType}\nRoute Type Code: ${type}`);
    }
}

function handleParcelTripClick(info) {
    if (info.object) {
        markTooltipShown();
        showParcelTooltip(info.object.properties, info.x, info.y);
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
    return 1 + (ratio * 5); // 1-6 pixel width range
}

function handleTrafficRoadClick(info) {
    if (info.object) {
        const road = info.object;
        // All GeoJSON files now use camelCase properties
        const roadName = road.properties.fullName;
        const roadType = road.properties.roadType;
        const linearId = road.properties.linearId;
        const mtfcc = road.properties.mtfcc;
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
                    linearId,
                    mtfcc
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

// Get severity color for crash clusters
function getSeverityColor(severityCode) {
    switch(severityCode?.toUpperCase()) {
        case 'K': return [220, 53, 69];     // Red - Fatal (#dc3545)
        case 'A': return [253, 126, 20];    // Orange - Incapacitating (#fd7e14)
        case 'B': return [255, 193, 7];     // Yellow - Non-incapacitating (#ffc107)
        case 'C': return [23, 162, 184];    // Teal - Possible injury (#17a2b8)
        case 'O': return [40, 167, 69];     // Green - No injury (#28a745)
        default: return [40, 167, 69];      // Default to green
    }
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
            contributingFactors: Array.isArray(crash.ContributingFactors) ? crash.ContributingFactors : [],
            slopeAtLocation: crash.SlopeAtLocation || 0,
            slopePercentage: crash.SlopePercentage || 0,
            slopeCategory: crash.SlopeCategory || ''
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

async function handleCityBoundaryHover(info) {
    if (info.object && info.object.properties) {
        const city = info.object.properties;
        const cityName = city.CITY_NM || 'Unknown City';

        // Show tooltip at cursor position
        try {
            const cityBoundaryPopupModule = await import('./cityBoundaryPopup.js');
            cityBoundaryPopupModule.showCityBoundaryTooltip(cityName, info.x, info.y);
        } catch (error) {
            console.error('Error showing city boundary tooltip:', error);
        }
    } else {
        // Hide tooltip when not hovering over a city
        try {
            const cityBoundaryPopupModule = await import('./cityBoundaryPopup.js');
            cityBoundaryPopupModule.hideCityBoundaryTooltip();
        } catch (error) {
            console.error('Error hiding city boundary tooltip:', error);
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
                    PersonsInvolved: crash.PersonsInvolved || 0, // Ensure PersonsInvolved is mapped
                    VehiclesInvolved: crash.VehiclesInvolved || 0, // Ensure VehiclesInvolved is mapped (may be 0 if not available)
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

function handleCrashClusterClick(info) {
    console.log('handleCrashClusterClick called with:', info);

    if (info.object) {
        const cluster = info.object; // Direct access to cluster data
        console.log('Cluster data:', cluster);

        // Try to use Blazor popup if available
        console.log('Checking for crashClusterPopupInstance:', window.crashClusterPopupInstance);
        if (window.crashClusterPopupInstance) {
            // Create cluster data for popup - match CrashClusterData format
            const clusterData = {
                Latitude: cluster.position[1],
                Longitude: cluster.position[0],
                Crashes: cluster.Crashes.map(crash => ({
                    CrashId: crash.CrashId,
                    CrashDate: crash.CrashDate,
                    SeverityCode: crash.SeverityCode,
                    PersonsInvolved: crash.PersonsInvolved,
                    VehiclesInvolved: crash.VehiclesInvolved,
                    FatalCount: crash.FatalCount,
                    InjuryCount: crash.InjuryCount,
                    SlopeAtLocation: crash.SlopeAtLocation || 0,
                    SlopePercentage: crash.SlopePercentage || 0,
                    SlopeCategory: crash.SlopeCategory || ''
                }))
            };

            console.log('Calling Blazor popup with cluster data:', clusterData);
            window.crashClusterPopupInstance.invokeMethodAsync('ShowClusterPopupFromJS', clusterData)
                .then(() => console.log('Blazor cluster popup called successfully'))
                .catch(error => console.error('Error calling Blazor cluster popup:', error));
        } else {
            // Fallback to alert if Blazor popup not available
            const crashCount = cluster.crashCount || cluster.Crashes?.length || 1;
            const message = `Crash Cluster
Count: ${crashCount} crashes
Max Severity: ${cluster.maxSeverity}
Location: ${cluster.position[1].toFixed(6)}, ${cluster.position[0].toFixed(6)}`;
            alert(message);
        }
    }
}

// Export for debugging
export function getIntegratedMapInstance() {
    return integratedMapInstance;
}

// Fly to specific coordinates
export function flyToLocation(mapInstance, latitude, longitude, zoom = 16) {
    console.log(`[MapLibre] Flying to: ${latitude}, ${longitude}, zoom: ${zoom}`);

    if (!mapInstance || !mapInstance.maplibre) {
        console.error("[MapLibre] MapLibre instance not available for flyTo");
        return;
    }

    try {
        mapInstance.maplibre.flyTo({
            center: [longitude, latitude], // MapLibre expects [lon, lat]
            zoom: zoom,
            essential: true, // this animation is considered essential with respect to prefers-reduced-motion
            duration: 2000 // Animation duration in milliseconds
        });
        console.log(`[MapLibre] Successfully initiated fly to ${latitude}, ${longitude}`);
    } catch (error) {
        console.error("[MapLibre] Error in flyTo:", error);
        throw error;
    }
}