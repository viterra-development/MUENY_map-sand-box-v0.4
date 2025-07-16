window.arcgisInterop = {
  map: null,
  view: null,
  
  createMap: function (elementId, apiKey) {
    require(["esri/Map", "esri/views/MapView", "esri/layers/GeoJSONLayer"], function(Map, MapView, GeoJSONLayer) {
      const map = new Map({ basemap: "topo-vector" });
      const view = new MapView({
        container: elementId,
        map: map,
        center: [0.45, 51.47], // [lon, lat]
        zoom: 3
      });
      
      // Store references for later use
      window.arcgisInterop.map = map;
      window.arcgisInterop.view = view;
      
      // Set API key
      if (apiKey) {
        window.esriConfig = window.esriConfig || {};
        window.esriConfig.apiKey = apiKey;
      }
      
      console.log("Map created successfully");
    });
  },
  
  addRiversLayer: function() {
    require(["esri/layers/GeoJSONLayer"], function(GeoJSONLayer) {
      if (window.arcgisInterop.map) {
        const riversUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_50m_rivers_lake_centerlines.geojson";
        const riversLayer = new GeoJSONLayer({
          url: riversUrl,
          title: "Rivers"
        });
        
        riversLayer.load().then(function() {
          console.log("Rivers layer loaded successfully");
          window.arcgisInterop.map.add(riversLayer);
        }).catch(function(error) {
          console.error("Error loading rivers layer:", error);
        });
      } else {
        console.error("Map not available for rivers layer");
      }
    });
  },
  
  addAirportsLayer: function() {
    require(["esri/layers/GeoJSONLayer", "esri/symbols/SimpleMarkerSymbol", "esri/renderers/SimpleRenderer"], function(GeoJSONLayer, SimpleMarkerSymbol, SimpleRenderer) {
      if (window.arcgisInterop.map) {
        const airportsUrl = "https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/ne_10m_airports.geojson";
        const airportsLayer = new GeoJSONLayer({
          url: airportsUrl,
          title: "Airports"
        });
        
        // Style airports with red circles
        const airportSymbol = new SimpleMarkerSymbol({
          color: [200, 0, 80, 0.8],
          size: 8,
          outline: {
            color: [255, 255, 255],
            width: 1
          }
        });
        airportsLayer.renderer = new SimpleRenderer({
          symbol: airportSymbol
        });
        
        airportsLayer.load().then(function() {
          console.log("Airports layer loaded successfully");
          window.arcgisInterop.map.add(airportsLayer);
        }).catch(function(error) {
          console.error("Error loading airports layer:", error);
        });
      } else {
        console.error("Map not available for airports layer");
      }
    });
  }
}; 