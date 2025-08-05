#!/bin/bash

echo "🔄 Generating tiles from existing traffic count data..."
echo ""

# Run TCDS Importer in tiles-only mode (skips data scraping)
dotnet run --project TCDS.Importer/TCDS.Importer.csproj -- --tiles-only

# Check if successful
if [ $? -eq 0 ]; then
    echo ""
    echo "🎉 Tiles ready for testing!"
else
    echo ""
    echo "❌ Tile generation failed. Check the output above for errors."
    exit 1
fi