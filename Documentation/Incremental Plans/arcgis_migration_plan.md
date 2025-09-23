# Plan to Display Data with ArcGIS Maps SDK for .NET

This document outlines the steps to create a new page in the application to display the datasets from `MapService.cs` using the ArcGIS Maps SDK for .NET. This is intended for a side-by-side comparison with the current map implementation.

## Phase 1: Setup and Basic Map

### 1. Install ArcGIS NuGet Package
The first step is to add the ArcGIS Maps SDK for .NET to the project. Since this is a Blazor application, we will use the Blazor-specific package.

- **Action:** Add the `Esri.ArcGISRuntime.Blazor` NuGet package to the `MapSandBox` project.

### 2. Configure ArcGIS API Key
An ArcGIS API key is required to use ArcGIS services, including basemaps. This key should be set once when the application starts.

- **Action:** In `Program.cs`, add the following line to initialize the ArcGIS Runtime. You will need to replace `"YOUR_API_KEY"` with a valid key from your ArcGIS Developer account.

  ```csharp
  Esri.ArcGISRuntime.ArcGISRuntimeEnvironment.ApiKey = "YOUR_API_KEY";
  ```

### 3. Create New Razor Component
We will create a new page to host the ArcGIS map.

- **Action:** Create a new Razor component named `ArcGISMap.razor` inside the `Pages` directory.
- **Action:** Add a code-behind file `ArcGISMap.razor.cs`.
- **Action:** Add the page route directive at the top of `ArcGISMap.razor`: `@page "/arcgis-map"`

### 4. Display a Basic Map
Add the `MapView` component to the new page and initialize a map with a basemap.

- **Action:** In `ArcGISMap.razor`, add the `MapView` component:
  ```html
  @page "/arcgis-map"
  
  <h3>ArcGIS Map</h3>
  
  <MapView Map="@Map" />
  ```
- **Action:** In `ArcGISMap.razor.cs`, define the `Map` property:
  ```csharp
  using Microsoft.AspNetCore.Components;
  using Esri.ArcGISRuntime.Mapping;
  
  namespace MapSandBox.Pages
  {
      public partial class ArcGISMap : ComponentBase
      {
          public Map Map { get; set; } = new Map(BasemapStyle.ArcGISTopographic);
      }
  }
  ```

## Phase 2: Adding Data Layers

### 5. Add GeoJSON Layers
We will add the countries, rivers, and airports layers from the GeoJSON URLs defined in `MapService`. The `GeoJsonLayer` class can directly consume these URLs.

- **Action:** In `ArcGISMap.razor.cs`, modify the map initialization to add these layers.

  ```csharp
  // In ArcGISMap.razor.cs
  
  // ...
  using Esri.ArcGISRuntime.UI;
  using Esri.ArcGISRuntime.Symbology;
  using System;
  // ...
  
  protected override void OnInitialized()
  {
      base.OnInitialized();
      CreateMap();
  }
  
  private void CreateMap()
  {
      Map = new Map(BasemapStyle.ArcGISTopographic);
      
      // 1. Countries Layer
      var countriesUri = new Uri("https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_50m_admin_0_scale_rank.geojson");
      var countriesLayer = new GeoJsonLayer(countriesUri);
      Map.OperationalLayers.Add(countriesLayer);
  
      // 2. Rivers Layer
      var riversUri = new Uri("https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_50m_rivers_lake_centerlines.geojson");
      var riversLayer = new GeoJsonLayer(riversUri);
      Map.OperationalLayers.Add(riversLayer);
  
      // 3. Airports Layer
      var airportsUri = new Uri("https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_10m_airports.geojson");
      var airportsLayer = new GeoJsonLayer(airportsUri);
      Map.OperationalLayers.Add(airportsLayer);
  }
  ```

### 6. Style the Airport Layer
The airports are points, and we can style them using a `Renderer`. We will use a simple red circle for now.

- **Action:** Apply a `SimpleRenderer` to the `airportsLayer`.

  ```csharp
  // In CreateMap() method, after creating airportsLayer
  
  var airportSymbol = new SimpleMarkerSymbol(SimpleMarkerSymbolStyle.Circle, System.Drawing.Color.Red, 10);
  airportsLayer.Renderer = new SimpleRenderer(airportSymbol);
  Map.OperationalLayers.Add(airportsLayer);
  ```

## Phase 3 (DEFERRED - MUST SKIP to Phase 4): Advanced Data Visualization

### 7. Implement Flight Paths
The "flight-paths" layer in the original implementation is a `GreatCircle` layer. In the ArcGIS SDK, we need to manually create these great circle lines. We will create a `GraphicsOverlay` to display them. For this plan, we will generate a few random flight paths between airports from the GeoJSON data.

- **Action:** Create a new method `AddFlightPathsOverlayAsync`.
  - Fetch and parse the airports GeoJSON data.
  - Select a small number of airports to act as start and end points.
  - For each pair, create a `Polyline` representing the geodesic (great circle) path using `GeometryEngine.GeodesicDensifyByLength`.
  - Create a `Graphic` for each path with a `SimpleLineSymbol`.
  - Add the graphics to a new `GraphicsOverlay`.
  - Add the overlay to the `MapView`.

  *This is a complex step and will require careful implementation. The logic for pairing airports will be simplified for this initial version.*

## Phase 4: UI and Interactivity

### 8. Add Layer Toggling
To replicate the functionality of toggling layers, we will add checkboxes to the UI that control the visibility of each layer.

- **Action:** In `ArcGISMap.razor`, add checkboxes bound to properties that control the `IsVisible` property of each layer.
- **Action:** In `ArcGISMap.razor.cs`, add the corresponding properties and methods to handle the visibility changes.

This plan provides a structured approach to building the comparison page using the ArcGIS Maps SDK for .NET. We can proceed with implementation step-by-step. 