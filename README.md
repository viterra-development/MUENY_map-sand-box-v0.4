# MapSandBox

A web-based mapping application built with deck.gl for interactive geospatial data visualization.

## Technology Stack

### Mapping Platform
- **deck.gl** (v9.0.0-beta.2) - WebGL-powered framework for large-scale geospatial data visualization
- **Blazor WebAssembly** - Frontend framework for the application

## Current Datasets

The application currently loads the following datasets from Natural Earth via CloudFront CDN:

### 1. Countries (`ne_50m_admin_0_scale_rank.geojson`)
- **Source**: Natural Earth (1:50m scale)
- **Content**: Administrative boundaries and country polygons
- **Styling**: 
  - Stroked borders with 3px minimum line width
  - Gray color scheme ([60, 60, 60] for lines, [200, 200, 200] for fill)
  - No fill color (transparent)

### 2. Rivers (`ne_50m_rivers_lake_centerlines.geojson`)
- **Source**: Natural Earth (1:50m scale)
- **Content**: Major rivers and lake centerlines
- **Styling**:
  - Blue color scheme ([100, 150, 255])
  - 1px minimum line width
  - 60% opacity

### 3. Airports (`ne_10m_airports.geojson`)
- **Source**: Natural Earth (1:10m scale)
- **Content**: Global airport locations with properties including name, abbreviation, and scale rank
- **Styling**:
  - Point-based visualization
  - Size varies by scale rank (larger airports = larger points)
  - Red color scheme ([200, 0, 80, 180])
  - Interactive with click events
- **Features**:
  - Clickable points that display airport name and abbreviation
  - Auto-highlighting on hover

### 4. Flight Paths (Great Circle Arcs)
- **Data Source**: Derived from airports dataset
- **Content**: Great circle routes from London to major airports (scale rank < 4)
- **Styling**:
  - Gradient colors from blue ([0, 128, 200]) to red ([200, 0, 80])
  - 1px width
  - London as fixed source point

## Layer Management

The application includes an interactive layer control panel that allows users to:
- Toggle visibility of countries layer
- Toggle visibility of rivers layer  
- Toggle visibility of airports layer
- Toggle visibility of flight paths layer

## Initial View State

- **Center**: London, UK (51.47°N, 0.45°E)
- **Zoom Level**: 3
- **Bearing**: 0°
- **Pitch**: 90° (top-down view)

## Data Sources

All geospatial data is sourced from [Natural Earth](http://www.naturalearthdata.com/) and served via CloudFront CDN for optimal performance:

- Base URL: `https://d2ad6b4ur7yvpq.cloudfront.net/naturalearth-3.3.0/`

## Performance Considerations

- Uses WebGL rendering for efficient large-scale data visualization
- Leverages CDN for fast data loading
- Implements layer-based rendering for selective data display
- Optimized for interactive exploration of global datasets

## Future Enhancements

Potential areas for expansion:
- Additional data layers (cities, roads, etc.)
- Custom data upload capabilities
- Advanced filtering and querying
- Time-based animations
- Custom styling options 