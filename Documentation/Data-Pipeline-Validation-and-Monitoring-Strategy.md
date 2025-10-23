# Data Pipeline Validation and Monitoring Strategy

## Overview

This document provides a **tactical implementation plan** for validating the MapSandBox data pipeline end-to-end with focus on:
1. **Data lineage tracking** - storing original sources, intermediary steps, and live data locations
2. **Field-level validation** - specific data points to validate across the entire pipeline
3. **Automated health checks** - practical monitoring implementation

## Tactical Data Lineage Strategy

### Data Storage Architecture
```
Source Data (Immutable)
├── /data/sources/tcds/raw/           # Original TCDS scraping results
├── /data/sources/cris/exports/       # Original CRIS CSV exports
├── /data/sources/ssurgo/responses/   # Raw SSURGO API responses
├── /data/sources/noaa/downloads/     # Raw NOAA precipitation files
└── /data/sources/tiger/roads/        # Original Parker County road data

Intermediate Processing (Versioned)
├── /data/intermediate/tcds/          # Traffic matching steps
├── /data/intermediate/cris/          # Crash processing stages
├── /data/intermediate/soil/          # Soil data processing
└── /data/intermediate/noaa/          # Weather data processing

Live Application Data (Current)
├── /MapSandBox/wwwroot/              # Files served to application
├── /MapSandBox/wwwroot/tiles/        # Tiled data for performance
└── Azure Blob Storage               # Cloud-hosted datasets
```

### Data Lineage Implementation

#### 1. Source Data Preservation
```bash
# Create immutable source data archive structure
mkdir -p /data/sources/{tcds/raw,cris/exports,ssurgo/responses,noaa/downloads,tiger/roads}

# TCDS: Store original scraping results with timestamps
/data/sources/tcds/raw/
├── 20240924_130500_parker_county_traffic_data_consolidated_pages_1_to_100.json
├── 20240924_130500_screenshots/
└── 20240924_130500_metadata.json

# CRIS: Archive original CSV exports
/data/sources/cris/exports/
├── 2024Q3_extract_public_2023_crash.csv
├── 2024Q3_extract_public_2023_person.csv
└── 2024Q3_export_metadata.json

# SSURGO: Store API responses by request
/data/sources/ssurgo/responses/
├── 20240924_parker_county_full_response.json
├── 20240924_test_area_response.json
└── requests_log.json
```

#### 2. Intermediate Processing Steps
```bash
# TCDS Processing Chain
/data/intermediate/tcds/
├── 01_raw_traffic_locations.json           # Direct scraping output
├── 02_validated_locations.json             # After coordinate validation
├── 03_filtered_parker_county.json          # Geographic filtering
├── 04_aadt_calculations.json               # Traffic calculations
├── 05_road_matching_candidates.json        # Spatial matching results
├── 06_enhanced_traffic_final.json          # Ready for road merger
└── processing_log_20240924.json            # Step-by-step processing log

# CRIS Processing Chain
/data/intermediate/cris/
├── 01_parsed_csv_data.json                 # CSV parsing results
├── 02_geocoded_crashes.json                # Spatial processing
├── 03_road_segment_matches.json            # Road correlation
├── 04_risk_calculations.json               # Risk assessment
├── 05_clustered_hotspots.json              # Clustering analysis
└── 06_visualization_ready.json             # Final output
```

#### 3. Live Data Source Tracking
```json
// /data/lineage/live_data_manifest.json
{
  "lastUpdated": "2024-09-24T13:05:00Z",
  "dataSources": {
    "parkerRoadsWithTraffic": {
      "livePath": "/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson",
      "sourceFiles": [
        "/data/sources/tiger/roads/parker-county-roads.geojson",
        "/data/intermediate/tcds/06_enhanced_traffic_final.json"
      ],
      "lastProcessed": "2024-09-24T12:30:00Z",
      "recordCount": 6345,
      "trafficRecordCount": 634
    },
    "crashRiskSegments": {
      "livePath": "/MapSandBox/wwwroot/crash-risk-segments.geojson",
      "sourceFiles": [
        "/data/sources/cris/exports/2024Q3_extract_public_2023_crash.csv",
        "/data/intermediate/cris/06_visualization_ready.json"
      ],
      "lastProcessed": "2024-09-20T09:15:00Z",
      "recordCount": 1842
    }
  }
}
```

## Field-Level Validation Framework

### Critical Data Points to Validate End-to-End

#### TCDS Traffic Data Chain
```csharp
public class TcdsFieldValidation
{
    // Source → Intermediate → Live validation
    public ValidationResult ValidateTrafficDataChain(string sourceFile, string liveFile)
    {
        var result = new ValidationResult();

        // 1. Location ID consistency
        var sourceLocationIds = ExtractLocationIds(sourceFile);
        var liveLocationIds = ExtractLocationIds(liveFile);
        result.AddCheck("LocationID_Consistency", sourceLocationIds.SetEquals(liveLocationIds));

        // 2. AADT value preservation
        foreach (var locationId in sourceLocationIds)
        {
            var sourceAadt = GetLatestAADT(sourceFile, locationId);
            var liveAadt = GetAADT(liveFile, locationId);
            result.AddCheck($"AADT_Preservation_{locationId}", sourceAadt == liveAadt);
        }

        // 3. Coordinate accuracy (should not change)
        var sourceCoords = GetCoordinates(sourceFile);
        var liveCoords = GetCoordinates(liveFile);
        result.AddCheck("Coordinate_Preservation", CoordsMatch(sourceCoords, liveCoords, tolerance: 0.0001));

        // 4. I-20 specific validation (critical business rule)
        var i20Segments = GetI20Segments(liveFile);
        foreach (var segment in i20Segments)
        {
            result.AddCheck($"I20_MinAADT_{segment.Id}", segment.AADT >= 25000);
            result.AddCheck($"I20_NotRampData_{segment.Id}", !segment.SourceType.Contains("Ramp"));
        }

        return result;
    }
}
```

#### CRIS Crash Data Chain
```csharp
public class CrisFieldValidation
{
    public ValidationResult ValidateCrashDataChain(string csvSource, string liveGeoJson)
    {
        var result = new ValidationResult();

        // 1. Record count consistency
        var sourceCrashCount = CountCrashRecords(csvSource);
        var liveFeatureCount = CountFeatures(liveGeoJson);
        result.AddCheck("Record_Count_Consistency",
            Math.Abs(sourceCrashCount - liveFeatureCount) < sourceCrashCount * 0.05); // 5% tolerance

        // 2. Crash severity distribution
        var sourceSeverities = GetSeverityDistribution(csvSource);
        var liveSeverities = GetSeverityDistribution(liveGeoJson);
        result.AddCheck("Severity_Distribution",
            CompareSeverityDistributions(sourceSeverities, liveSeverities, tolerance: 0.1));

        // 3. Spatial bounds validation
        var sourceBounds = CalculateBounds(csvSource);
        var liveBounds = CalculateBounds(liveGeoJson);
        result.AddCheck("Spatial_Bounds_Consistency", BoundsOverlap(sourceBounds, liveBounds, 0.99));

        // 4. Date range preservation
        var sourceDateRange = GetDateRange(csvSource);
        var liveDateRange = GetDateRange(liveGeoJson);
        result.AddCheck("Date_Range_Consistency", DateRangesMatch(sourceDateRange, liveDateRange));

        return result;
    }
}
```

#### Cross-Dataset Validation
```csharp
public class CrossDatasetValidation
{
    // Validate TCDS traffic data matches roads it's applied to
    public ValidationResult ValidateTrafficRoadAlignment()
    {
        var roads = LoadRoadGeometry("/MapSandBox/wwwroot/parker-county-roads.geojson");
        var traffic = LoadTrafficData("/data/sources/tcds/raw/latest_consolidated.json");

        var result = new ValidationResult();

        foreach (var trafficLocation in traffic)
        {
            // Find closest road segment
            var closestRoad = FindClosestRoad(roads, trafficLocation.Coordinates);
            var distance = CalculateDistance(trafficLocation.Coordinates, closestRoad.Coordinates);

            // Traffic data should be within 200m of road centerline
            result.AddCheck($"Traffic_Road_Distance_{trafficLocation.LocationId}", distance <= 0.002);

            // Route name consistency check
            var routeNamesMatch = CompareRouteNames(trafficLocation.Route, closestRoad.FullName);
            result.AddCheck($"Route_Name_Consistency_{trafficLocation.LocationId}", routeNamesMatch);
        }

        return result;
    }

    // Validate crash locations align with road network
    public ValidationResult ValidateCrashRoadAlignment()
    {
        // Similar validation for crash data spatial accuracy
        // Ensure crashes are mapped to correct road segments
        // Validate crash attributes match road characteristics
    }
}
```

## Practical Implementation Plan

### Phase 1: Data Lineage Setup (Week 1)
```bash
# 1. Create directory structure
mkdir -p /data/{sources,intermediate,lineage}
mkdir -p /data/sources/{tcds/raw,cris/exports,ssurgo/responses,noaa/downloads}
mkdir -p /data/intermediate/{tcds,cris,soil,noaa}

# 2. Modify existing processors to save intermediate steps
# Add to TCDS.Importer/Program.cs:
await SaveIntermediateStep("01_raw_traffic_locations", trafficData);
await SaveIntermediateStep("02_validated_locations", validatedData);

# 3. Create data manifest tracking
dotnet run --project DataLineageTracker -- --generate-manifest

# 4. Set up automated source data backup
# Add to each processor: backup source data before processing
```

### Phase 2: Field Validation Implementation (Week 2)
```bash
# 1. Create validation test project
dotnet new xunit -n DataValidationTests
dotnet add DataValidationTests reference TCDS.Importer
dotnet add DataValidationTests reference CrisDataProcessor

# 2. Implement field validation classes
# Create TcdsFieldValidation, CrisFieldValidation classes

# 3. Add validation to CI pipeline
# .github/workflows/data-validation.yml
```

### Phase 3: Automated Health Checks (Week 3)
```bash
# 1. Create health check endpoints
dotnet add MapSandBox package Microsoft.Extensions.Diagnostics.HealthChecks

# 2. Implement data freshness checks
GET /health/data-freshness
GET /health/field-validation
GET /health/processing-status

# 3. Set up monitoring dashboard
docker-compose up -d grafana influxdb
```

### Specific Validation Tests to Implement

#### Daily Validation Suite
```bash
#!/bin/bash
# /scripts/daily_validation.sh

echo "Running daily data validation..."

# 1. Source data integrity
echo "Checking source data integrity..."
dotnet run --project DataValidationTests -- --test-suite=SourceIntegrity

# 2. Field-level consistency
echo "Validating field consistency..."
dotnet run --project DataValidationTests -- --test-suite=FieldConsistency

# 3. Cross-dataset alignment
echo "Checking cross-dataset alignment..."
dotnet run --project DataValidationTests -- --test-suite=CrossDataset

# 4. Live data service validation
echo "Validating live data services..."
curl -f http://localhost:5214/health/data-validation || exit 1

# 5. Generate validation report
dotnet run --project ValidationReporter -- --date=$(date +%Y%m%d)

echo "Validation complete. Report available at /data/validation/reports/"
```

#### Critical Field Validations
```csharp
[Fact]
public void TCDS_I20_Traffic_Values_Must_Be_Realistic()
{
    // Load live traffic data
    var trafficData = LoadLiveTrafficData();
    var i20Segments = trafficData.Features.Where(f => f.Properties.FullName.Contains("I-20"));

    foreach (var segment in i20Segments)
    {
        // I-20 should have high traffic volumes
        Assert.True(segment.Properties.Traffic.Aadt >= 25000,
            $"I-20 segment {segment.Properties.LinearId} has unrealistic AADT: {segment.Properties.Traffic.Aadt}");

        // Should not be using ramp data
        Assert.False(segment.Properties.TrafficMatch.SourceType.Contains("Ramp"),
            $"I-20 segment incorrectly using ramp data source");
    }
}

[Fact]
public void CRIS_Crash_Dates_Must_Be_Within_Expected_Range()
{
    var crashes = LoadLiveCrashData();
    var currentYear = DateTime.Now.Year;

    foreach (var crash in crashes.Features)
    {
        var crashDate = crash.Properties.CrashDate;
        Assert.True(crashDate.Year >= currentYear - 2 && crashDate.Year <= currentYear,
            $"Crash date {crashDate} outside expected range");
    }
}

[Fact]
public void Source_To_Live_Record_Count_Consistency()
{
    var sourceTrafficCount = CountSourceTrafficRecords();
    var liveTrafficCount = CountLiveTrafficFeatures();

    // Allow for some filtering but not massive data loss
    var retentionRate = (double)liveTrafficCount / sourceTrafficCount;
    Assert.True(retentionRate >= 0.8,
        $"Too much data loss: {liveTrafficCount}/{sourceTrafficCount} = {retentionRate:P}");
}
```

## Quick Start Implementation

### Week 1: Set Up Data Lineage
```bash
# 1. Create the directory structure
mkdir -p /data/{sources,intermediate,lineage,validation}
cd /workspaces/map-sand-box

# 2. Modify TCDS.Importer to save intermediate steps
# Add this function to TCDS.Importer/Program.cs:
```

```csharp
static async Task SaveIntermediateStep(string stepName, object data, string dataDirectory)
{
    var intermediateDir = Path.Combine("/data/intermediate/tcds");
    Directory.CreateDirectory(intermediateDir);

    var fileName = $"{stepName}_{DateTime.Now:yyyyMMdd_HHmmss}.json";
    var filePath = Path.Combine(intermediateDir, fileName);

    var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    await File.WriteAllTextAsync(filePath, json);

    Console.WriteLine($"💾 Saved intermediate step: {fileName}");
}
```

### Week 2: Create Validation Tests
```bash
# Create validation project
dotnet new xunit -n DataPipelineValidation
cd DataPipelineValidation

# Add package references
dotnet add package Microsoft.NET.Test.Sdk
dotnet add package xunit.runner.visualstudio
dotnet add package NetTopologySuite
dotnet add package Newtonsoft.Json

# Create test classes
```

```csharp
// DataPipelineValidation/TcdsValidationTests.cs
public class TcdsValidationTests
{
    [Fact]
    public void Validate_I20_Traffic_Data_Not_Using_Ramp_Values()
    {
        // Load current live data
        var liveDataPath = "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson";
        var trafficData = LoadGeoJsonFeatures(liveDataPath);

        var i20Segments = trafficData.Where(f => f.Properties["FULLNAME"]?.ToString().Contains("I- 20") == true);

        foreach (var segment in i20Segments)
        {
            var aadt = (int?)segment.Properties["aadt"];
            Assert.True(aadt >= 25000, $"I-20 segment has suspicious low AADT: {aadt}");
        }
    }

    [Fact]
    public void Validate_Source_To_Live_Record_Counts()
    {
        var sourceCount = CountTrafficRecordsInSource();
        var liveCount = CountTrafficRecordsInLive();
        var retentionRate = (double)liveCount / sourceCount;

        Assert.True(retentionRate >= 0.5, $"Too much data loss: {retentionRate:P}");
    }
}
```

### Week 3: Health Check Endpoints
```csharp
// Add to MapSandBox/Program.cs
builder.Services.AddHealthChecks()
    .AddCheck<DataFreshnessHealthCheck>("data-freshness")
    .AddCheck<FileAvailabilityHealthCheck>("file-availability");

// Create MapSandBox/HealthChecks/DataFreshnessHealthCheck.cs
public class DataFreshnessHealthCheck : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        var trafficFile = "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson";

        if (!File.Exists(trafficFile))
            return Task.FromResult(HealthCheckResult.Unhealthy("Traffic data file missing"));

        var lastWrite = File.GetLastWriteTime(trafficFile);
        var daysSinceUpdate = (DateTime.Now - lastWrite).TotalDays;

        if (daysSinceUpdate > 14)
            return Task.FromResult(HealthCheckResult.Degraded($"Traffic data is {daysSinceUpdate:F0} days old"));

        return Task.FromResult(HealthCheckResult.Healthy($"Traffic data is {daysSinceUpdate:F1} days old"));
    }
}
```

## Field-Level Validation Details

Based on your current data structure, here are the **specific fields** to validate end-to-end:

### TCDS Traffic Data Fields
```json
// Source: /data/sources/tcds/raw/parker_county_traffic_data_MASTER.json
{
  "locationId": "184CC1",           // → Should preserve in live data
  "locationInfo": {
    "latitude": 32.7167,            // → Must not change during processing
    "longitude": -97.8167,          // → Must not change during processing
    "locatedOn": "I- 20",           // → Route matching validation
    "category": "MAINLINE"          // → Critical: RAMP data should not apply to mainline roads
  },
  "aadtData": [
    {
      "year": 2023,
      "aadt": 45000                 // → This exact value should appear in live GeoJSON
    }
  ]
}

// Live: /MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson
{
  "properties": {
    "LINEARID": "abc123",           // → Road segment identifier
    "FULLNAME": "I- 20",            // → Must match locationInfo.locatedOn logic
    "aadt": 45000,                  // → Must match source aadtData latest value
    "traffic": {
      "locationId": "184CC1",       // → Must match source locationId
      "sourceType": "MainLine"      // → Must NOT be "Ramp" for interstate
    }
  }
}
```

### CRIS Crash Data Fields
```json
// Source: /data/sources/cris/exports/crash.csv
// CRASH_ID,CRASH_DATE,CRASH_SEVERITY,LATITUDE,LONGITUDE,COUNTY_ID
// 12345,2023-03-15,2,32.7500,-97.8000,187

// Intermediate: /data/intermediate/cris/04_risk_calculations.json
{
  "crashId": "12345",
  "crashDate": "2023-03-15",        // → Must preserve exact date
  "severity": 2,                    // → Must preserve severity code
  "coordinates": [32.7500, -97.8000], // → Must preserve coordinates
  "matchedRoadSegment": "abc123"    // → Should match a real LINEARID
}

// Live: crash risk data in application
{
  "properties": {
    "crashCount": 5,                // → Count should match source crash records
    "riskScore": 0.85,             // → Should be calculated from real crashes
    "lastCrashDate": "2023-03-15"  // → Should match most recent crash
  }
}
```

## Automated Daily Validation Script

```bash
#!/bin/bash
# /scripts/validate_pipeline.sh - Run this daily via cron

echo "🔍 MapSandBox Data Pipeline Validation - $(date)"
echo "================================================"

# 1. Check file existence
echo "📁 Checking critical files..."
CRITICAL_FILES=(
    "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-county-roads.geojson"
    "/workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson"
    "/workspaces/map-sand-box/TCDS.Importer/Data/parker_county_traffic_data_MASTER.json"
)

for file in "${CRITICAL_FILES[@]}"; do
    if [ -f "$file" ]; then
        SIZE=$(ls -lh "$file" | awk '{print $5}')
        MODIFIED=$(stat -c %y "$file" | cut -d' ' -f1)
        echo "✅ $file ($SIZE, modified: $MODIFIED)"
    else
        echo "❌ MISSING: $file"
        exit 1
    fi
done

# 2. Validate I-20 traffic values
echo "🛣️ Validating I-20 traffic data..."
I20_CHECK=$(grep -c '"FULLNAME".*"I-.*20".*"aadt".*[2-9][0-9][0-9][0-9][0-9]' /workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson || echo "0")
if [ "$I20_CHECK" -gt 0 ]; then
    echo "✅ I-20 segments have realistic AADT values ($I20_CHECK segments)"
else
    echo "⚠️ I-20 traffic validation needs attention"
fi

# 3. Check data freshness
echo "📅 Checking data freshness..."
TRAFFIC_AGE=$(find /workspaces/map-sand-box/MapSandBox/wwwroot/parker-roads-with-enhanced-traffic.geojson -mtime +7 2>/dev/null | wc -l)
if [ "$TRAFFIC_AGE" -eq 0 ]; then
    echo "✅ Traffic data is fresh (< 7 days old)"
else
    echo "⚠️ Traffic data is more than 7 days old"
fi

# 4. Run validation tests
echo "🧪 Running validation tests..."
cd /workspaces/map-sand-box
dotnet test DataPipelineValidation --logger:console --verbosity:minimal

# 5. Check application health endpoint (if running)
echo "🌐 Checking application health..."
if curl -f -s http://localhost:5214/health >/dev/null 2>&1; then
    echo "✅ Application health endpoint responding"
else
    echo "ℹ️ Application not running or health endpoint unavailable"
fi

echo "✅ Validation complete - $(date)"
```

## Monitoring Dashboard Data Points

Track these specific metrics in your monitoring dashboard:

### Data Quality Metrics
- **I-20 AADT Range**: Min/Max/Average AADT for Interstate 20 segments
- **Traffic Match Rate**: Percentage of road segments with traffic data
- **Coordinate Drift**: Any changes in lat/lon between source and live data
- **Record Count Consistency**: Source vs. intermediate vs. live record counts

### Processing Metrics
- **TCDS Processing Time**: Time to complete traffic data import and merge
- **File Size Trends**: Track growth/shrinkage of output files over time
- **Error Count**: Number of processing errors or warnings
- **Memory Usage**: Peak memory during processing

### Service Availability
- **File Availability**: All critical GeoJSON files exist and are readable
- **Data Freshness**: Days since last data update
- **Application Response**: Map loading time and layer rendering performance

This tactical approach gives you immediate, actionable validation that catches real issues in your specific data pipeline.