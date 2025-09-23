# CRIS Data Processing and Visualization Plan

## Overview

This plan outlines the implementation approach for processing Texas Crash Records Information System (CRIS) data and integrating it with the existing MapSandBox infrastructure to create a crash risk assessment and visualization system based on the CRIS Model Card specifications.

## Current Infrastructure Assessment

### Existing Architecture
- **Blazor WebAssembly** application with dual mapping system (deck.gl, MapLibre)
- **Data Processing Infrastructure**: SoilDataProcessor project for handling geospatial data
- **Service Layer**: MapService and MapLibreService for layer configuration
- **Models**: Existing MapModels.cs and specialized data models (SoilModels.cs)
- **JavaScript Integration**: Advanced deck.gl and MapLibre integration with overlay capabilities
- **Data Sources**: Natural Earth via CDN, local Parker County data (roads, parcels)
- **Traffic Data**: parker-roads-with-traffic.geojson with AADT data (634 of 6345 road segments, 10% coverage)

### Available CRIS Data Structure
The CRIS export contains the following CSV files:
- **crash.csv**: Main crash records with location, conditions, severity
- **person.csv**: Individual person records (injuries, demographics)
- **unit.csv**: Vehicle/unit information
- **damages.csv**: Damage assessments
- **charges.csv**: Legal charges/citations
- **endorsements.csv**: License endorsements
- **lookup.csv**: Reference data/codes
- **restrictions.csv**: License restrictions
- **primaryperson.csv**: Primary person involved

## CRIS Model Card Implementation Plan

### Phase 1: Data Infrastructure and Processing

#### 1.1 Create CRIS Data Models
**Location**: `MapSandBox/Models/CrisModels.cs`

```csharp
// Core CRIS data structures aligned with model card specs
public class CrashRecord
{
    public string CrashId { get; set; }
    public DateTime CrashDateTime { get; set; }
    public string RoadwayId { get; set; }
    public int? Segment { get; set; }
    public KabcoSeverity Severity { get; set; }
    public decimal Latitude { get; set; }
    public decimal Longitude { get; set; }
    public int? Aadt { get; set; }
    public int? Pci { get; set; }
    public decimal? LidarElevation { get; set; }
    public List<ContributingFactor> ContributingFactors { get; set; }
    public WeatherCondition WeatherCondition { get; set; }
    public RoadwayCondition RoadwayCondition { get; set; }
    public List<VehicleInfo> Vehicles { get; set; }
    public List<PersonInfo> Persons { get; set; }
}

public class CrisModelScore
{
    public string LocationId { get; set; }
    public decimal CrashFrequencyScore { get; set; }    // Weight: 0.35
    public decimal SeverityIndexScore { get; set; }     // Weight: 0.25
    public decimal TrafficVolumeScore { get; set; }     // Weight: 0.10
    public decimal DrainageRiskScore { get; set; }      // Weight: 0.05
    public decimal EnvironmentalScore { get; set; }     // Weight: 0.05
    public decimal CompositeRiskScore { get; set; }     // Note: Weights adjusted without PCI (total = 0.80)
    public RiskLevel RiskLevel { get; set; }
}
```

#### 1.2 Create CRIS Data Processor
**Location**: `CrisDataProcessor/` (new project)

Similar to SoilDataProcessor structure:
- Console application for CSV processing
- Data transformation and aggregation
- GeoJSON output generation for web consumption
- Integration with existing road network data

**Key Processing Tasks**:
1. **Data Ingestion**: Parse CSV files and validate data integrity
2. **Geocoding Validation**: Verify lat/lon coordinates within Parker County bounds
3. **Traffic-Enabled Road Filtering**: Spatially join crashes only with the 634 road segments that have AADT data
4. **Temporal Aggregation**: Calculate crash frequency per mile/year for traffic-enabled roads
5. **Severity Indexing**: Implement weighted KABCO scoring system
6. **Risk Score Calculation**: Apply model card weights using existing AADT values

#### 1.3 CRIS Service Layer
**Location**: `MapSandBox/Services/CrisService.cs`

```csharp
public class CrisService
{
    public CrisConfiguration GetDefaultConfiguration()
    public List<CrisLayer> GetCrisLayers()
    public CrisModelScore CalculateRiskScore(List<CrashRecord> crashes)
    public GeoJsonFeatureCollection GetCrashDataByArea(BoundingBox area)
    public List<RiskSegment> GetHighRiskSegments(decimal threshold = 0.7m)
}
```

### Phase 2: Data Integration and Storage

#### 2.1 Processed Data Output Structure
**Location**: `MapSandBox/wwwroot/cris-data/`

```
cris-data/
├── parker-county-crashes-traffic-roads.geojson     # Crashes on traffic-enabled roads only
├── parker-county-risk-segments-traffic.geojson     # Risk scores for 634 traffic-enabled segments
├── parker-county-intersection-risks.geojson        # Intersection risk analysis (traffic roads)
└── cris-model-metadata.json                        # Model parameters and thresholds
```

#### 2.2 Data Integration with Existing Road Network
- **Focus on Traffic-Enabled Roads**: Limit initial scope to 634 road segments with existing AADT data in parker-roads-with-traffic.geojson
- **Spatial Join**: Associate crashes with traffic-enabled road segments using proximity analysis
- **Enhance Road Data**: Add risk scores to existing road features that already have traffic data
- **Leverage Existing Infrastructure**: Use existing AADT values and road geometry for immediate model implementation

### Phase 3: Visualization Implementation

#### 3.1 CRIS Layer Configuration
**Integration Point**: Extend existing layer system in MapService/MapLibreService

**New Layer Types**:
1. **Crash Points Layer**
   - Symbol: Severity-based color coding (KABCO scale)
   - Size: Based on number of people involved
   - Clustering: For high-density areas

2. **Risk Segments Layer**
   - Line styling: Color intensity based on composite risk score
   - Width: Based on traffic volume (AADT)
   - Popup: Detailed risk breakdown

3. **Risk Heatmap Layer**
   - Kernel density estimation of crash frequency
   - Color ramp: Green (low risk) to Red (high risk)
   - Temporal animation capabilities

4. **Model Card Dashboard Layer**
   - Interactive dashboard showing model component weights
   - Real-time score calculation for selected areas
   - Threshold adjustment controls

#### 3.2 JavaScript Integration
**Enhancement**: Extend existing maplibre-deckgl-integration.js

```javascript
// New CRIS-specific layer handling
function addCrisLayers(map, deckOverlay) {
    // Crash points with interactive popups
    // Risk-based road segment styling
    // Dynamic filtering by date range, severity
    // Model card component visualization
}

function createCrisPopup(crashData) {
    // Comprehensive crash information display
    // Model score breakdown
    // Contributing factors analysis
    // Historical context for location
}
```

#### 3.3 UI Components
**Location**: `MapSandBox/Components/`

1. **CrisLayerControl.razor**
   - Toggle individual CRIS layers
   - Adjust model weights in real-time
   - Date range filtering
   - Severity level filtering

2. **CrisRiskDashboard.razor**
   - Model card score display
   - Top risk locations listing
   - Trend analysis charts
   - Export capabilities

3. **CrashDetailsPopup.razor**
   - Detailed crash information
   - Contributing factors breakdown
   - Related crashes in area
   - Risk assessment for location

### Phase 4: Performance and Optimization

#### 4.1 Data Processing Optimization
- **Incremental Updates**: Process only new/changed crash records
- **Spatial Indexing**: Optimize geographic queries
- **Caching Strategy**: Pre-calculate common aggregations
- **Compression**: Optimize GeoJSON file sizes

#### 4.2 Visualization Performance
- **Level-of-Detail**: Dynamic simplification based on zoom level
- **Tile Generation**: Pre-compute risk tiles for fast rendering
- **Progressive Loading**: Load data progressively by importance
- **WebGL Optimization**: Leverage deck.gl performance features

## Implementation Timeline

### Week 1-2: Foundation
- [ ] Create CrisModels.cs with complete data structures
- [ ] Implement CrisDataProcessor console application
- [ ] Set up initial CSV parsing and validation
- [ ] Create basic risk score calculation engine

### Week 3-4: Data Processing
- [ ] Implement spatial aggregation algorithms
- [ ] Create road segment association logic
- [ ] Generate processed GeoJSON outputs
- [ ] Integrate with existing road network data

### Week 5-6: Service Layer
- [ ] Implement CrisService.cs
- [ ] Create CRIS layer configurations
- [ ] Integrate with existing mapping services
- [ ] Add API endpoints for crash data queries

### Week 7-8: Visualization
- [ ] Extend JavaScript mapping integration
- [ ] Create CRIS-specific layer rendering
- [ ] Implement interactive popups and tooltips
- [ ] Add layer control components

### Week 9-10: Optimization and Testing
- [ ] Performance optimization
- [ ] Data validation and quality assurance
- [ ] User interface polish
- [ ] Documentation and deployment

## Technical Considerations

### Data Quality and Validation
- **Coordinate Validation**: Ensure all crashes fall within Parker County boundaries
- **Temporal Consistency**: Validate date/time formats and ranges
- **Referential Integrity**: Cross-validate between related tables
- **Missing Data Handling**: Implement fallback strategies for incomplete records

### Security and Privacy
- **Data Anonymization**: Remove personally identifiable information
- **Access Controls**: Implement appropriate data access restrictions
- **CRIS Data Usage Compliance**: Ensure compliance with TxDOT data usage policies

### Integration Challenges
- **Road Network Matching**: Handle discrepancies between CRIS road references and local road data
- **Coordinate System**: Ensure consistent spatial reference systems
- **Data Freshness**: Plan for regular CRIS data updates
- **Backward Compatibility**: Maintain existing application functionality

## Success Metrics

### Technical Metrics
- **Data Processing Speed**: <30 seconds for full Parker County dataset
- **Map Rendering Performance**: <2 seconds for risk layer loading
- **Data Accuracy**: >95% spatial accuracy for crash-to-road matching
- **User Interface Responsiveness**: <1 second for layer toggles

### User Experience Metrics
- **Risk Assessment Accuracy**: Validate model predictions against known high-risk locations
- **Usability**: Intuitive navigation and information discovery
- **Information Clarity**: Clear presentation of complex risk data
- **Actionable Insights**: Enable data-driven safety decision making

## Dependencies and Prerequisites

### External Dependencies
- **CRIS Data Access**: Continued access to updated CRIS exports
- **Elevation Data**: Integration with existing DEM infrastructure
- **Existing AADT Data**: Already available in parker-roads-with-traffic.geojson

### Internal Dependencies
- **Existing Infrastructure**: Build upon current mapping and data processing systems
- **JavaScript Libraries**: Leverage current deck.gl and MapLibre implementations
- **Service Architecture**: Extend existing service layer patterns
- **UI Framework**: Utilize established Blazor component patterns

This plan provides a comprehensive roadmap for implementing CRIS data processing and visualization while leveraging the existing MapSandBox infrastructure and maintaining consistency with established patterns and practices.