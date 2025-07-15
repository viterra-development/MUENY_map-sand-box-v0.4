#!/bin/bash
set -e

# Load .env variables
if [ -f .env ]; then
  export $(grep -v '^#' .env | xargs)
else
  echo ".env file not found!"
  exit 1
fi

if [ -z "$ARCGIS_API_KEY" ]; then
  echo "ARCGIS_API_KEY not set in .env!"
  exit 1
fi

TEMPLATE="MapSandBox/wwwroot/appsettings.template.json"
OUTPUT="MapSandBox/wwwroot/appsettings.json"

sed "s/__ARCGIS_API_KEY__/$ARCGIS_API_KEY/g" "$TEMPLATE" > "$OUTPUT"
echo "Generated $OUTPUT with API key from .env" 