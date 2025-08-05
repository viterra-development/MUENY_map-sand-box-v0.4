# Progressive Loading Implementation - Phase 1
## From Data Extraction to Production Deployment

This README provides step-by-step instructions for implementing progressive loading of traffic count data using deck.gl's native TileLayer capabilities.

## 🎯 Overview

**What This Implements:**
- Progressive loading of traffic count locations using spatial tiles
- Automatic viewport-based data loading with deck.gl TileLayer
- Zero custom tile management code (leverages native deck.gl features)
- Drop-in replacement for existing traffic-counts layer

**Performance Benefits:**
- Only loads data visible in current viewport
- Automatic caching and memory management
- Smooth panning/zooming with preloaded tiles
- 60fps rendering with WebGL acceleration

## 📋 Prerequisites

- .NET 9.0 SDK
- Node.js (for MapSandBox frontend)
- Access to TCDS traffic data system
- Web server capability for serving static tiles

## 🚀 Step-by-Step Implementation

### Step 1: Data Extraction and Tile Generation

#### 1.1 Run TCDS Importer with Tile Generation
```bash
# Navigate to project root
cd /workspaces/map-sand-box

# Run the TCDS Importer (extracts data + generates tiles)
dotnet run --project TCDS.Importer/TCDS.Importer.csproj
```

**What this does:**
- Extracts traffic count data from TCDS system (max 10 pages)
- Generates individual page JSON files
- Creates consolidated GeoJSON file  
- **NEW**: Generates spatial tiles for progressive loading
- Exports clean GeoJSON for mapping applications

#### 1.2 Verify Tile Generation
After running the importer, check the output:

```bash
# Check that tiles were generated
ls -la TCDS.Importer/Data/tiles/traffic-counts/

# Should see directory structure like:
# 8/128/95.geojson (zoom level 8)
# 10/513/383.geojson (zoom level 10) 
# 12/2052/1532.geojson (zoom level 12)
# 14/8208/6128.geojson (zoom level 14)
```

**Tile Structure:**
```
TCDS.Importer/Data/tiles/traffic-counts/
├── 8/          # County-level summary
├── 10/         # City-level detail  
├── 12/         # Neighborhood-level
└── 14/         # Street-level detail
```

### Step 2: Copy Tiles to Web Application

#### 2.1 Copy Generated Tiles
```bash
# Copy tiles from TCDS.Importer to MapSandBox wwwroot
cp -r TCDS.Importer/Data/tiles MapSandBox/wwwroot/

# Verify tiles are in correct location
ls -la MapSandBox/wwwroot/tiles/traffic-counts/
```

#### 2.2 Set Proper Permissions (Production)
```bash
# Ensure web server can read tile files
chmod -R 644 MapSandBox/wwwroot/tiles/
chmod -R 755 MapSandBox/wwwroot/tiles/*/
```

### Step 3: Test the Implementation

#### 3.1 Run MapSandBox Application
```bash
# Start the development server
dotnet watch --project MapSandBox

# Application will be available at:
# HTTP:  http://localhost:5214
# HTTPS: https://localhost:7067
```

#### 3.2 Verify Progressive Loading
1. **Navigate to MapLibre Home** page (not regular Home)
2. **Look for "Traffic Count Locations"** in layer controls
3. **Zoom to Parker County area** (around coordinates 32.7, -97.6)
4. **Test progressive loading:**
   - Zoom out to level 8: Should see summary points
   - Zoom in to level 12+: Should see detailed locations
   - Pan around: New tiles should load automatically

#### 3.3 Performance Testing
Open browser developer tools and monitor:
- **Network tab**: Should see tile requests as you pan/zoom
- **Memory usage**: Should stay under 100MB
- **Frame rate**: Should maintain 60fps during interactions

### Step 4: Production Deployment

#### 4.1 Build for Production
```bash
# Build optimized release version
dotnet publish MapSandBox -c Release -o ./publish

# Verify tiles are included in publish output
ls -la ./publish/wwwroot/tiles/traffic-counts/
```

#### 4.2 Web Server Configuration

**IIS Configuration (web.config):**
```xml
<configuration>
  <system.webServer>
    <staticContent>
      <!-- Enable compression for GeoJSON tiles -->
      <mimeMap fileExtension=".geojson" mimeType="application/geo+json" />
    </staticContent>
    <httpCompression>
      <dynamicTypes>
        <add mimeType="application/geo+json" enabled="true" />
      </dynamicTypes>
    </httpCompression>
    <!-- Cache tiles for 1 hour -->
    <location path="tiles">
      <system.webServer>
        <staticContent>
          <clientCache cacheControlMode="UseMaxAge" cacheControlMaxAge="01:00:00" />
        </staticContent>
      </system.webServer>
    </location>
  </system.webServer>
</configuration>
```

**Nginx Configuration:**
```nginx
server {
    # Serve tiles with caching
    location /tiles/ {
        expires 1h;
        add_header Cache-Control "public, immutable";
        gzip on;
        gzip_types application/geo+json application/json;
    }
    
    # Main application
    location / {
        proxy_pass http://localhost:5000;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
    }
}
```

#### 4.3 CDN Configuration (Optional)
For high-traffic deployments, serve tiles from CDN:

```javascript
// Update tile URL in MapLibre configuration
data: 'https://your-cdn.com/tiles/traffic-counts/{z}/{x}/{y}.geojson'
```

## 🔧 Configuration Options

### Tile Generation Settings
Modify `SimpleTileGenerator.cs` to adjust:

```csharp
// Zoom levels to generate (currently 8, 10, 12, 14)
await GenerateZoomLevel(validRecords, outputPath, 8, "county-summary");
await GenerateZoomLevel(validRecords, outputPath, 10, "city-level");
await GenerateZoomLevel(validRecords, outputPath, 12, "neighborhood");
await GenerateZoomLevel(validRecords, outputPath, 14, "street-level");

// Geographic bounds (currently Parker County)
var parkerCountyBounds = new
{
    MinLat = 32.6,   // South boundary
    MaxLat = 32.9,   // North boundary  
    MinLng = -98.1,  // West boundary
    MaxLng = -97.5   // East boundary
};
```

### TileLayer Settings
Modify `maplibre-deckgl-integration.js` to adjust:

```javascript
return new deck.TileLayer({
    minZoom: 8,                              // Minimum zoom level
    maxZoom: 16,                             // Maximum zoom level  
    tileSize: 512,                           // Tile size (256 or 512)
    maxCacheSize: 10 * 1024 * 1024,         // 10MB cache
    maxCacheByteSize: 50 * 1024 * 1024,     // 50MB total
    refinementStrategy: 'best-available',    // Loading strategy
});
```

## 📊 Monitoring and Analytics

### Performance Metrics to Track
- **Initial load time**: <2 seconds for first viewport
- **Tile load time**: <500ms per tile
- **Memory usage**: <100MB active tiles  
- **Cache hit rate**: >80% for repeat views
- **Frame rate**: 60fps during interactions

### Browser Developer Tools Monitoring
```javascript
// Monitor tile loading performance
const observer = new PerformanceObserver((list) => {
    for (const entry of list.getEntries()) {
        if (entry.name.includes('/tiles/traffic-counts/')) {
            console.log(`Tile loaded: ${entry.name} in ${entry.duration}ms`);
        }
    }
});
observer.observe({entryTypes: ['resource']});
```

## 🐛 Troubleshooting

### Common Issues

#### 1. Tiles Not Loading
**Symptoms:** Map shows no traffic count data
**Solutions:**
- Verify tiles exist: `ls MapSandBox/wwwroot/tiles/traffic-counts/`
- Check browser network tab for 404 errors
- Ensure web server can serve .geojson files

#### 2. Poor Performance
**Symptoms:** Slow panning/zooming, high memory usage
**Solutions:**
- Reduce `maxCacheSize` in TileLayer config
- Check if too many zoom levels are being generated
- Verify gzip compression is enabled

#### 3. Data Not Updating
**Symptoms:** Old traffic data still showing
**Solutions:**
- Re-run TCDS Importer to regenerate tiles
- Clear browser cache: Ctrl+Shift+R
- Verify tiles were copied to wwwroot correctly

#### 4. Coordinate Issues
**Symptoms:** Traffic points in wrong locations
**Solutions:**
- Verify lat/lng order in GeoJSON (lng, lat)
- Check coordinate system (should be WGS84)
- Validate tile coordinate calculations

### Debug Mode
Add to browser console for detailed logging:
```javascript
// Enable deck.gl debug mode
window.deck = {luma: {log: 3}};

// Monitor TileLayer status
const tileLayer = deck.getLayer('traffic-counts');
console.log('TileLayer cache:', tileLayer._cache);
```

## 🔄 Data Update Process

### Regular Updates (Daily/Weekly)
```bash
# 1. Extract new data
dotnet run --project TCDS.Importer/TCDS.Importer.csproj

# 2. Copy updated tiles  
cp -r TCDS.Importer/Data/tiles MapSandBox/wwwroot/

# 3. Restart application (if needed)
# Most changes are automatically picked up
```

### Automated Pipeline (Production)
```bash
#!/bin/bash
# update-traffic-data.sh

echo "Starting traffic data update..."

# Run TCDS Importer
dotnet run --project TCDS.Importer/TCDS.Importer.csproj

# Copy tiles to production directory
cp -r TCDS.Importer/Data/tiles /var/www/mapsandbox/wwwroot/

# Set permissions
chmod -R 644 /var/www/mapsandbox/wwwroot/tiles/
chmod -R 755 /var/www/mapsandbox/wwwroot/tiles/*/

# Clear CDN cache (if using)
# curl -X POST "https://api.cloudflare.com/client/v4/zones/ZONE_ID/purge_cache"

echo "Traffic data update completed successfully"
```

## 📈 Future Enhancements

### Phase 2: Vector Tiles (When Needed)
- Trigger: >5,000 locations or need better compression
- Implementation: Replace GeoJSON tiles with MVT format
- Benefit: 60-80% smaller file sizes

### Phase 3: PMTiles (Optional)  
- Trigger: Need single-file deployment or offline capability
- Implementation: Generate single PMTiles file
- Benefit: Maximum performance, offline support

### Real-time Updates
- WebSocket integration for live traffic data
- Incremental tile updates
- Event-driven tile invalidation

## ✅ Success Criteria

**Implementation is successful when:**
- ✅ Traffic count points load progressively based on zoom level
- ✅ Smooth panning/zooming with no loading delays
- ✅ Memory usage stays under 100MB
- ✅ Initial viewport loads in <2 seconds  
- ✅ Tile requests only occur for visible areas
- ✅ 60fps performance during interactions
- ✅ Works across different zoom levels (8-16)

**Production deployment is successful when:**
- ✅ Tiles are properly served with compression and caching
- ✅ CDN/caching strategy is implemented (if applicable)  
- ✅ Data update process is documented and automated
- ✅ Performance monitoring is in place
- ✅ Troubleshooting procedures are established

## 🎉 Congratulations!

You now have a production-ready progressive loading implementation that:
- **Leverages deck.gl's native capabilities** instead of custom code
- **Provides excellent performance** with automatic optimizations  
- **Scales efficiently** as your dataset grows
- **Maintains the same user experience** with better performance
- **Is easy to maintain** with minimal custom code

This implementation transforms what could have been weeks of custom development into a simple configuration change while providing superior performance and maintainability.