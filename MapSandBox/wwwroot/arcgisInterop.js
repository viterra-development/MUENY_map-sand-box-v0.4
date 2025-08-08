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
  }
}; 