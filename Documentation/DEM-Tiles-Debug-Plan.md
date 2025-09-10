# DEM Tiles Debug Plan - Black Tiles Issue

## Problem
- Tiles are loading successfully from Azure Front Door
- All tiles appear black with no variation
- All DEM layers (parker-twi, parker-elevation, parker-slope, etc.) show the same issue

## Debug Pipeline Steps

### Step 1: Verify Source Data Integrity
**Check if original DEM files are valid**
```bash
# Check if DEM files exist and have data
ls -la DEM/*.tif
gdalinfo DEM/parker_1m_dem_cog_twi.tif
```

**Expected**: Should show file info, data range, and statistics

### Step 2: Verify Local Generated Tiles
**Check a sample local tile before it was uploaded**
```bash
# Check if we still have the backup
ls -la tiles-backup-*.tar.gz

# If backup exists, extract and check a sample tile
tar -tzf tiles-backup-*.tar.gz | head -5
# Extract just one tile to test
tar -xzf tiles-backup-*.tar.gz MapSandBox/wwwroot/tiles/parker-twi/14/3731/6595.png

# View the tile properties
file MapSandBox/wwwroot/tiles/parker-twi/14/3731/6595.png
```

**Expected**: PNG file with actual image data, not solid black

### Step 3: Test Direct Azure Storage Access
**Download and inspect a tile from Azure Storage**
```bash
# Download a sample tile directly from Azure Storage
curl -o test-tile.png "https://mapsandboxtiles.z13.web.core.windows.net/tiles/parker-twi/14/3731/6595.png"

# Check the downloaded tile
file test-tile.png
ls -la test-tile.png
```

**Expected**: Valid PNG file with image data

### Step 4: Test Front Door vs Direct Storage
**Compare tiles from both sources**
```bash
# Download same tile from Front Door
curl -o test-tile-fd.png "https://mapsandbox-tiles-b0dfe8ffaga8d7ft.z03.azurefd.net/tiles/parker-twi/14/3731/6595.png"

# Compare the files
diff test-tile.png test-tile-fd.png
ls -la test-tile*.png
```

**Expected**: Files should be identical

### Step 5: Verify Tile Generation Process
**Check if gdal2tiles.py generated valid tiles**

If we need to regenerate tiles:
```bash
cd DEM
python generate_web_tiles.py
```

**Look for**:
- Successful completion messages
- Generated tile counts matching expectations
- No error messages during generation

### Step 6: Check Specific DEM Data Ranges
**Verify the source DEM files have proper data ranges**
```bash
# Check TWI data range
gdalinfo -stats DEM/parker_1m_dem_cog_twi.tif

# Check elevation data range  
gdalinfo -stats DEM/parker_1m_dem_cog_filled.tif
```

**Expected**: Should show min/max values that aren't all the same

### Step 7: Test Individual Tile Rendering
**Use gdal_translate to create a test tile**
```bash
# Create a small test tile from the TWI data
gdal_translate -of PNG -outsize 256 256 -projwin -97.7 32.8 -97.6 32.7 DEM/parker_1m_dem_cog_twi.tif test_manual_tile.png
```

**Expected**: Should create a visible image with variation

## Most Likely Issues

### Issue 1: Source DEM Data Problems
- **Cause**: DEM files have no data variation (all NoData or single value)
- **Check**: `gdalinfo -stats` shows min=max or all NoData
- **Fix**: Re-download or reprocess DEM source data

### Issue 2: Tile Generation Color Mapping
- **Cause**: gdal2tiles not applying proper color ramp to single-band data
- **Check**: Generated PNG tiles are grayscale with no variation
- **Fix**: Add color ramp or scaling to gdal2tiles command

### Issue 3: Data Type/Scale Issues
- **Cause**: Float values being converted to PNG incorrectly
- **Check**: Original data has float values but PNGs show 0-255 range
- **Fix**: Add proper scaling in tile generation

### Issue 4: Upload Corruption
- **Cause**: Tiles corrupted during Azure upload
- **Check**: Compare local vs Azure tiles
- **Fix**: Re-upload tiles with verification

## Debugging Commands to Run

**Quick diagnostic sequence:**
```bash
# 1. Check if source DEM has data variation
gdalinfo -stats DEM/parker_1m_dem_cog_twi.tif | grep -E "(Minimum|Maximum|Mean)"

# 2. Check if we have tile backup
ls -la tiles-backup-*.tar.gz

# 3. Download and inspect Azure tile
curl -o debug-tile.png "https://mapsandboxtiles.z13.web.core.windows.net/tiles/parker-twi/14/3731/6595.png"
file debug-tile.png

# 4. If possible, check original local tile
# (if backup exists)
```

## Expected Investigation Results

| Step | Good Result | Bad Result (Needs Fix) |
|------|-------------|------------------------|
| Source DEM | Min/Max values different, has variation | Min=Max or all NoData |
| Local tiles | PNG with visible patterns | Solid black/white PNG |
| Azure tiles | Same as local tiles | Different from local or corrupted |
| Generation log | Success messages, >1M tiles created | Errors or warnings |

## Next Actions Based on Results

- **If source DEM is bad**: Need to reprocess DEM data
- **If tile generation is bad**: Need to fix gdal2tiles parameters  
- **If upload is bad**: Need to re-upload tiles
- **If Azure serving is bad**: Need to check Azure configuration

Let's start with Step 1 and work through systematically.