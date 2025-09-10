# DEM Tiles Fix - Handling Negative Values

## Root Cause
The parker-twi tiles are black because:
- **TWI data range**: -36.639 to -9.150 (all negative values)
- **PNG pixel range**: 0 to 255 (positive only)
- **gdal2tiles.py**: No scaling specified, negative values → 0 (black pixels)

## Solution Options

### Option 1: Add Scaling to gdal2tiles (RECOMMENDED)
Modify `generate_web_tiles.py` to add proper data scaling:

```python
# For TWI layer, add scaling parameters
if layer_id == "parker-twi":
    cmd.extend([
        "--srcnodata", "-999999",
        "--dstnodata", "0", 
        "--scale", "-36.639", "-9.150", "0", "255"
    ])
```

### Option 2: Pre-process Data with Color Ramp
Create a VRT file with color mapping:

```python
# Create a color ramp VRT for TWI data
def create_color_ramp_vrt(input_tif, output_vrt, min_val, max_val):
    vrt_content = f'''<VRTDataset rasterXSize="..." rasterYSize="...">
        <VRTRasterBand dataType="Byte" band="1">
            <ColorInterp>Palette</ColorInterp>
            <ColorTable>
                <Entry c1="0" c2="0" c3="255" c4="255"/>      <!-- Blue for low TWI -->
                <Entry c1="255" c2="255" c3="0" c4="255"/>    <!-- Yellow for high TWI -->
            </ColorTable>
            <ComplexSource>
                <SourceFilename>{input_tif}</SourceFilename>
                <ScaleOffset>{-min_val}</ScaleOffset>
                <ScaleRatio>{255/(max_val-min_val)}</ScaleRatio>
            </ComplexSource>
        </VRTRasterBand>
    </VRTDataset>'''
```

### Option 3: Use gdal_translate preprocessing
Pre-process the TIF files before tile generation:

```bash
# Scale TWI data to 0-255 range
gdal_translate -scale -36.639 -9.150 0 255 -ot Byte \
    parker_1m_dem_cog_twi.tif \
    parker_1m_dem_cog_twi_scaled.tif
```

## Recommended Fix: Update generate_web_tiles.py

```python
def generate_tiles(layer_id, input_file):
    """Generate web tiles for a single layer."""
    input_path = Path(input_file)
    output_path = Path(OUTPUT_BASE) / layer_id
    
    # Check if input file exists
    if not input_path.exists():
        print(f"✗ Input file not found: {input_path}")
        return False
    
    print(f"\n📊 Processing {layer_id}:")
    print(f"   Input:  {input_path}")
    print(f"   Output: {output_path}")
    
    # Build gdal2tiles command
    cmd = [
        "gdal2tiles.py",
        "-z", ZOOM_LEVELS,
        "-w", "leaflet",
        "--profile", PROFILE,
        "--resampling", "bilinear",
        "--processes", "4"
    ]
    
    # Add layer-specific scaling for negative values
    if layer_id == "parker-twi":
        cmd.extend([
            "--srcnodata", "-999999",
            "--scale", "-36.639", "-9.150", "0", "255"
        ])
    elif layer_id == "parker-elevation":
        cmd.extend([
            "--srcnodata", "-999999", 
            "--scale", "177.964", "416.457", "0", "255"
        ])
    # Add scaling for other layers as needed...
    
    cmd.extend([str(input_path), str(output_path)])
```

## Quick Test Command
To test if scaling works:

```bash
# Generate one scaled tile manually
gdal_translate -scale -36.639 -9.150 0 255 -ot Byte \
    -projwin -97.7 32.8 -97.6 32.7 -outsize 256 256 \
    DEM/parker_1m_dem_cog_twi.tif test_twi_scaled.png
```

This should create a PNG with visible variation instead of solid black.

## Next Steps
1. Update `generate_web_tiles.py` with scaling parameters
2. Regenerate the parker-twi tiles 
3. Re-upload to Azure Storage
4. Test in application