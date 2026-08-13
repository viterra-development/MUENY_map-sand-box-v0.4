## 3DEP Elevation Data access

1) Get a high-res DEM (1 m)
Option A — USGS 3DEP (fastest, full coverage, free)

Open The National Map Downloader. Choose Data > Elevation Products (3DEP) > DEM 1 Meter. 
apps.nationalmap.gov
+1
USGS

In Search, type Parker County, Texas (or draw a polygon around the county).

Select all the 1 m DEM (GeoTIFF) tiles that intersect the county and Download the ZIPs (each contains a .tif).

Result: a folder of GeoTIFF tiles (bare-earth) ready to mosaic. These are from USGS 3DEP. 
USGS Science Data Catalog
Data.gov

### Get links in output file

### run curl command to download the files

Example:

curl -O https://prd-tnm.s3.amazonaws.com/StagedProducts/Elevation/1m/Projects/TX_Pecos_Dallas_2018_D19/TIFF/USGS_1M_14_x59y361_TX_Pecos_Dallas_2018_D19.tif


### Processing Steps:

Absolutely—here’s a clear, repeatable path to grab **DEM + hydro inputs** for **Parker County, TX** and then build **slope, flow accumulation, TWI, and SPI**.

# 1) Get a high-res DEM (1 m)

## Option A — USGS 3DEP (fastest, full coverage, free)

1. Open **The National Map Downloader**. Choose **Data > Elevation Products (3DEP) > DEM 1 Meter**. ([apps.nationalmap.gov][1], [USGS][2])
2. In **Search**, type **Parker County, Texas** (or draw a polygon around the county).
3. Select all the **1 m DEM (GeoTIFF)** tiles that intersect the county and **Download** the ZIPs (each contains a *.tif*).
4. Result: a folder of GeoTIFF tiles (bare-earth) ready to mosaic. These are from USGS **3DEP**. ([USGS Science Data Catalog][3], [Data.gov][4])

## Option B — TxGIO (formerly TNRIS) StratMap Elevation (Texas-specific, also free)

1. Open the **TxGIO / TNRIS StratMap Elevation – LiDAR** page and click through to the **DataHub**. ([TNRIS][5], [TxGIO DataHub][6])
2. Search for Parker-area LiDAR projects (e.g., multi-county flights covering Parker). Open the project page and download **DEM (TIFF)** (and/or **LAS/LAZ** if you want to roll your own surface).
3. Result: county-area DEM tiles comparable to 3DEP, hosted by Texas. (TxGIO notes statewide LiDAR coverage; projects are free to download.) ([TNRIS][7])

# 2) (Optional) Grab hydro layers

These are handy for QA and context (streams/catchments), but you **don’t need** them to compute TWI/SPI.

* **NHDPlus High Resolution (NHDPlus HR)** (flowlines + catchments + VAA): download by map from USGS. ([USGS][8])
* **Legacy NHDPlus v1 FAC/FDR** (≈10 m, prebuilt flow direction/accumulation rasters by production unit “Texas Gulf 12a–f”)—useful if you need quick, coarser flow grids. ([US EPA][9], [nhdplus.com][10])

# 3) Get a county boundary (for clipping)

* **Texas State & County Boundaries** from TxGIO DataHub (SHP/GeoJSON). Download and select **Parker County**. ([TxGIO DataHub][6])

# 4) Mosaic & clip the DEM (QGIS or GDAL)

**QGIS GUI**

* Raster ► Misc ► **Build Virtual Raster (VRT)** on all DEM tiles → *parker\_1m.vrt*
* Raster ► Extraction ► **Clip Raster by Mask Layer** (mask = Parker County polygon) → *parker\_1m\_dem.tif*

**GDAL CLI**

```bash
# 4a) Mosaic tiles
gdalbuildvrt parker_1m.vrt ./tiles/*.tif
gdal_translate -co COMPRESS=LZW -co TILED=YES parker_1m.vrt parker_1m_dem.tif

# 4b) Clip to county
gdalwarp -cutline parker_county.shp -crop_to_cutline -co COMPRESS=LZW -co TILED=YES \
  parker_1m_dem.tif parker_1m_dem_clipped.tif
```

# 5) Derive slope, flow accumulation, TWI, SPI

You can do this in **WhiteboxTools** (easy installs; runs from QGIS plugin, Python, R, or CLI) or **TauDEM/SAGA**. Below uses **WhiteboxTools**; names in **bold** are tool names.

> **Notes**
>
> * Use a **hydrologically corrected DEM** (fill or breach depressions) before routing flow.
> * For TWI you want **specific catchment area (SCA)** and **slope**. Whitebox’s **DInfFlowAccumulation** can output **SCA** directly; **D8FlowAccumulation** can also output SCA if you set `--out_type="specific contributing area"`. ([Whitebox Geo][11])
> * Whitebox includes dedicated **WetnessIndex** and **StreamPowerIndex** tools. ([Whitebox Geo][12])

**WhiteboxTools (CLI)**

```bash
# 5a) Fill or breach depressions (choose one)
whitebox_tools -r=FillDepressions -i=parker_1m_dem_clipped.tif -o=parker_filled.tif -v
# or: whitebox_tools -r=BreachDepressionsLeastCost -i=parker_1m_dem_clipped.tif -o=parker_breached.tif -v

# 5b) Slope (degrees or percent)
whitebox_tools -r=Slope --dem=parker_filled.tif --output=parker_slope_deg.tif -v
# (If you prefer percent slope, convert later, or compute with GDAL: gdaldem slope ... -p)

# 5c) Flow accumulation as SCA (D-Infinity; recommended)
whitebox_tools -r=DInfFlowAccumulation --input=parker_filled.tif --out_type="specific catchment area" \
  --output=parker_sca.tif -v

# 5d) TWI (ln(SCA / tan(beta)))
whitebox_tools -r=WetnessIndex --sca=parker_sca.tif --slope=parker_slope_deg.tif \
  --output=parker_twi.tif -v

# 5e) SPI (SCA * tan(beta); “relative stream power index”)
whitebox_tools -r=StreamPowerIndex --sca=parker_sca.tif --slope=parker_slope_deg.tif \
  --output=parker_spi.tif -v
```

* **Why this works:** Whitebox docs show **D8/DInf** accumulation can output **SCA**, and **WetnessIndex** computes the classic Beven & Kirkby TWI. **StreamPowerIndex** implements SPI from slope & SCA. ([Whitebox Geo][11], [Hydrology at USU][13])

**If you prefer TauDEM/SAGA**

* **TauDEM** has explicit TWI help and the D-Infinity workflow. Compute **D-Infinity contributing area** (SCA) → **Slope** → **TWI**. ([Hydrology at USU][14])
* **SAGA** has one-step tools (*Topographic Wetness Index (One Step)*; *Stream Power Index*) if you want a GUI flow. ([SourceForge][15], [SAGA GIS][16])

# 6) (Optional) Use prebuilt flow grids (coarser)

If 1-m processing is heavy and you just need a quick view, grab **NHDPlus v1** **FAC/FDR** (10 m) for Texas Gulf **Region 12** (pick the correct **Unit a–f** tiles for your area). These are ESRI grids you can project/clip and use directly; just note the coarser scale. ([US EPA][9])

---

## Output checklist (what you’ll have at the end)

* `parker_1m_dem_clipped.tif` — 1 m bare-earth DEM (GeoTIFF)
* `parker_slope_deg.tif` — slope raster
* `parker_sca.tif` — specific catchment area (m²/m)
* `parker_twi.tif` — topographic wetness index
* `parker_spi.tif` — stream power index

---

If you want, say the word and I’ll paste a **copy-paste QGIS model** or a **bash script** that: mosaics tiles, clips to Parker, fills depressions, and runs **all four** outputs end-to-end with sane compression and overviews.

[1]: https://apps.nationalmap.gov/downloader/?utm_source=chatgpt.com "TNM Download v2 - National Map Apps"
[2]: https://www.usgs.gov/tools/download-data-maps-national-map?utm_source=chatgpt.com "Download Data & Maps from The National Map"
[3]: https://data.usgs.gov/datacatalog/data/USGS%3A77ae0551-c61e-4979-aedd-d797abdcde0e?utm_source=chatgpt.com "1 meter Digital Elevation Models (DEMs)"
[4]: https://catalog.data.gov/dataset/1-meter-digital-elevation-models-dems-usgs-national-map-3dep-downloadable-data-collection?utm_source=chatgpt.com "1 meter Digital Elevation Models (DEMs) - Dataset - Catalog"
[5]: https://tnris.org/stratmap/elevation-lidar.html?utm_source=chatgpt.com "Elevation – Lidar"
[6]: https://data.tnris.org/?utm_source=chatgpt.com "TxGIO DataHub"
[7]: https://tnris.org/stratmap/?utm_source=chatgpt.com "StratMap - Strategic Mapping Program"
[8]: https://www.usgs.gov/national-hydrography/access-national-hydrography-products?utm_source=chatgpt.com "Access National Hydrography Products"
[9]: https://www.epa.gov/waterdata/nhdplusv1-texas-gulf-data?utm_source=chatgpt.com "NHDPlusV1 Texas Gulf Data | US EPA"
[10]: https://nhdplus.com/NHDPlus/NHDPlusV1_12.php?utm_source=chatgpt.com "Texas Gulf - NHD Plus"
[11]: https://www.whiteboxgeo.com/manual/wbt_book/available_tools/hydrological_analysis.html "Hydrological analysis - WhiteboxTools User Manual"
[12]: https://www.whiteboxgeo.com/manual/wbt_book/tool_index.html?utm_source=chatgpt.com "Tool Index - WhiteboxTools User Manual"
[13]: https://hydrology.usu.edu/taudem/taudem5/help53/TopographicWetnessIndex.html?utm_source=chatgpt.com "Topographic Wetness Index"
[14]: https://hydrology.usu.edu/taudem/taudem5/?utm_source=chatgpt.com "Terrain Analysis Using Digital Elevation Models (TauDEM)"
[15]: https://sourceforge.net/p/saga-gis/discussion/790705/thread/e3580630a1/?utm_source=chatgpt.com "TWI calculation error using 'Flow accumulation (qm of esp)'"
[16]: https://saga-gis.sourceforge.io/saga_tool_doc/9.3.1/ta_hydrology_21.html?utm_source=chatgpt.com "SAGA 9.3.1 | Tool Library Documentation | Tool Stream Power Index"
