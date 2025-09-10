# DEM Tile Processing Pipeline Validation Plan

## Overview
We need to systematically validate each step of the pipeline from USGS source files to MapLibre display to identify where the TWI tile visualization is failing.

**Pipeline**: USGS DEM → gdal_translate scaling → gdal2tiles → PNG tiles → Azure Storage → Front Door → MapLibre

## Step-by-Step Validation

### **Step 1: Source Data Validation**
**Goal**: Confirm USGS DEM files contain expected data and coverage

**Validation Commands**:
```bash
# Check file exists and basic info
gdalinfo DEM/parker_1m_dem_cog_twi.tif

# Get actual data statistics 
gdalinfo -stats DEM/parker_1m_dem_cog_twi.tif | grep -E "(Minimum|Maximum|Mean|NoData)"

# Check spatial extent matches Parker County
gdalinfo DEM/parker_1m_dem_cog_twi.tif | grep -E "(Upper Left|Lower Right)"

# Visual check of data distribution
gdalinfo -hist DEM/parker_1m_dem_cog_twi.tif
```

**Expected Results**: 
- Min/Max values match expected TWI range (-36.639 to -9.150)
- Spatial extent covers Parker County area
- Histogram shows data distribution across the range

**Key Questions**:
- Does the TWI file actually cover the full Parker County area?
- Is most of the file NoData with real data only in watershed areas?

---

### **Step 2: Small Sample Area Test**
**Goal**: Test scaling on a tiny area to see if it works correctly

**Validation Commands**:
```bash
# Extract a small 512x512 sample from the center of Parker County
gdal_translate -srcwin 1000 1000 512 512 \
    DEM/parker_1m_dem_cog_twi.tif \
    test_sample_original.tif

# Check the sample statistics
gdalinfo -stats test_sample_original.tif

# Apply our scaling to the sample
gdal_translate -scale -36.639 -9.15 0 255 -ot Byte \
    test_sample_original.tif \
    test_sample_scaled.tif

# Check scaled sample statistics
gdalinfo -stats test_sample_scaled.tif

# Convert to viewable PNG
gdal_translate -of PNG test_sample_scaled.tif test_sample.png
```

**Expected Results**: 
- Original sample shows TWI values in expected range
- Scaled sample shows 0-255 values with variation
- PNG shows visible patterns (not solid black/white)

---

### **Step 3: Preprocessed File Validation**
**Goal**: Validate the intermediate byte GeoTIFF has correct data distribution

**Validation Commands**:
```bash
# Check the byte GeoTIFF created by our pipeline
gdalinfo -stats _tiles_tmp/parker-twi_byte.tif

# Check histogram to see data distribution
gdalinfo -hist _tiles_tmp/parker-twi_byte.tif

# Extract a sample area from the byte file
gdal_translate -srcwin 1000 1000 512 512 \
    _tiles_tmp/parker-twi_byte.tif \
    byte_sample.tif

# Convert sample to PNG for visual inspection
gdal_translate -of PNG byte_sample.tif byte_sample.png
```

**Expected Results**:
- Byte file shows 0-255 value range with variation
- Sample PNG shows visible patterns
- NoData areas are handled correctly

---

### **Step 4: Single Tile Generation Test**
**Goal**: Test gdal2tiles on the preprocessed byte file for ONE specific tile

**Validation Commands**:
```bash
# Generate tiles for just zoom level 14 to a test directory
gdal2tiles.py -z 14-14 --xyz \
    _tiles_tmp/parker-twi_byte.tif \
    test_single_tiles/

# Check what tiles were generated
find test_single_tiles/ -name "*.png" | head -10

# Pick one tile and examine it
file test_single_tiles/14/3731/6595.png
ls -la test_single_tiles/14/3731/6595.png

# Check if tile has variation (not solid color)
identify -verbose test_single_tiles/14/3731/6595.png | grep -E "(mean|standard deviation)"
```

**Expected Results**:
- PNG tiles are generated
- Tiles have reasonable file sizes (not tiny = empty)
- Tiles show statistical variation (std dev > 0)

---

### **Step 5: Individual Tile Content Verification**
**Goal**: Manually verify that individual tiles contain expected data

**Validation Commands**:
```bash
# Convert tile back to readable format to check values
gdal_translate test_single_tiles/14/3731/6595.png test_tile_check.tif

# Check the pixel values in the tile
gdalinfo -stats test_tile_check.tif

# Sample a few pixel values
gdallocationinfo test_tile_check.tif 128 128
gdallocationinfo test_tile_check.tif 64 64
gdallocationinfo test_tile_check.tif 192 192

# Check for variation across the tile
python -c "
from osgeo import gdal
ds = gdal.Open('test_tile_check.tif')
band = ds.GetRasterBand(1)
arr = band.ReadAsArray()
print(f'Min: {arr.min()}, Max: {arr.max()}, Mean: {arr.mean():.2f}')
print(f'Unique values: {len(set(arr.flatten()))}')
"
```

**Expected Results**:
- Pixel values vary across the tile (not all the same value)
- Values are in 0-255 range with meaningful variation
- Multiple unique pixel values exist

---

### **Step 6: Local Tile Serving Test**
**Goal**: Test tiles locally before uploading to Azure

**Validation Commands**:
```bash
# Test with local tiles served via simple HTTP server
cd test_single_tiles && python -m http.server 8001

# Test URL access: http://localhost:8001/14/3731/6595.png
# Verify tile loads in browser and shows variation

# Test MapLibre with local tile server
# Update tile URL template temporarily to: http://localhost:8001/{z}/{x}/{y}.png
```

**Expected Results**:
- Tiles are accessible via HTTP
- Tiles display with visible variation in browser
- Can isolate if issue is tile generation vs MapLibre config

---

### **Step 7: Azure Storage Validation**
**Goal**: Validate Azure upload preserves tile data integrity

**Validation Commands**:
```bash
# Upload test tiles to Azure
cd .. && dotnet run --project TileUploadUtility -- --source-dir test_single_tiles

# Download a tile from Azure Storage directly
curl -o azure_tile.png "https://mapsandboxtiles.z13.web.core.windows.net/tiles/14/3731/6595.png"

# Compare local vs Azure tile
diff test_single_tiles/14/3731/6595.png azure_tile.png

# Check Azure tile properties
file azure_tile.png
identify azure_tile.png
```

**Expected Results**:
- Upload completes successfully
- Downloaded tile is identical to local tile
- Azure tile has same properties as local tile

---

### **Step 8: MapLibre Configuration Test**
**Goal**: Test MapLibre with known good tiles first, then our tiles

**Validation Commands**:
```bash
# Test 1: Use a known working raster tile source
# Update MapLibre config to use OpenStreetMap tiles temporarily:
# https://tile.openstreetmap.org/{z}/{x}/{y}.png

# Test 2: Use our local tiles
# http://localhost:8001/{z}/{x}/{y}.png

# Test 3: Use our Azure tiles
# https://mapsandbox-tiles-b0dfe8ffaga8d7ft.z03.azurefd.net/tiles/{z}/{x}/{y}.png
```

**Expected Results**:
- Known good tiles render properly in MapLibre
- Our local tiles render properly
- Azure tiles render properly
- Can isolate MapLibre vs tile generation issues

---

## Key Diagnostic Questions

At each step, we need to answer:

1. **Data Coverage**: Does the source TWI data actually cover the expected geographic area?
2. **NoData Handling**: Are we correctly handling areas with no watershed data?
3. **Scaling Accuracy**: Is the -36.639 to -9.150 → 0-255 scaling working correctly?
4. **Tile Coordinate System**: Are tiles being generated in the correct coordinate system for MapLibre?
5. **Data Distribution**: Is the TWI data concentrated in a small area (watersheds only)?

## Expected Issues and Solutions

| **Issue** | **Symptoms** | **Validation Step** | **Solution** |
|-----------|-------------|-------------------|-------------|
| Source data coverage | Most tiles black with data only in small areas | Step 1 | Verify data extent matches expected area |
| NoData scaling | Black areas where no watershed exists | Step 2-3 | Add proper -srcnodata/-dstnodata flags |
| Coordinate system mismatch | Tiles in wrong locations | Step 4-6 | Check projection and tile scheme |
| Upload corruption | Local tiles work, Azure tiles don't | Step 7 | Re-upload with verification |
| MapLibre config | Tiles load but don't display | Step 8 | Fix MapLibre tile URL or settings |

## Next Steps

1. Start with **Step 1** to validate source data
2. Work through each step systematically  
3. Stop at the first step that fails - that's where the issue lies
4. Fix the identified issue before proceeding to next step
5. Document findings and solutions for each step

---

**Status**: Ready to begin validation
**Current Focus**: Step 1 - Source Data Validation