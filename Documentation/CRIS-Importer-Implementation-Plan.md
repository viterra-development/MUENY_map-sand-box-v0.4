# CRIS Crash Data Importer Implementation Plan

## Project Overview

This plan outlines the implementation of a CRIS (Crash Records Information System) data importer to extract crash data and display it as a new section in the existing RoadPopup component. The goal is to create a crash risk model card similar to the traffic data currently displayed.

## Target Risk Model Features

| Feature | Weight (Default) | Example Threshold |
|---------|------------------|-------------------|
| Crash Frequency (per mile) | 0.35 | > 5 crashes / mile-year |
| Severity Index (weighted KABCO) | 0.25 | ≥ 1 fatality or ≥ 3 incapacitating injuries |
| Pavement Condition Index (PCI) | 0.2 | PCI < 60 triggers resurfacing review |
| Traffic Volume (AADT) | 0.1 | > 15,000 vehicles/day |
| Elevation / Drainage Risk | 0.05 | Slope > 5% or repeated hydroplaning |
| Environmental Factors | 0.05 | Frequent wet/icy surface crashes |

## CRIS Data Field Mapping

### Available Data Fields

**✅ Available in `crash` table:**
- **Roadway ID**: `Rpt_Hwy_Sys_ID` + `Rpt_Hwy_Num` (e.g., "SH0171", "IH0020", "FM0920") 
- **Segment**: `Milepoint` (mile marker reference)
- **Crash Date/Time**: `Crash_Date` + `Crash_Time` (YYYY-MM-DD format + time)
- **Severity (KABCO)**: `Crash_Sev_ID` (references lookup table)
- **Contributing Factors**: `Wthr_Cond_ID`, `Light_Cond_ID`, `Surf_Cond_ID` (weather, lighting, road surface)
- **Roadway Condition**: `Surf_Cond_ID`, `Road_Constr_Zone_Fl` (surface condition, construction zone)
- **Location**: `Latitude`, `Longitude` (decimal degrees)
- **AADT**: `Adt_Curnt_Amt` (traffic volume)

**✅ Available in `unit` table:**
- **Vehicle Type/Class**: `Unit_Desc_ID`, `Veh_Body_Styl_ID` (passenger, commercial, motorcycle)

**✅ Available in `person` table:**
- **Injury Severity**: `Prsn_Injry_Sev_ID` (for detailed injury breakdown)

**❌ Not Available in CRIS:**
- **PCI**: Not directly available in CRIS data
- **LiDAR Elevation**: Not directly available in CRIS data

### Data Available for Risk Model

| Risk Factor | Status | CRIS Source |
|-------------|--------|-------------|
| ✅ Crash Frequency | Available | Count crashes per roadway segment over time |
| ✅ Severity Index | Available | Calculate weighted KABCO from `Crash_Sev_ID` + injury counts |
| ❌ PCI | Missing | Would need TxDOT pavement data |
| ✅ Traffic Volume | Available | `Adt_Curnt_Amt` (AADT) |
| ❌ Elevation/Drainage | Missing | Would need terrain data |
| ✅ Environmental Factors | Available | `Wthr_Cond_ID`, `Surf_Cond_ID` for wet/icy analysis |

## Data Models

### CrashPopupData Model
```csharp
public class CrashPopupData
{
    public required string CrashId { get; set; }
    public required DateTime CrashDateTime { get; set; }
    public required string Severity { get; set; } // KABCO
    public required string SeverityDescription { get; set; }
    public required string RoadwayId { get; set; }
    public double? Segment { get; set; } // Milepoint
    public required double[] Coordinates { get; set; }
    
    // Contributing Factors
    public string? WeatherCondition { get; set; }
    public string? LightCondition { get; set; }
    public string? RoadwaySurfaceCondition { get; set; }
    public bool IsConstructionZone { get; set; }
    
    // Traffic Data
    public int? AADT { get; set; }
    public string? AADTYear { get; set; }
    
    // Vehicle Information
    public List<VehicleInfo> Vehicles { get; set; } = new();
    
    // Injury Counts
    public int FatalCount { get; set; }
    public int SeriousInjuryCount { get; set; }
    public int NonIncapInjuryCount { get; set; }
    public int PossibleInjuryCount { get; set; }
    public int NoInjuryCount { get; set; }
    
    public bool HasInjuries => FatalCount + SeriousInjuryCount + NonIncapInjuryCount + PossibleInjuryCount > 0;
    public string FormattedDateTime => CrashDateTime.ToString("MMM dd, yyyy h:mm tt");
    public string FormattedCoordinates => $"{Coordinates[1]:F6}, {Coordinates[0]:F6}";
}

public class VehicleInfo
{
    public string? VehicleType { get; set; }
    public string? VehicleYear { get; set; }
    public string? VehicleMake { get; set; }
}
```

### Enhanced RoadPopupData Model
```csharp
public class RoadPopupData
{
    // Existing properties...
    
    // New crash data properties
    public CrashSummaryData? CrashData { get; set; }
    public bool HasCrashData => CrashData != null && CrashData.TotalCrashes > 0;
}

public class CrashSummaryData
{
    public int TotalCrashes { get; set; }
    public double SeverityScore { get; set; }
    public double CrashFrequency { get; set; } // crashes per mile per year
    public string RiskLevel { get; set; } // "High", "Medium", "Low"
    public string TimeRange { get; set; } // "2023-2025"
}
```

## Database Schema

```sql
-- Main crash table
CREATE TABLE Crashes (
    CrashId BIGINT PRIMARY KEY,
    CrashDate DATE NOT NULL,
    CrashTime TIME NOT NULL,
    Latitude DECIMAL(10, 8) NOT NULL,
    Longitude DECIMAL(11, 8) NOT NULL,
    RoadwayId NVARCHAR(50),
    Segment DECIMAL(10, 3),
    SeverityId INT,
    WeatherConditionId INT,
    LightConditionId INT,
    SurfaceConditionId INT,
    IsConstructionZone BIT,
    AADT INT,
    AADTYear NVARCHAR(4),
    -- Injury counts
    FatalCount INT DEFAULT 0,
    SeriousInjuryCount INT DEFAULT 0,
    NonIncapInjuryCount INT DEFAULT 0,
    PossibleInjuryCount INT DEFAULT 0,
    NoInjuryCount INT DEFAULT 0
);

-- Spatial index for geographic queries
CREATE SPATIAL INDEX IX_Crashes_Location ON Crashes(geography::Point(Latitude, Longitude, 4326));

-- Index for roadway queries
CREATE INDEX IX_Crashes_Roadway ON Crashes(RoadwayId, Segment);
```

## API Design (Minimal APIs)

```csharp
// In Program.cs
app.MapGet("/api/crashes/road/{roadwayId}", async (string roadwayId, CrashService crashService) =>
    await crashService.GetCrashesByRoadway(roadwayId));

app.MapGet("/api/crashes/segment", async (string roadwayId, double mileStart, double mileEnd, CrashService crashService) =>
    await crashService.GetCrashesBySegment(roadwayId, mileStart, mileEnd));

app.MapGet("/api/crashes/summary/{roadwayId}", async (string roadwayId, CrashService crashService) =>
    await crashService.GetCrashSummary(roadwayId));
```

## RoadPopup Component Integration

Add new crash section to `RoadPopup.razor`:

```razor
@if (RoadData.HasCrashData)
{
    <div class="popup-section crash-section">
        <div class="popup-label">Crash History</div>
        <div class="crash-stats">
            <div class="crash-stat severity-@RoadData.CrashData.RiskLevel.ToLower()">
                <div class="stat-value">@RoadData.CrashData.TotalCrashes</div>
                <div class="stat-label">Crashes (@RoadData.CrashData.TimeRange)</div>
            </div>
            <div class="crash-stat">
                <div class="stat-value">@RoadData.CrashData.SeverityScore.ToString("F1")</div>
                <div class="stat-label">Severity Index</div>
            </div>
            <div class="crash-stat">
                <div class="stat-value">@RoadData.CrashData.CrashFrequency.ToString("F1")</div>
                <div class="stat-label">Crashes/Mile/Year</div>
            </div>
        </div>
        <div class="risk-indicator risk-@RoadData.CrashData.RiskLevel.ToLower()">
            Risk Level: @RoadData.CrashData.RiskLevel
        </div>
    </div>
}
```

## Import Strategy

### Data Processing Pipeline
1. **CSV Parser**: Use `CsvHelper` library to parse CRIS CSV files
2. **Data Transformation**: Join crash, unit, and person tables to aggregate vehicle and injury data
3. **Lookup Resolution**: Use lookup table to resolve coded values to descriptions
4. **Segment Mapping**: Create road segments based on roadway ID + milepoint
5. **Risk Calculation**: Calculate crash frequency and severity scores per segment
6. **Batch Processing**: Process large CSV files in batches to avoid memory issues
7. **Data Validation**: Validate coordinates, dates, and required fields

### Technology Stack
- **CSV Processing**: CsvHelper NuGet package
- **Database**: Azure SQL Database Basic (~$5/month) or SQLite for development
- **APIs**: .NET Minimal APIs
- **Integration**: Extend existing RoadPopup component

## Open Questions & Decisions Needed

### 1. Road Segmentation Strategy
Since CRIS doesn't have explicit segment IDs, we need to decide on segmentation:

**Available Segmentation Data:**
- `Rpt_Hwy_Sys_ID` + `Rpt_Hwy_Num` (e.g., "SH", "171" = "SH0171")  
- `Milepoint` (decimal mile marker like 21.471, 18.996)
- `Latitude` + `Longitude` (precise coordinates)

**Options:**
- **Option A**: Create mile-based segments (e.g., "SH0171_Mile21-22")  
- **Option B**: Use existing road segment geometry and snap crashes to nearest segment  
- **Option C**: Create hybrid system using both milepoint and geographic proximity

**Decision Needed**: Which approach fits better with current road data structure?

### 2. Database Choice
**Options:**
- **Azure SQL Database Basic** (~$5/month) - Scalable, full SQL features
- **SQLite** - Free, file-based, good for development and small datasets
- **Azure SQL Database Serverless** - Pay per use, good for intermittent workloads

**Decision Needed**: What's the expected data volume and budget?

### 3. Missing Data Handling
**For PCI and LiDAR elevation:**
- Mark as "Not Available" in UI
- Future integration with TxDOT pavement data
- Future integration with terrain data sources

### 4. Performance Considerations
- Implement spatial indexing for geographic queries
- Consider data aggregation/pre-calculation for common queries
- Implement caching for frequently accessed road segments

### 5. Data Refresh Strategy
- One-time historical import
- Periodic updates (monthly/quarterly)
- Real-time integration (future consideration)

## Implementation Phases

### Phase 1: Foundation
1. Create crash data models
2. Set up database schema
3. Implement CSV import pipeline
4. Create basic minimal APIs

### Phase 2: Integration
1. Extend RoadPopupData model
2. Implement crash summary calculations
3. Update RoadPopup component
4. Add crash section styling

### Phase 3: Enhancement
1. Implement risk scoring algorithm
2. Add crash detail popup
3. Performance optimization
4. Data validation and error handling

### Phase 4: Future Enhancements
1. Integration with PCI data
2. Terrain/elevation data integration
3. Advanced analytics and reporting
4. Automated data refresh pipeline

## File Locations

- **CRIS Export Data**: `/CRIS Exports/extract_public_2023_20250818094137870_115143_20250101-20250818_PARKER/`
- **Current Models**: `/MapSandBox/Models/MapLibreModels.cs`
- **RoadPopup Component**: `/MapSandBox/Components/RoadPopup.razor`

## Next Steps

1. **Decide on segmentation strategy** (Options A, B, or C above)
2. **Choose database solution** (Azure SQL vs SQLite)
3. **Implement Phase 1** foundation components
4. **Test with sample CRIS data**
5. **Iterate on crash risk model weights and thresholds**