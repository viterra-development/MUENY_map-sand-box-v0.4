window.arcgisInterop = {
  createMap: function (elementId, apiKey) {
    require(["esri/Map", "esri/views/MapView"], function(Map, MapView) {
      const map = new Map({ basemap: "topo-vector" });
      const view = new MapView({
        container: elementId,
        map: map,
        center: [0.45, 51.47], // [lon, lat]
        zoom: 3
      });
      // Set API key
      if (apiKey) {
        window.esriConfig = window.esriConfig || {};
        window.esriConfig.apiKey = apiKey;
      }
    });
  }
}; 