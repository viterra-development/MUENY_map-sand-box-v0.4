# CRIS Road Segment Popup Implementation Plan

## Overview

This plan outlines the implementation of interactive CRIS data popups for road segments, displaying detailed risk assessment information based on the CRIS Model Card specifications (excluding PCI data).

## Implementation Approach: Static Configuration First

### **Selected Approach: Plan B - Data Processing Configuration**

We will implement **static weight configuration** first, with the option to add real-time recalculation later via API.

### **How It Will Work**

1. **Configuration-Driven Processing**:
   - Weights configured in `CrisDataProcessor` settings
   - Data processed once with specified weights
   - Generated files contain final risk scores
   - UI displays pre-calculated results

2. **Weight Adjustment Workflow**:
   - Analyst configures desired weights in processor
   - Runs data processing with new weights
   - Deploys updated data files to web application
   - UI shows results with new risk prioritization

3. **Use Cases**:
   - **Seasonal Analysis**: Process with higher Environmental weights for winter studies
   - **Traffic Focus**: Generate dataset with increased Traffic Volume weighting
   - **Safety Audit**: Create high-severity prioritized risk maps
   - **Infrastructure Planning**: Emphasize Drainage Risk for flood-prone analysis

### **Future Enhancement Path**
- **Phase 2**: Add recalculate API endpoint for real-time weight adjustment
- **Phase 3**: Implement client-side weight tuning with API calls
- **Phase 4**: Hybrid approach with cached results and dynamic recalculation

## Current State Assessment

### ✅ Already Implemented
- **Data Models**: Complete `RiskSegment` and `CrashRecord` models
- **Risk Calculation**: Basic scoring in `CrisRiskCalculator`
- **Data Processing**: Full CRIS data pipeline generating road segment risk scores
- **Map Integration**: MapLibre + deck.gl visualization with layer controls
- **Service Layer**: `CrisService` with data loading capabilities

### 🔧 Missing Components
- **Enhanced Risk Calculation**: 5-feature scoring system (excluding PCI)
- **Environmental Analysis**: Weather/surface condition crash categorization
- **Elevation/Drainage Data**: Slope analysis and hydroplaning incident detection
- **Popup Component**: Interactive UI component for displaying segment details
- **Map Click Integration**: Event handling to show popup on road segment click

## Implementation Plan

### Phase 1: Enhanced Data Models and Calculation

#### 1.1 Update Risk Calculation Models
**File**: `MapSandBox/Models/CrisModels.cs`

Add enhanced environmental risk tracking:
```csharp
public class EnvironmentalRiskFactors
{
    public decimal SlopePercentage { get; set; }
    public int WetSurfaceCrashes { get; set; }
    public int IcySurfaceCrashes { get; set; }
    public int FogRelatedCrashes { get; set; }
    public int HydroplaningIncidents { get; set; }
    public bool HasDrainageIssues { get; set; }
}

public class RiskSegment
{
    // Add environmental data
    public EnvironmentalRiskFactors EnvironmentalFactors { get; set; } = new();
    public decimal SlopePercentage { get; set; }

    // Add calculated metrics for popup display
    public decimal CrashesPerMilePerYear { get; set; }
    public int FatalCrashCount { get; set; }
    public int SeriousInjuryCrashCount { get; set; }
    public bool MeetsCrashFrequencyThreshold { get; set; }  // > 5 crashes/mile-year
    public bool MeetsSeverityThreshold { get; set; }        // ≥ 1 fatality or ≥ 3 incapacitating
    public bool MeetsTrafficVolumeThreshold { get; set; }   // > 15,000 AADT
    public bool HasDrainageRisk { get; set; }               // Slope > 5% or hydroplaning incidents
    public bool HasEnvironmentalRisk { get; set; }          // Frequent wet/icy crashes
}

public class DetailedCrisModelScore : CrisModelScore
{
    public decimal CrashFrequencyPerMile { get; set; }
    public int FatalCrashes { get; set; }
    public int IncapacitatingInjuryCrashes { get; set; }
    public decimal SlopePercentage { get; set; }
    public int WeatherRelatedCrashes { get; set; }
    public Dictionary<string, int> CrashBySurfaceCondition { get; set; } = new();
    public Dictionary<string, int> CrashByWeatherCondition { get; set; } = new();
}
```

#### 1.2 Configuration-Based Weight Management
**File**: `CrisDataProcessor/appsettings.json` (new)

Add configurable model weights:
```json
{
  "CrisModelConfiguration": {
    "ModelWeights": {
      "CrashFrequency": 0.35,
      "SeverityIndex": 0.25,
      "TrafficVolume": 0.15,
      "DrainageRisk": 0.15,
      "Environmental": 0.10
    },
    "Thresholds": {
      "CrashFrequencyPerMile": 5.0,
      "FatalCrashThreshold": 1,
      "IncapacitatingInjuryThreshold": 3,
      "TrafficVolumeThreshold": 15000,
      "SlopeThreshold": 5.0
    }
  }
}
```

#### 1.3 Enhanced Risk Calculation Service
**File**: `CrisDataProcessor/Services/CrisRiskCalculator.cs`

Add methods for detailed 5-feature scoring with configurable weights:
```csharp
public class CrisRiskCalculator
{
    private readonly CrisModelConfiguration _config;

    public CrisRiskCalculator(CrisModelConfiguration config)
    {
        _config = config;
    }

    public DetailedCrisModelScore CalculateDetailedRiskScore(
        List<CrashRecord> crashes,
        RiskSegment segment)
    {
        var score = new DetailedCrisModelScore
        {
            LocationId = segment.SegmentId
        };

        // 1. Crash Frequency (Weight: 0.35)
        score.CrashFrequencyPerMile = CalculateCrashFrequencyPerMile(crashes, segment.SegmentLength);
        score.CrashFrequencyScore = Math.Min(score.CrashFrequencyPerMile / 10m, 1.0m); // Normalize to 0-1

        // 2. Severity Index (Weight: 0.25)
        score.FatalCrashes = crashes.Count(c => c.Severity == KabcoSeverity.K_Fatal);
        score.IncapacitatingInjuryCrashes = crashes.Count(c => c.Severity == KabcoSeverity.A_IncapacitatingInjury);
        score.SeverityIndexScore = CalculateWeightedSeverityIndex(crashes);

        // 3. Traffic Volume (Weight: 0.15) - Increased from 0.10 to compensate for no PCI
        score.TrafficVolumeScore = segment.Aadt.HasValue
            ? Math.Min((decimal)segment.Aadt.Value / 25000m, 1.0m)
            : 0.1m;

        // 4. Elevation/Drainage Risk (Weight: 0.15) - Increased from 0.05
        score.SlopePercentage = segment.SlopePercentage;
        score.DrainageRiskScore = CalculateDrainageRiskScore(crashes, segment);

        // 5. Environmental Factors (Weight: 0.10) - Increased from 0.05
        score.WeatherRelatedCrashes = CountWeatherRelatedCrashes(crashes);
        score.CrashBySurfaceCondition = crashes
            .GroupBy(c => c.RoadwayCondition)
            .ToDictionary(g => g.Key, g => g.Count());
        score.CrashByWeatherCondition = crashes
            .GroupBy(c => c.WeatherCondition)
            .ToDictionary(g => g.Key, g => g.Count());
        score.EnvironmentalScore = CalculateEnvironmentalScore(crashes);

        // Calculate composite score using configured weights
        var weights = _config.ModelWeights;
        score.CompositeRiskScore =
            (score.CrashFrequencyScore * weights.CrashFrequency) +
            (score.SeverityIndexScore * weights.SeverityIndex) +
            (score.TrafficVolumeScore * weights.TrafficVolume) +
            (score.DrainageRiskScore * weights.DrainageRisk) +
            (score.EnvironmentalScore * weights.Environmental);

        score.RiskLevel = DetermineRiskLevel(score.CompositeRiskScore);

        return score;
    }

    private decimal CalculateCrashFrequencyPerMile(List<CrashRecord> crashes, decimal segmentLengthMiles)
    {
        if (segmentLengthMiles <= 0) return 0;

        // Assume data covers approximately 1 year
        var yearsOfData = 1.0m; // Could be calculated from crash date range
        return crashes.Count / (segmentLengthMiles * yearsOfData);
    }

    private decimal CalculateDrainageRiskScore(List<CrashRecord> crashes, RiskSegment segment)
    {
        var drainageRisk = 0m;

        // Slope risk (weight: 0.6)
        if (segment.SlopePercentage > 5m)
            drainageRisk += 0.6m;
        else if (segment.SlopePercentage > 3m)
            drainageRisk += 0.3m;

        // Wet surface crashes (weight: 0.4)
        var wetCrashes = crashes.Count(c =>
            c.RoadwayCondition.Contains("Wet") ||
            c.RoadwayCondition.Contains("Standing Water") ||
            c.WeatherCondition.Contains("Rain"));
        var wetCrashRatio = crashes.Count > 0 ? (decimal)wetCrashes / crashes.Count : 0;
        drainageRisk += wetCrashRatio * 0.4m;

        return Math.Min(drainageRisk, 1.0m);
    }

    private decimal CalculateEnvironmentalScore(List<CrashRecord> crashes)
    {
        if (!crashes.Any()) return 0;

        var environmentalCrashes = crashes.Count(c =>
            c.WeatherCondition.Contains("Rain") ||
            c.WeatherCondition.Contains("Snow") ||
            c.WeatherCondition.Contains("Fog") ||
            c.RoadwayCondition.Contains("Ice") ||
            c.RoadwayCondition.Contains("Wet") ||
            c.LightCondition.Contains("Dark"));

        return Math.Min((decimal)environmentalCrashes / crashes.Count, 1.0m);
    }

    private int CountWeatherRelatedCrashes(List<CrashRecord> crashes)
    {
        return crashes.Count(c =>
            !string.IsNullOrEmpty(c.WeatherCondition) &&
            !c.WeatherCondition.Contains("Clear") &&
            !c.WeatherCondition.Contains("Cloudy"));
    }
}
```

### Phase 2: Data Enhancement and Processing

#### 2.1 Add Elevation/Slope Data Integration
**File**: `CrisDataProcessor/Services/ElevationService.cs` (new)

```csharp
public class ElevationService
{
    public async Task<decimal> CalculateRoadSegmentSlope(
        double startLat, double startLon,
        double endLat, double endLon)
    {
        // Integration with elevation data source (DEM, USGS, or third-party API)
        // Calculate slope percentage between start and end points
        // Return slope as percentage
    }

    public async Task EnhanceRoadSegmentsWithElevation(List<RiskSegment> segments)
    {
        foreach (var segment in segments)
        {
            segment.SlopePercentage = await CalculateRoadSegmentSlope(
                (double)segment.StartLatitude, (double)segment.StartLongitude,
                (double)segment.EndLatitude, (double)segment.EndLongitude);
        }
    }
}
```

#### 2.2 Enhanced Environmental Analysis
**File**: `CrisDataProcessor/Services/EnvironmentalAnalyzer.cs` (new)

```csharp
public class EnvironmentalAnalyzer
{
    public EnvironmentalRiskFactors AnalyzeEnvironmentalRisk(
        List<CrashRecord> crashes,
        RiskSegment segment)
    {
        return new EnvironmentalRiskFactors
        {
            SlopePercentage = segment.SlopePercentage,
            WetSurfaceCrashes = crashes.Count(c => IsWetSurfaceCrash(c)),
            IcySurfaceCrashes = crashes.Count(c => IsIcySurfaceCrash(c)),
            FogRelatedCrashes = crashes.Count(c => IsFogRelatedCrash(c)),
            HydroplaningIncidents = crashes.Count(c => IsHydroplaningIncident(c)),
            HasDrainageIssues = segment.SlopePercentage > 5m ||
                               crashes.Any(c => IsHydroplaningIncident(c))
        };
    }

    private bool IsWetSurfaceCrash(CrashRecord crash)
    {
        return crash.RoadwayCondition.Contains("Wet") ||
               crash.WeatherCondition.Contains("Rain") ||
               crash.RoadwayCondition.Contains("Standing Water");
    }

    private bool IsIcySurfaceCrash(CrashRecord crash)
    {
        return crash.RoadwayCondition.Contains("Ice") ||
               crash.RoadwayCondition.Contains("Snow") ||
               crash.WeatherCondition.Contains("Snow") ||
               crash.WeatherCondition.Contains("Sleet");
    }

    private bool IsFogRelatedCrash(CrashRecord crash)
    {
        return crash.WeatherCondition.Contains("Fog") ||
               crash.WeatherCondition.Contains("Smoke");
    }

    private bool IsHydroplaningIncident(CrashRecord crash)
    {
        // Look for contributing factors or crash patterns that suggest hydroplaning
        return crash.ContributingFactors.Any(f =>
            f.Description.Contains("Hydroplan") ||
            f.Description.Contains("Water") ||
            f.Description.Contains("Aquaplan")) ||
            (IsWetSurfaceCrash(crash) && crash.ContributingFactors.Any(f =>
                f.Description.Contains("Speed") || f.Description.Contains("Control")));
    }
}
```

#### 2.3 Configuration Integration
**File**: `CrisDataProcessor/Program.cs`

Load configuration and integrate into processing pipeline:
```csharp
public class CrisProcessor
{
    private readonly ElevationService _elevationService;
    private readonly EnvironmentalAnalyzer _environmentalAnalyzer;
    private readonly CrisModelConfiguration _config;

    public CrisProcessor(
        // ... existing dependencies ...
        IConfiguration configuration)
    {
        // ... existing assignments ...
        _config = configuration.GetSection("CrisModelConfiguration").Get<CrisModelConfiguration>();
    }

    public async Task RunAsync()
    {
        _logger.LogInformation("Starting CRIS data processing with weights: CF={CrashFreq}, SI={SeverityIndex}, TV={TrafficVolume}, DR={DrainageRisk}, ENV={Environmental}",
            _config.ModelWeights.CrashFrequency, _config.ModelWeights.SeverityIndex,
            _config.ModelWeights.TrafficVolume, _config.ModelWeights.DrainageRisk, _config.ModelWeights.Environmental);

        // ... existing processing steps ...

        // Step 6: Add elevation/slope data
        await _elevationService.EnhanceRoadSegmentsWithElevation(riskSegments);

        // Step 7: Enhanced environmental analysis with configured thresholds
        foreach (var segment in riskSegments)
        {
            var segmentCrashes = crashesBySegment.GetValueOrDefault(segment.SegmentId, new List<CrashRecord>());
            segment.EnvironmentalFactors = _environmentalAnalyzer.AnalyzeEnvironmentalRisk(segmentCrashes, segment);

            // Calculate detailed risk metrics using configured weights
            var detailedScore = _riskCalculator.CalculateDetailedRiskScore(segmentCrashes, segment);

            // Update segment with threshold flags using configured thresholds
            segment.CrashesPerMilePerYear = detailedScore.CrashFrequencyPerMile;
            segment.FatalCrashCount = detailedScore.FatalCrashes;
            segment.SeriousInjuryCrashCount = detailedScore.IncapacitatingInjuryCrashes;
            segment.MeetsCrashFrequencyThreshold = detailedScore.CrashFrequencyPerMile > _config.Thresholds.CrashFrequencyPerMile;
            segment.MeetsSeverityThreshold = detailedScore.FatalCrashes >= _config.Thresholds.FatalCrashThreshold ||
                                           detailedScore.IncapacitatingInjuryCrashes >= _config.Thresholds.IncapacitatingInjuryThreshold;
            segment.MeetsTrafficVolumeThreshold = segment.Aadt > _config.Thresholds.TrafficVolumeThreshold;
            segment.HasDrainageRisk = segment.SlopePercentage > _config.Thresholds.SlopeThreshold ||
                                    segment.EnvironmentalFactors.HydroplaningIncidents > 0;
            segment.HasEnvironmentalRisk = segment.EnvironmentalFactors.WetSurfaceCrashes +
                                         segment.EnvironmentalFactors.IcySurfaceCrashes > segmentCrashes.Count * 0.2m;
        }

        // ... continue with output generation ...

        // Log processing summary with configuration used
        _logger.LogInformation("CRIS processing completed. Generated {SegmentCount} risk segments using weights: CF={CF}%, SI={SI}%, TV={TV}%, DR={DR}%, ENV={ENV}%",
            riskSegments.Count,
            _config.ModelWeights.CrashFrequency * 100,
            _config.ModelWeights.SeverityIndex * 100,
            _config.ModelWeights.TrafficVolume * 100,
            _config.ModelWeights.DrainageRisk * 100,
            _config.ModelWeights.Environmental * 100);
    }
}
```

### Phase 3: UI Components

#### 3.1 Create Road Segment Popup Component
**File**: `MapSandBox/Components/CrisRoadSegmentPopup.razor`

```html
@using MapSandBox.Models

<div class="cris-popup @(IsVisible ? "visible" : "hidden")">
    @if (RoadSegment != null)
    {
        <div class="popup-header">
            <h4>🛣️ @RoadSegment.RoadName</h4>
            <button class="close-btn" @onclick="Close">×</button>
        </div>

        <div class="risk-overview">
            <div class="risk-score-display">
                <span class="risk-score">@RoadSegment.RiskScore.ToString("F3")</span>
                <span class="risk-level @GetRiskLevelClass()">@RoadSegment.RiskLevel</span>
            </div>
        </div>

        <div class="feature-breakdown">
            <div class="feature-section">
                <h5>📊 Risk Assessment Features</h5>

                <div class="feature-item">
                    <div class="feature-header">
                        <span class="feature-icon">🚗</span>
                        <span class="feature-label">Crash Frequency</span>
                        <span class="feature-weight">35%</span>
                    </div>
                    <div class="feature-value">
                        @RoadSegment.CrashesPerMilePerYear.ToString("F1") crashes/mile/year
                    </div>
                    <div class="feature-threshold @(RoadSegment.MeetsCrashFrequencyThreshold ? "warning" : "safe")">
                        @(RoadSegment.MeetsCrashFrequencyThreshold ? "⚠️ Above 5.0 threshold" : "✅ Within safe limits")
                    </div>
                </div>

                <div class="feature-item">
                    <div class="feature-header">
                        <span class="feature-icon">💥</span>
                        <span class="feature-label">Severity Index</span>
                        <span class="feature-weight">25%</span>
                    </div>
                    <div class="feature-value">
                        @RoadSegment.FatalCrashCount fatalities, @RoadSegment.SeriousInjuryCrashCount serious injuries
                    </div>
                    <div class="feature-threshold @(RoadSegment.MeetsSeverityThreshold ? "warning" : "safe")">
                        @(RoadSegment.MeetsSeverityThreshold ? "⚠️ High severity incidents" : "✅ Low severity pattern")
                    </div>
                </div>

                <div class="feature-item">
                    <div class="feature-header">
                        <span class="feature-icon">🚦</span>
                        <span class="feature-label">Traffic Volume</span>
                        <span class="feature-weight">15%</span>
                    </div>
                    <div class="feature-value">
                        @(RoadSegment.Aadt?.ToString("N0") ?? "N/A") vehicles/day
                    </div>
                    <div class="feature-threshold @(RoadSegment.MeetsTrafficVolumeThreshold ? "warning" : "safe")">
                        @(RoadSegment.MeetsTrafficVolumeThreshold ? "⚠️ High volume (>15k)" : "✅ Normal volume")
                    </div>
                </div>

                <div class="feature-item">
                    <div class="feature-header">
                        <span class="feature-icon">🌧️</span>
                        <span class="feature-label">Drainage Risk</span>
                        <span class="feature-weight">15%</span>
                    </div>
                    <div class="feature-value">
                        @RoadSegment.SlopePercentage.ToString("F1")% slope
                        @if (RoadSegment.EnvironmentalFactors.HydroplaningIncidents > 0)
                        {
                            <br />@RoadSegment.EnvironmentalFactors.HydroplaningIncidents hydroplaning incidents
                        }
                    </div>
                    <div class="feature-threshold @(RoadSegment.HasDrainageRisk ? "warning" : "safe")">
                        @(RoadSegment.HasDrainageRisk ? "⚠️ Drainage concerns" : "✅ Good drainage")
                    </div>
                </div>

                <div class="feature-item">
                    <div class="feature-header">
                        <span class="feature-icon">🌦️</span>
                        <span class="feature-label">Environmental</span>
                        <span class="feature-weight">10%</span>
                    </div>
                    <div class="feature-value">
                        @RoadSegment.EnvironmentalFactors.WetSurfaceCrashes wet surface crashes<br />
                        @RoadSegment.EnvironmentalFactors.IcySurfaceCrashes icy surface crashes
                    </div>
                    <div class="feature-threshold @(RoadSegment.HasEnvironmentalRisk ? "warning" : "safe")">
                        @(RoadSegment.HasEnvironmentalRisk ? "⚠️ Weather sensitive" : "✅ Weather stable")
                    </div>
                </div>
            </div>

            <div class="recent-crashes-section">
                <h5>🚨 Recent Crashes (@RoadSegment.CrashCount total)</h5>
                @if (RoadSegment.RecentCrashes.Any())
                {
                    <div class="crash-list">
                        @foreach (var crash in RoadSegment.RecentCrashes.Take(5))
                        {
                            <div class="crash-item @GetSeverityClass(crash.Severity)">
                                <div class="crash-date">@crash.CrashDateTime.ToString("MM/dd/yyyy")</div>
                                <div class="crash-severity">@GetSeverityDisplayName(crash.Severity)</div>
                                <div class="crash-details">@crash.Persons.Count persons, @crash.Vehicles.Count vehicles</div>
                            </div>
                        }
                    </div>
                    @if (RoadSegment.RecentCrashes.Count > 5)
                    {
                        <div class="more-crashes">... and @(RoadSegment.RecentCrashes.Count - 5) more crashes</div>
                    }
                }
                else
                {
                    <div class="no-crashes">No recent crashes recorded</div>
                }
            </div>
        </div>
    }
</div>

<style>
.cris-popup {
    position: absolute;
    background: white;
    border: 1px solid #ccc;
    border-radius: 8px;
    box-shadow: 0 4px 20px rgba(0,0,0,0.15);
    max-width: 400px;
    max-height: 80vh;
    overflow-y: auto;
    z-index: 1000;
    font-family: Arial, sans-serif;
    transition: opacity 0.2s ease, visibility 0.2s ease;
}

.cris-popup.hidden {
    opacity: 0;
    visibility: hidden;
    pointer-events: none;
}

.cris-popup.visible {
    opacity: 1;
    visibility: visible;
    pointer-events: all;
}

.popup-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    padding: 15px;
    border-bottom: 1px solid #eee;
    background: #f8f9fa;
    border-radius: 8px 8px 0 0;
}

.popup-header h4 {
    margin: 0;
    color: #333;
    font-size: 16px;
}

.close-btn {
    background: none;
    border: none;
    font-size: 20px;
    cursor: pointer;
    color: #666;
    padding: 0;
    width: 24px;
    height: 24px;
    display: flex;
    align-items: center;
    justify-content: center;
}

.close-btn:hover {
    color: #333;
}

.risk-overview {
    padding: 15px;
    background: #f8f9fa;
    border-bottom: 1px solid #eee;
}

.risk-score-display {
    display: flex;
    justify-content: space-between;
    align-items: center;
}

.risk-score {
    font-size: 24px;
    font-weight: bold;
    color: #333;
}

.risk-level {
    padding: 4px 12px;
    border-radius: 20px;
    font-size: 12px;
    font-weight: bold;
    text-transform: uppercase;
}

.risk-level.very-high { background: #dc3545; color: white; }
.risk-level.high { background: #fd7e14; color: white; }
.risk-level.moderate { background: #ffc107; color: #333; }
.risk-level.low { background: #17a2b8; color: white; }
.risk-level.very-low { background: #28a745; color: white; }

.feature-breakdown {
    padding: 15px;
}

.feature-section h5, .recent-crashes-section h5 {
    margin: 0 0 15px 0;
    color: #333;
    font-size: 14px;
    border-bottom: 1px solid #eee;
    padding-bottom: 8px;
}

.feature-item {
    margin-bottom: 15px;
    padding: 12px;
    background: #f8f9fa;
    border-radius: 6px;
    border-left: 4px solid #dee2e6;
}

.feature-header {
    display: flex;
    justify-content: space-between;
    align-items: center;
    margin-bottom: 6px;
}

.feature-icon {
    font-size: 16px;
    margin-right: 8px;
}

.feature-label {
    font-weight: 600;
    color: #333;
    flex: 1;
}

.feature-weight {
    font-size: 12px;
    color: #666;
    background: #e9ecef;
    padding: 2px 6px;
    border-radius: 10px;
}

.feature-value {
    font-size: 14px;
    color: #495057;
    margin-bottom: 4px;
}

.feature-threshold {
    font-size: 12px;
    font-weight: 500;
}

.feature-threshold.warning {
    color: #dc3545;
}

.feature-threshold.safe {
    color: #28a745;
}

.recent-crashes-section {
    margin-top: 20px;
    padding-top: 15px;
    border-top: 1px solid #eee;
}

.crash-list {
    display: flex;
    flex-direction: column;
    gap: 8px;
}

.crash-item {
    padding: 8px;
    border-radius: 4px;
    border-left: 3px solid;
    font-size: 12px;
}

.crash-item.fatal {
    background: #f8d7da;
    border-left-color: #dc3545;
}

.crash-item.incapacitating {
    background: #fff3cd;
    border-left-color: #fd7e14;
}

.crash-item.non-incapacitating {
    background: #fff3cd;
    border-left-color: #ffc107;
}

.crash-item.possible {
    background: #d1ecf1;
    border-left-color: #17a2b8;
}

.crash-item.no-injury {
    background: #d4edda;
    border-left-color: #28a745;
}

.crash-date {
    font-weight: 600;
    margin-bottom: 2px;
}

.crash-severity {
    color: #666;
    margin-bottom: 2px;
}

.crash-details {
    font-size: 11px;
    color: #666;
}

.more-crashes, .no-crashes {
    font-size: 12px;
    color: #666;
    text-align: center;
    margin-top: 10px;
    padding: 8px;
    background: #f8f9fa;
    border-radius: 4px;
}
</style>

@code {
    [Parameter] public RiskSegment? RoadSegment { get; set; }
    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }

    private async Task Close()
    {
        await OnClose.InvokeAsync();
    }

    private string GetRiskLevelClass()
    {
        return RoadSegment?.RiskLevel.ToString().ToLower().Replace("very", "very-") ?? "low";
    }

    private string GetSeverityClass(KabcoSeverity severity)
    {
        return severity switch
        {
            KabcoSeverity.K_Fatal => "fatal",
            KabcoSeverity.A_IncapacitatingInjury => "incapacitating",
            KabcoSeverity.B_NonIncapacitatingInjury => "non-incapacitating",
            KabcoSeverity.C_PossibleInjury => "possible",
            KabcoSeverity.O_NoInjury => "no-injury",
            _ => "unknown"
        };
    }

    private string GetSeverityDisplayName(KabcoSeverity severity)
    {
        return severity switch
        {
            KabcoSeverity.K_Fatal => "Fatal (K)",
            KabcoSeverity.A_IncapacitatingInjury => "Incapacitating (A)",
            KabcoSeverity.B_NonIncapacitatingInjury => "Non-Incapacitating (B)",
            KabcoSeverity.C_PossibleInjury => "Possible Injury (C)",
            KabcoSeverity.O_NoInjury => "No Injury (O)",
            _ => "Unknown"
        };
    }
}
```

#### 3.2 Update Model Weights for 5-Feature System
**File**: `MapSandBox/Models/CrisModels.cs`

```csharp
public class CrisModelWeights
{
    public decimal CrashFrequency { get; set; } = 0.35m;     // 35% (unchanged)
    public decimal SeverityIndex { get; set; } = 0.25m;      // 25% (unchanged)
    public decimal TrafficVolume { get; set; } = 0.15m;      // 15% (increased from 10%)
    public decimal DrainageRisk { get; set; } = 0.15m;       // 15% (increased from 5%)
    public decimal Environmental { get; set; } = 0.10m;      // 10% (increased from 5%)
    // Total = 1.0 (100%) - PCI weight redistributed to other features
}
```

### Phase 4: Map Integration

#### 4.1 Update Map Click Handling
**File**: `MapSandBox/Pages/CrisAnalysis.razor`

```csharp
@code {
    private CrisRoadSegmentPopup? roadSegmentPopup;
    private RiskSegment? selectedSegment;
    private bool showPopup = false;

    private async Task HandleMapClick(MapLibreClickEventArgs e)
    {
        // Query for road segment at click location
        var segment = await QueryRoadSegmentAtLocation(e.Latitude, e.Longitude);

        if (segment != null)
        {
            selectedSegment = segment;
            showPopup = true;
            StateHasChanged();
        }
        else
        {
            showPopup = false;
            StateHasChanged();
        }
    }

    private async Task<RiskSegment?> QueryRoadSegmentAtLocation(double lat, double lon)
    {
        try
        {
            // Implementation would query the risk segments data
            // For now, find closest segment within reasonable distance
            var allSegments = await CrisService.LoadRiskSegmentsAsync();

            const double toleranceMeters = 50; // 50 meter tolerance

            return allSegments
                .Where(s => CalculateDistance(lat, lon, (double)s.StartLatitude, (double)s.StartLongitude) < toleranceMeters ||
                           CalculateDistance(lat, lon, (double)s.EndLatitude, (double)s.EndLongitude) < toleranceMeters)
                .OrderBy(s => Math.Min(
                    CalculateDistance(lat, lon, (double)s.StartLatitude, (double)s.StartLongitude),
                    CalculateDistance(lat, lon, (double)s.EndLatitude, (double)s.EndLongitude)))
                .FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error querying road segment: {ex.Message}");
            return null;
        }
    }

    private double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        // Haversine formula for distance calculation
        const double earthRadius = 6371000; // Earth radius in meters

        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);

        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));

        return earthRadius * c;
    }

    private double ToRadians(double degrees)
    {
        return degrees * Math.PI / 180;
    }

    private async Task HandlePopupClose()
    {
        showPopup = false;
        selectedSegment = null;
        StateHasChanged();
    }
}
```

Add popup component to the page:
```html
<div class="map-section">
    <MapLibreMap @ref="mapComponent" Config="mapConfig" OnMapClick="@HandleMapClick" />
    <RoadPopup @ref="roadPopup" />
    <SoilPopup @ref="soilPopup" />
    <CrashPopup @ref="crashPopup" />

    <!-- Add CRIS road segment popup -->
    <CrisRoadSegmentPopup @ref="roadSegmentPopup"
                          RoadSegment="selectedSegment"
                          IsVisible="showPopup"
                          OnClose="HandlePopupClose" />

    <!-- ... rest of existing content ... -->
</div>
```

### Phase 5: Testing and Refinement

#### 5.1 Test Data Validation
- Verify enhanced risk calculations produce expected results
- Test popup displays with various road segment scenarios
- Validate threshold logic and warning conditions

#### 5.2 Performance Optimization
- Optimize map click queries for large datasets
- Implement popup positioning to avoid edge clipping
- Add loading states for data queries

#### 5.3 User Experience Polish
- Add smooth popup animations
- Implement proper responsive design
- Add keyboard accessibility (ESC to close popup)

## Implementation Timeline

### Week 1: Configuration and Data Enhancement
- [ ] Create configuration system (appsettings.json, configuration models)
- [ ] Update risk calculation service to use configurable weights
- [ ] Implement elevation/slope analysis service
- [ ] Enhanced environmental categorization service
- [ ] Update data processing pipeline with configuration integration

### Week 2: UI Components and Static Display
- [ ] Create CrisRoadSegmentPopup component with static data display
- [ ] Integrate popup with map click events
- [ ] Test popup positioning and styling
- [ ] Remove or disable dynamic weight adjustment UI (for now)

### Week 3: Integration and Testing
- [ ] End-to-end testing with different weight configurations
- [ ] Test data processing with various weight scenarios
- [ ] Performance optimization for popup display
- [ ] Documentation for configuration options

### Week 4: Polish and Future Planning
- [ ] Final UI polish and accessibility
- [ ] Production deployment testing
- [ ] Document configuration workflow for analysts
- [ ] Plan Phase 2: Real-time recalculation API design

## Success Criteria

### Technical Requirements
- [ ] Popup displays detailed risk assessment for any road segment
- [ ] All 5 model features properly calculated and displayed using configured weights
- [ ] Threshold warnings work correctly based on configured thresholds
- [ ] Map integration is smooth and responsive
- [ ] Performance is acceptable (< 500ms click-to-popup)
- [ ] Configuration system allows easy weight and threshold adjustments
- [ ] Data processing pipeline honors all configuration settings

### User Experience Requirements
- [ ] Intuitive popup interaction
- [ ] Clear presentation of complex risk data
- [ ] Actionable threshold warnings
- [ ] Accessible design for all users
- [ ] Consistent with existing application UI patterns
- [ ] Clear documentation for configuration workflow

## Dependencies

### Data Sources
- **Elevation Data**: USGS DEM or third-party elevation API
- **Weather Data**: Enhanced weather condition mapping
- **Road Geometry**: Existing TIGER/Line data (already available)

### Technical Dependencies
- **MapLibre Click Events**: Existing map infrastructure
- **CRIS Data Pipeline**: Existing data processing (enhancement needed)
- **Risk Calculation Service**: Existing service (enhancement needed)

## Configuration Workflow for Analysts

### **Step 1: Configure Model Weights**
Edit `CrisDataProcessor/appsettings.json`:
```json
{
  "CrisModelConfiguration": {
    "ModelWeights": {
      "CrashFrequency": 0.40,    // Increase for crash-heavy analysis
      "SeverityIndex": 0.30,     // Increase for safety-focused analysis
      "TrafficVolume": 0.15,     // Standard for mixed analysis
      "DrainageRisk": 0.10,      // Increase for weather/infrastructure focus
      "Environmental": 0.05      // Standard for general analysis
    }
  }
}
```

### **Step 2: Run Data Processing**
```bash
cd CrisDataProcessor
dotnet run
```

### **Step 3: Deploy Updated Data**
Copy generated files from `CrisDataProcessor/MapSandBox/wwwroot/cris-data/` to `MapSandBox/wwwroot/cris-data/`

### **Step 4: Verify Results**
- Check generated metadata file for applied weights
- Review risk segment rankings in UI
- Validate popup displays reflect new prioritization

---

This implementation plan provides a comprehensive roadmap for delivering interactive CRIS road segment popups with detailed risk assessment information using **configurable static weights**, excluding PCI data but maintaining the full model card feature set through the remaining 5 components. The foundation is laid for future real-time recalculation capabilities.