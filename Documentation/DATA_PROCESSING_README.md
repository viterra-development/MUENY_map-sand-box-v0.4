# Data Processing Pipeline Documentation

## Overview

This document outlines the comprehensive data processing pipeline for the MapSandBox project, which integrates multiple geospatial data sources for Parker County, Texas. The pipeline consists of three primary data processors and several supporting utilities that work together to generate visualization-ready datasets.

## Data Processing Components

### 1. TCDS.Importer
**Purpose**: Texas Crash Data System (TCDS) traffic count data importation and road-traffic data merging
**Technology**: .NET 9.0 Console Application with Playwright web scraping
**Location**: `/TCDS.Importer/`

#### Key Functionality
- **Web Scraping**: Automated data extraction from TxDOT TCDS website using Playwright
- **Traffic Count Processing**: Processes Annual Average Daily Traffic (AADT) data
- **Road-Traffic Merging**: Integrates traffic counts with Parker County road geometries
- **Data Quality Validation**: AADT validation and traffic matching services
- **Tile Generation**: Creates map tiles for visualization

#### Key Services
- `TcdsScrapingService` - Web scraping automation
- `EnhancedRoadTrafficMerger` - Advanced road-traffic merger with I-20 highway fixes
- `AadtValidationService` - Traffic data validation
- `TypeBasedTrafficMatcher` - Enhanced traffic matching algorithms
- `DataQualityMonitor` - Processing quality assurance
- `SimpleTileGenerator` - Map tile generation

#### Execution Modes
```bash
# Full processing (web scraping + merge + analysis)
dotnet run --project TCDS.Importer

# Road-traffic merge only (skip web scraping)
dotnet run --project TCDS.Importer -- --merge

# Tile generation only
dotnet run --project TCDS.Importer -- --tiles-only
```

#### Outputs
- **Primary**: `parker-roads-with-enhanced-traffic.geojson` (15.3MB) - Parker County roads with integrated traffic data
- **Quality Report**: `traffic-quality-report.json` - Data quality metrics
- **Coverage**: ~634 of 6,345 road segments (10% coverage) with AADT data

### 2. SoilDataProcessor
**Purpose**: SSURGO (Soil Survey Geographic Database) soil data processing and integration
**Technology**: .NET 9.0 Console Application with USDA SSURGO API integration
**Location**: `/SoilDataProcessor/`

#### Key Functionality
- **SSURGO API Integration**: Direct connection to USDA Soil Data Access API
- **Spatial Processing**: Geometry-based soil data queries using NetTopologySuite
- **Azure Upload**: Cloud storage integration for processed soil data
- **Area Processing**: Support for both test areas and full county processing

#### Data Sources
- **USDA SSURGO API**: `https://SDMDataAccess.sc.egov.usda.gov/Tabular/post.rest`
- **Geometry Source**: Parker County boundary or custom test areas

#### Execution Modes
```bash
# Small test area processing (default - ~1 km²)
dotnet run --project SoilDataProcessor

# Full Parker County processing
dotnet run --project SoilDataProcessor -- --full-county

# Upload existing data to Azure
dotnet run --project SoilDataProcessor -- --upload
```

#### Processing Areas
- **Test Area**: Small polygon in Parker County (1 km²) at coordinates (-97.800, 32.750)
- **Full County**: Complete Parker County boundary processing
- **Custom Geometry**: Configurable area definitions

#### Outputs
- **GeoJSON Features**: Soil properties with spatial geometries
- **Azure Storage**: Cloud-hosted soil datasets
- **Soil Properties**: Comprehensive soil characteristics and classifications

### 3. CrisDataProcessor
**Purpose**: Texas Crash Records Information System (CRIS) crash data processing and risk analysis
**Technology**: .NET 9.0 Console Application with advanced spatial analysis
**Location**: `/CrisDataProcessor/`

#### Key Functionality
- **CRIS Data Import**: Processing of Texas crash records CSV exports
- **Spatial Analysis**: Advanced crash location and road segment correlation
- **Risk Calculation**: Crash density and risk assessment algorithms
- **Enhanced Analytics**: Environmental factors, elevation data, and crash clustering

#### Core Services
- `CrisCsvParser` - CRIS CSV data parsing and validation
- `CrisRiskCalculator` - Crash risk assessment algorithms
- `CrisSpatialAnalyzer` - Basic spatial analysis of crash locations
- `EnhancedCrisSpatialAnalyzer` - Advanced spatial analysis with clustering
- `CrisGeoJsonGenerator` - Output generation for visualization
- `RoadGeometryService` - Road segment geometry processing
- `EnhancedRiskSegmentGenerator` - Advanced risk segment creation
- `ElevationService` - Elevation data integration
- `EnvironmentalAnalyzer` - Environmental factor analysis

#### Data Sources
- **Input**: CRIS CSV exports from `/CRIS Exports/extract_public_2023_*` directory
- **Supporting**: Parker County road geometry data
- **Enhancement**: DEM elevation data, environmental factors

#### CRIS Data Structure
The processor handles the following CSV files from CRIS exports:
- `crash.csv` - Main crash records (location, conditions, severity)
- `person.csv` - Individual person records (injuries, demographics)
- `unit.csv` - Vehicle/unit information
- `damages.csv` - Damage assessments
- `charges.csv` - Legal charges/citations
- `primaryperson.csv` - Primary person involved
- `lookup.csv` - Reference data/codes

#### Processing Pipeline
1. **Data Import**: Parse CRIS CSV files and validate data integrity
2. **Spatial Correlation**: Match crash locations to road segments
3. **Risk Assessment**: Calculate crash density and risk metrics
4. **Clustering Analysis**: Identify crash hotspots and patterns
5. **Enhancement**: Integrate elevation and environmental data
6. **Output Generation**: Create visualization-ready GeoJSON

#### Outputs
- **Risk Segments**: Road segments with calculated crash risk metrics
- **Crash Clusters**: Identified crash hotspot areas
- **Enhanced Analytics**: Multi-factor risk assessments
- **Visualization Data**: GeoJSON outputs for mapping integration

## Data Processing Dependencies and Flow

### Processing Order and Dependencies

```
1. Foundation Data (Independent)
   ├── Parker County Roads (TIGER/Line data)
   ├── DEM Data (Digital Elevation Model)
   └── CRIS Raw Exports (CSV files)

2. Primary Processing (Sequential + Parallel)
   ├── TCDS.Importer (Must run first)
   │   ├── Input: Parker County Roads
   │   └── Output: Roads with AADT Traffic Data
   ├── SoilDataProcessor (Can run in parallel with step 3)
   │   ├── Input: Parker County Boundary
   │   └── Output: Soil Data (GeoJSON/Azure)
   └── CrisDataProcessor (Depends on TCDS.Importer output)
       ├── Input: CRIS CSV + Roads with AADT + DEM
       └── Output: Crash Risk Analysis

3. Integration/Visualization
   └── MapSandBox Application
       ├── Consumes: All processed outputs
       └── Provides: Interactive mapping interface
```

### Cross-Dependencies

#### TCDS.Importer Dependencies
- **Input**: Parker County road geometries (`parker-county-roads.geojson`)
- **External**: TxDOT TCDS website for traffic count data
- **Output Used By**: MapSandBox visualization layers

#### SoilDataProcessor Dependencies
- **Input**: Parker County boundary geometry
- **External**: USDA SSURGO API
- **Configuration**: Azure storage credentials (optional)
- **Output Used By**: MapSandBox soil visualization layers

#### CrisDataProcessor Dependencies
- **Input**:
  - CRIS CSV export files
  - Parker County road geometries **with AADT traffic data** (from TCDS.Importer)
  - DEM elevation data (optional)
- **Output Used By**: MapSandBox crash risk visualization

### Shared Infrastructure

#### Common Components
- **MapSandBox.Shared**: Shared models and utilities
- **Configuration**: `appsettings.json` files in solution root
- **Logging**: Microsoft.Extensions.Logging framework
- **Spatial Processing**: NetTopologySuite for geometry operations

#### Output Integration
All processors generate outputs consumed by the MapSandBox Blazor application:
- **Deck.gl Layers**: High-performance WebGL visualization
- **MapLibre Integration**: Vector tile base maps with deck.gl overlays
- **Interactive Features**: Popups, layer controls, and spatial queries

## Execution Guidelines

### Sequential Processing (Recommended)
For complete data refresh, run processors in this order:

```bash
# 1. Traffic data (requires external web scraping)
dotnet run --project TCDS.Importer -- --merge

# 2. Soil data (API-dependent, can be parallel with CRIS)
dotnet run --project SoilDataProcessor -- --full-county

# 3. Crash risk analysis (requires road geometries from step 1)
dotnet run --project CrisDataProcessor

# 4. Generate visualization tiles
./generate-tiles.sh
```

### Parallel Processing Options
These can run simultaneously:
- SoilDataProcessor (independent data source)

**Note**: CrisDataProcessor requires AADT traffic data from TCDS.Importer and cannot run in parallel with it.

### Development/Testing Modes
```bash
# Quick testing with small datasets
dotnet run --project SoilDataProcessor  # Uses test area
dotnet run --project TCDS.Importer -- --tiles-only  # Skip scraping

# Upload only (no processing)
dotnet run --project SoilDataProcessor -- --upload
```

## Output Locations

### Processed Data Files
- **Traffic Data**: `/TestOutput/parker-roads-with-enhanced-traffic.geojson`
- **Quality Reports**: `/TestOutput/traffic-quality-report.json`
- **Soil Data**: Azure Blob Storage (configurable)
- **CRIS Analysis**: Generated in processor working directory

### Configuration Files
- **Global**: `/appsettings.json` (solution root)
- **Processor-Specific**: Each processor has its own `appsettings.json`
- **Environment**: `.env` file for API keys and secrets

### Log Files
- **Console Output**: Real-time processing logs
- **Debug Logs**: Processor-specific log files (e.g., `output.log`)

## Data Quality and Validation

### TCDS.Importer Quality Metrics
- **Coverage Assessment**: Road segments with traffic data
- **AADT Validation**: Traffic count data quality checks
- **Geometry Matching**: Road-traffic spatial correlation accuracy
- **Quality Report**: JSON output with processing statistics

### SoilDataProcessor Validation
- **API Response Validation**: SSURGO data integrity checks
- **Geometry Validation**: Spatial boundary compliance
- **Data Completeness**: Soil property coverage assessment

### CrisDataProcessor Quality Assurance
- **Data Parsing**: CSV integrity and format validation
- **Spatial Correlation**: Crash location accuracy verification
- **Risk Calculation**: Statistical validation of risk metrics
- **Clustering Validation**: Hotspot identification accuracy

## Configuration and Setup

### Prerequisites
- **.NET 9.0 SDK**: Required for all processors
- **Playwright**: Browser automation for TCDS.Importer
- **Azure Storage**: Optional for SoilDataProcessor cloud upload
- **Environment Variables**: API keys and connection strings

### Environment Setup
1. Configuration files are already present in the repository
2. Install Playwright browsers: `playwright install`
3. Configure Azure storage (if using cloud upload)

### Development Commands
```bash
# Build all processors
dotnet build MapSandBox.sln

# Run individual processors
dotnet run --project TCDS.Importer
dotnet run --project SoilDataProcessor
dotnet run --project CrisDataProcessor

# Development server (visualization)
dotnet run --project MapSandBox
```

This comprehensive data processing pipeline provides the foundation for advanced geospatial analysis and visualization of Parker County's transportation, soil, and crash risk data through the MapSandBox application.