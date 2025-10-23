# City Boundaries Layer Implementation Plan

## Overview
Add TxDOT City Boundaries as a new GeoJSON layer to the CRIS Analysis page, visualizing city limit polygons for Parker County and surrounding areas.

## Data Source
- **File**: `/data/TxDOT_City_Boundaries_2267092582487155282.geojson`
- **Size**: ~665KB
- **Format**: GeoJSON FeatureCollection
- **CRS**: EPSG:4326 (standard lat/long)
- **Coverage**: Parker County and immediate surrounding cities (pre-filtered from TxDOT statewide data)
- **Cities Included**: Weatherford (county seat), Aledo, Azle, Springtown, Willow Park, Hudson Oaks, Annetta (North/South), Cool, Cresson, Brock, Peaster, Dennis, Millsap, Sanctuary, plus border cities (Fort Worth, Mineral Wells)

### Data Structure
Each feature includes:
- **Geometry**: Polygon coordinates defining city boundaries
- **Properties**:
  - `CITY_NM`: City name (e.g., "Annetta", "Reno (Parker)")
  - `TXDOT_CITY_NBR`: TxDOT city number
  - `CITY_FIPS`: FIPS code
  - `CNTY_SEAT_FLAG`: County seat indicator ("Y"/"N")
  - `POP1990`, `POP2000`, `POP2010`, `POP2020`, `POP2022`: Population data
  - `POP_CD`: Population code
  - `MAP_COLOR_CD`, `COLOR_CD`: Color coding for visualization
  - `GID`, `OBJECTID`: Database identifiers

## Implementation Steps

### 1. Data Deployment
**File**: Move GeoJSON to web-accessible location

**Actions**:
```bash
# Move from /data/ to /MapSandBox/wwwroot/
mv /workspaces/map-sand-box/data/TxDOT_City_Boundaries_2267092582487155282.geojson \
   /workspaces/map-sand-box/MapSandBox/wwwroot/txdot-city-boundaries.geojson
```

**Rationale**:
- Files in `wwwroot/` are served as static content
- Follows existing pattern (parker-county-roads.geojson, etc.)
- Simplified filename for easier reference
- Dataset is already filtered to Parker County area (no preprocessing needed)

### 2. Backend Configuration
**File**: `MapSandBox/Services/MapLibreService.cs`

**Location**: Add to `GetDefaultLayers()` method (around line 350-360)

**Code Addition**:
```csharp
new LayerConfig
{
    Id = "txdot-city-boundaries",
    Type = "GeoJson",
    DataUrl = "/txdot-city-boundaries.geojson",
    Visible = false,
    Properties = new Dictionary<string, object>
    {
        ["filled"] = true,
        ["stroked"] = true,
        ["getFillColor"] = new int[] { 100, 150, 200, 30 },  // Light blue, semi-transparent
        ["getLineColor"] = new int[] { 0, 100, 200, 255 },   // Darker blue border
        ["getLineWidth"] = 2,
        ["lineWidthMinPixels"] = 1,
        ["lineWidthMaxPixels"] = 3,
        ["opacity"] = 0.6,
        ["pickable"] = true,
        ["autoHighlight"] = true,
        ["onClick"] = "handleCityBoundaryClick"
    }
}
```

**Design Decisions**:
- **Fill Color**: Light blue (100, 150, 200, 30) - low opacity for overlay visibility
- **Line Color**: Darker blue (0, 100, 200) - clear boundary delineation
- **Line Width**: 2px with responsive scaling (1-3px)
- **Default Visibility**: `false` - user can toggle on/off
- **Interaction**: Pickable with click handler for city information display

### 3. Layer Info Registration
**File**: `MapSandBox/Services/MapLibreService.cs`

**Location**: Add to `GetLayerInfo()` method (around line 408)

**Code Addition**:
```csharp
new LayerInfo {
    Id = "txdot-city-boundaries",
    Name = "City Boundaries (TxDOT)",
    Visible = false
}
```

**UI Categorization**:
- Should appear in "Background Layers" section (CrisAnalysis.razor:45-56)
- Alphabetically positioned for easy discovery
- Clear naming convention with data source attribution

### 4. Data Model
**File**: `MapSandBox/Models/MapModels.cs`

**Location**: Add after existing popup data models (around line 75)

**Code Addition**:
```csharp
public class CityBoundaryData
{
    public string CityName { get; set; } = "";
    public int TxDotCityNumber { get; set; }
    public string CityFips { get; set; } = "";
    public bool IsCountySeat { get; set; }
}
```

**Purpose**: Simplified model for city boundary popup data

### 5. JavaScript Interop Module
**File**: `MapSandBox/wwwroot/js/cityBoundaryPopup.js` (new file)

**Code**:
```javascript
let cityBoundaryPopupInstance = null;

export function setCityBoundaryPopupInstance(dotNetObjectReference) {
    cityBoundaryPopupInstance = dotNetObjectReference;
}

export function clearCityBoundaryPopupInstance() {
    cityBoundaryPopupInstance = null;
}

export function showCityBoundaryPopup(cityData) {
    if (cityBoundaryPopupInstance) {
        cityBoundaryPopupInstance.invokeMethodAsync('ShowPopupFromJS', cityData);
    }
}
```

**Purpose**: Bridge between JavaScript click events and C# popup component (follows crashPopup.js pattern)

### 6. JavaScript Click Handler
**File**: `MapSandBox/wwwroot/js/maplibre-deckgl-integration.js`

**Location**: Add handler function with other click handlers (around line 1377, after handleCrashClick)

**Code Addition**:
```javascript
async function handleCityBoundaryClick(info) {
    console.log('handleCityBoundaryClick called with:', info);

    if (info.object && info.object.properties) {
        const city = info.object.properties;

        // Create the city boundary popup data object
        const cityBoundaryData = {
            cityName: city.CITY_NM || 'Unknown',
            txDotCityNumber: city.TXDOT_CITY_NBR || 0,
            cityFips: city.CITY_FIPS || '',
            isCountySeat: city.CNTY_SEAT_FLAG === 'Y'
        };

        // Import and use the city boundary popup module
        try {
            const cityBoundaryPopupModule = await import('./cityBoundaryPopup.js');
            cityBoundaryPopupModule.showCityBoundaryPopup(cityBoundaryData);
        } catch (error) {
            console.error('Error showing city boundary popup:', error);
            // Fallback to alert if popup fails
            alert(`City: ${cityBoundaryData.cityName}
County Seat: ${cityBoundaryData.isCountySeat ? 'Yes' : 'No'}
TxDOT City #: ${cityBoundaryData.txDotCityNumber}`);
        }
    }
}
```

**Integration**: Reference in layer properties (already configured in step 2)

### 7. C# Popup Component
**File**: `MapSandBox/Components/CityBoundaryPopup.razor` (new file)

**Purpose**: Blazor popup component matching CrashPopup.razor style

**Code**:
```razor
@using MapSandBox.Models
@using Microsoft.JSInterop
@inject IJSRuntime JSRuntime
@implements IAsyncDisposable

@if (IsVisible && CityData != null)
{
    <div class="cris-popup-overlay" @onclick="Hide">
        <div class="cris-popup-card" @onclick:stopPropagation="true">
            <div class="popup-header">
                <h4>🏙️ @CityData.CityName</h4>
                <button class="close-btn" @onclick="Hide">×</button>
            </div>

            <div class="crash-breakdown">
                <h5>📊 City Information</h5>

                <div class="detail-item">
                    <div class="detail-header">
                        <span class="detail-icon">🏛️</span>
                        <span class="detail-label">Administrative Details</span>
                    </div>
                    <div class="location-details">
                        <div class="location-item">
                            <span class="location-label">County Seat:</span>
                            <span class="location-value">@(CityData.IsCountySeat ? "Yes" : "No")</span>
                        </div>
                        <div class="location-item">
                            <span class="location-label">TxDOT City #:</span>
                            <span class="location-value mono">@CityData.TxDotCityNumber</span>
                        </div>
                        <div class="location-item">
                            <span class="location-label">FIPS Code:</span>
                            <span class="location-value mono">@CityData.CityFips</span>
                        </div>
                    </div>
                </div>
            </div>
        </div>
    </div>
}

@code {
    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public CityBoundaryData? CityData { get; set; }
    [Parameter] public EventCallback OnHide { get; set; }

    private DotNetObjectReference<CityBoundaryPopup>? _objectReference;
    private IJSObjectReference? _jsModule;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            _jsModule = await JSRuntime.InvokeAsync<IJSObjectReference>("import", "./js/cityBoundaryPopup.js");
            _objectReference = DotNetObjectReference.Create(this);
            await _jsModule.InvokeVoidAsync("setCityBoundaryPopupInstance", _objectReference);
        }
    }

    public void Show(CityBoundaryData data)
    {
        CityData = data;
        IsVisible = true;
        StateHasChanged();
    }

    [JSInvokable]
    public void ShowPopupFromJS(CityBoundaryData data)
    {
        Show(data);
    }

    public async Task Hide()
    {
        IsVisible = false;
        await OnHide.InvokeAsync();
        StateHasChanged();
    }

    public async ValueTask DisposeAsync()
    {
        if (_jsModule != null && _objectReference != null)
        {
            await _jsModule.InvokeVoidAsync("clearCityBoundaryPopupInstance");
            _objectReference.Dispose();
            await _jsModule.DisposeAsync();
        }
    }
}
```

**Design Notes**:
- Uses identical `cris-popup-overlay` and `cris-popup-card` classes as CrashPopup
- Reuses existing CSS classes: `detail-item`, `detail-header`, `location-details`, `location-item`
- City icon 🏙️ in header to distinguish from crash popup
- Simple, focused information display
- Proper JS interop lifecycle management matching CrashPopup pattern
- No custom CSS needed - leverages existing CRIS popup styles

### 8. UI Integration
**File**: `MapSandBox/Pages/CrisAnalysis.razor`

**Changes**:
1. **Add popup reference** (line 19):
   ```razor
   <CityBoundaryPopup @ref="cityBoundaryPopup" />
   ```

2. **Add private field** (line 93):
   ```csharp
   private CityBoundaryPopup? cityBoundaryPopup;
   ```

**Result**: City boundaries layer appears in Background Layers section, ready to toggle

## Testing Plan

### Visual Verification
1. **Layer Visibility Toggle**
   - Navigate to `/cris` page
   - Locate "City Boundaries (TxDOT)" in Background Layers section
   - Toggle checkbox - boundaries should appear/disappear
   - Verify blue polygons outline cities

2. **Styling Validation**
   - Boundaries should have light blue fill (barely visible)
   - Borders should be darker blue and prominent
   - Layer should not obscure crash data or road segments

3. **Interaction Testing**
   - Click on city boundary polygon
   - Verify popup displays city name and administrative data
   - Verify County Seat flag displays properly
   - Check TxDOT City Number and FIPS Code display correctly

### Performance Testing
1. **Load Time**
   - Monitor browser network tab for GeoJSON download (~665KB)
   - Should load within 1-2 seconds on typical connections
   - Parker County focus means smaller dataset than statewide data
   - No impact on page initialization

2. **Rendering Performance**
   - Zoom in/out - layer should render smoothly across Parker County area
   - Toggle visibility - should respond instantly
   - Multiple layers active (roads, crashes, soil, city boundaries) - no lag or stuttering
   - Test at various zoom levels (county-wide view to city-level view)

### Data Quality Checks
1. **Parker County Cities**
   - Verify major cities display correctly: Weatherford (county seat), Aledo, Azle, Springtown, Willow Park
   - Check smaller cities render properly: Annetta (North/South), Cool, Cresson, Brock, Peaster
   - Validate administrative data (County Seat status for Weatherford, TxDOT numbers)

2. **Coordinate Accuracy**
   - Compare boundary positions with Parker County road network overlay
   - Verify alignment with CRIS crash data points
   - Check boundaries don't overlap incorrectly or have gaps
   - Validate Fort Worth and Mineral Wells boundaries at county edges

## Alternative Approaches

### Approach 1: Vector Tiles (Future Enhancement)
**Pros**:
- Progressive loading for large datasets
- Better performance with many features
- Standard industry approach

**Cons**:
- Requires tile generation pipeline
- More complex implementation
- Current ~665KB Parker County dataset doesn't justify complexity

**Decision**: Use GeoJSON for simplicity; dataset is already optimized for Parker County focus area

### Approach 2: Server-side MVT Tiles
**Pros**:
- Industry standard for vector data
- Better performance at multiple zoom levels
- Supports dynamic filtering

**Cons**:
- Requires backend API changes
- Overkill for static 665KB dataset
- Additional infrastructure complexity

**Decision**: Direct GeoJSON loading is sufficient for current needs

## Future Enhancements

### Short-term (1-2 months)
1. **Enhanced Popup**: Add population demographics, growth trends, links to city websites
2. **Search by City**: Allow users to search and navigate to specific cities
3. **City Name Labels**: Optional city name labels that appear at certain zoom levels

### Medium-term (3-6 months)
1. **Integration with CRIS Data**: Show crash counts and safety metrics per city boundary
2. **Comparative Analysis**: City-to-city safety metrics and rankings
3. **Highlight on Hover**: Subtle highlight effect when hovering over city boundaries

### Long-term (6+ months)
1. **Expand Coverage**: Add neighboring counties (Tarrant, Palo Pinto, Hood, Wise, Jack)
2. **Vector Tile Migration**: If dataset expands to regional or statewide coverage
3. **API Integration**: Real-time updates from TxDOT data sources for boundary changes

## Success Criteria

### Functional Requirements
- ✅ City boundaries display on CRIS Analysis page
- ✅ Layer toggles on/off via checkbox control
- ✅ Click interaction displays city information popup
- ✅ Styling distinguishes boundaries from other layers

### Performance Requirements
- ✅ Layer loads in < 2 seconds on standard connection
- ✅ No visible lag when toggling visibility
- ✅ Smooth rendering at all zoom levels
- ✅ No interference with existing layer performance

### User Experience Requirements
- ✅ Intuitive layer naming and placement in UI
- ✅ Popup information is clear and useful
- ✅ Visual styling supports primary CRIS analysis goals
- ✅ Layer doesn't obscure critical crash/road data

## References

### Existing Patterns
- **GeoJSON Layers**: parker-county-roads.geojson, county-cad-parcels
- **Layer Configuration**: MapLibreService.cs lines 114-388
- **Click Handlers**: maplibre-deckgl-integration.js handleRoadClick, handleSoilUnitClick
- **Popup Components**: RoadPopup.razor, SoilPopup.razor, CrashPopup.razor

### Documentation
- Project architecture: `/CLAUDE.md`
- Data processing: `/Documentation/DATA_PROCESSING_README.md`
- CRIS integration: `/Documentation/CRIS-Slope-Integration-Plan.md`

### External Resources
- TxDOT City Boundaries: https://gis-txdot.opendata.arcgis.com/
- deck.gl GeoJsonLayer: https://deck.gl/docs/api-reference/layers/geojson-layer
- MapLibre GL JS: https://maplibre.org/maplibre-gl-js/docs/
