# Azure Tile Storage Migration Plan

## Overview
Migrate 1.25 million DEM tile files (4.8GB) from local wwwroot to Azure Storage with Azure Front Door for optimal performance and cost efficiency.

**Updated 2025-01-07**: Plan updated to use Azure Front Door Standard instead of deprecated Azure CDN Classic. Classic CDN will be retired September 30, 2027.

## Phase 1: Azure Infrastructure Setup

### 1.1 Create Azure Resources
```bash
# Set variables
RESOURCE_GROUP="mapbox-tiles-rg"
STORAGE_ACCOUNT="mapsandboxtiles"
LOCATION="eastus"
FRONTDOOR_PROFILE="mapsandbox-tiles-fd"
FRONTDOOR_ENDPOINT="mapsandbox-tiles"

# Create resource group
az group create --name $RESOURCE_GROUP --location $LOCATION

# Create storage account
az storage account create \
    --name $STORAGE_ACCOUNT \
    --resource-group $RESOURCE_GROUP \
    --location $LOCATION \
    --sku Standard_LRS \
    --kind StorageV2 \
    --access-tier Hot

# Enable static website hosting
az storage blob service-properties update \
    --account-name $STORAGE_ACCOUNT \
    --static-website \
    --index-document index.html \
    --error-document-404 404.html

# Get storage account key
STORAGE_KEY=$(az storage account keys list \
    --resource-group $RESOURCE_GROUP \
    --account-name $STORAGE_ACCOUNT \
    --query '[0].value' --output tsv)

# Configure CORS for cross-origin requests
az storage cors add \
    --account-name $STORAGE_ACCOUNT \
    --account-key $STORAGE_KEY \
    --services b \
    --methods GET HEAD OPTIONS \
    --origins "*" \
    --allowed-headers "*" \
    --max-age 86400
```

### 1.2 Create Azure Front Door (Recommended - Replaces Classic CDN)
```bash
# Set Front Door variables (Classic CDN is deprecated, using Front Door Standard)
FRONTDOOR_PROFILE="mapsandbox-tiles-fd"
FRONTDOOR_ENDPOINT="mapsandbox-tiles"

# Create Azure Front Door profile (Standard tier)
az afd profile create \
    --profile-name $FRONTDOOR_PROFILE \
    --resource-group $RESOURCE_GROUP \
    --sku Standard_AzureFrontDoor

# Create Front Door endpoint
az afd endpoint create \
    --resource-group $RESOURCE_GROUP \
    --endpoint-name $FRONTDOOR_ENDPOINT \
    --profile-name $FRONTDOOR_PROFILE \
    --enabled-state Enabled

# Create origin group for storage
az afd origin-group create \
    --resource-group $RESOURCE_GROUP \
    --origin-group-name storage-origin-group \
    --profile-name $FRONTDOOR_PROFILE \
    --probe-request-type GET \
    --probe-protocol Https \
    --probe-interval-in-seconds 120 \
    --probe-path "/tiles/parker-twi/0/0/0.png"

# Add storage as origin
az afd origin create \
    --resource-group $RESOURCE_GROUP \
    --origin-group-name storage-origin-group \
    --origin-name storage-origin \
    --profile-name $FRONTDOOR_PROFILE \
    --origin-host-header $STORAGE_ACCOUNT.z13.web.core.windows.net \
    --host-name $STORAGE_ACCOUNT.z13.web.core.windows.net \
    --http-port 80 \
    --https-port 443 \
    --enabled-state Enabled

# Create route to connect endpoint to origin
az afd route create \
    --resource-group $RESOURCE_GROUP \
    --endpoint-name $FRONTDOOR_ENDPOINT \
    --profile-name $FRONTDOOR_PROFILE \
    --route-name tiles-route \
    --origin-group storage-origin-group \
    --supported-protocols Https \
    --patterns-to-match "/tiles/*" \
    --forwarding-protocol MatchRequest \
    --https-redirect Enabled
```

## Phase 2: Build Tile Upload Utility

### 2.1 Create Upload Console Application
Create new project: `TileUploadUtility`

```bash
# Create new console application
dotnet new console -n TileUploadUtility
cd TileUploadUtility

# Add Azure Storage package
dotnet add package Azure.Storage.Blobs
dotnet add package Microsoft.Extensions.Configuration
dotnet add package Microsoft.Extensions.Configuration.Json
```

### 2.2 Upload Utility Implementation

**Program.cs**:
```csharp
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace TileUploadUtility;

class Program
{
    private static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .Build();

        var connectionString = config.GetConnectionString("AzureStorage");
        var sourceDirectory = config["SourceDirectory"] ?? "../MapSandBox/wwwroot/tiles";
        var containerName = config["ContainerName"] ?? "$web";
        
        var uploader = new TileUploader(connectionString, containerName);
        await uploader.UploadTilesAsync(sourceDirectory);
    }
}

public class TileUploader
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly BlobContainerClient _containerClient;

    public TileUploader(string connectionString, string containerName)
    {
        _blobServiceClient = new BlobServiceClient(connectionString);
        _containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
    }

    public async Task UploadTilesAsync(string sourceDirectory)
    {
        var tileFiles = Directory.GetFiles(sourceDirectory, "*.png", SearchOption.AllDirectories);
        Console.WriteLine($"Found {tileFiles.Length:N0} tile files to upload");

        var semaphore = new SemaphoreSlim(50); // Limit concurrent uploads
        var uploaded = 0;
        var failed = new ConcurrentBag<string>();

        var tasks = tileFiles.Select(async filePath =>
        {
            await semaphore.WaitAsync();
            try
            {
                await UploadTileAsync(filePath, sourceDirectory);
                Interlocked.Increment(ref uploaded);
                
                if (uploaded % 1000 == 0)
                {
                    Console.WriteLine($"Uploaded {uploaded:N0} / {tileFiles.Length:N0} files");
                }
            }
            catch (Exception ex)
            {
                failed.Add($"{filePath}: {ex.Message}");
                Console.WriteLine($"Failed to upload {filePath}: {ex.Message}");
            }
            finally
            {
                semaphore.Release();
            }
        });

        await Task.WhenAll(tasks);
        
        Console.WriteLine($"Upload completed: {uploaded:N0} successful, {failed.Count} failed");
        
        if (failed.Any())
        {
            await File.WriteAllLinesAsync("failed-uploads.txt", failed);
            Console.WriteLine("Failed uploads written to failed-uploads.txt");
        }
    }

    private async Task UploadTileAsync(string filePath, string sourceDirectory)
    {
        // Create blob path maintaining directory structure
        var relativePath = Path.GetRelativePath(sourceDirectory, filePath);
        var blobName = $"tiles/{relativePath.Replace('\\', '/')}";
        
        var blobClient = _containerClient.GetBlobClient(blobName);
        
        // Set cache headers
        var headers = new Dictionary<string, string>
        {
            ["Cache-Control"] = "public, max-age=31536000", // 1 year
            ["Content-Type"] = "image/png"
        };

        await blobClient.UploadAsync(
            filePath, 
            new BlobUploadOptions 
            { 
                HttpHeaders = new BlobHttpHeaders 
                { 
                    CacheControl = headers["Cache-Control"],
                    ContentType = headers["Content-Type"]
                }
            });
    }
}
```

**appsettings.json**:
```json
{
  "ConnectionStrings": {
    "AzureStorage": "DefaultEndpointsProtocol=https;AccountName=mapsandboxtiles;AccountKey=YOUR_ACCOUNT_KEY;EndpointSuffix=core.windows.net"
  },
  "SourceDirectory": "../MapSandBox/wwwroot/tiles",
  "ContainerName": "$web"
}
```

### 2.3 Batch Upload Script
```bash
#!/bin/bash
# upload-tiles.sh

echo "Starting tile upload to Azure Storage..."

cd TileUploadUtility
dotnet build
dotnet run

echo "Upload completed. Checking Front Door propagation..."

# Test a sample tile (Front Door endpoint URL will be available after creation)
TILE_URL="https://mapsandbox-tiles-[random].z01.azurefd.net/tiles/parker-twi/18/59899/105599.png"
echo "Test tile URL: $TILE_URL"
curl -I $TILE_URL

echo "Migration completed!"
```

## Phase 3: Application Integration

### 3.1 Update Configuration Models

**Models/AzureTileConfig.cs**:
```csharp
namespace MapSandBox.Models;

public class AzureTileConfig
{
    public string BaseUrl { get; set; } = "";
    public string CdnUrl { get; set; } = "";
    public bool UseCdn { get; set; } = true;
    public string GetTileUrl(string tileType) => UseCdn ? $"{CdnUrl}/tiles/{tileType}" : $"{BaseUrl}/tiles/{tileType}";
}
```

### 3.2 Update MapLibreService

**Services/MapLibreService.cs** additions:
```csharp
public class MapLibreService
{
    private readonly AzureTileConfig _azureTileConfig;

    public MapLibreService(IConfiguration configuration)
    {
        _azureTileConfig = configuration.GetSection("AzureTiles").Get<AzureTileConfig>() 
            ?? new AzureTileConfig();
    }

    public MapLibreConfig GetMapLibreConfigWithAzureTiles()
    {
        var config = GetDefaultMapLibreConfig();
        
        // Update tile sources to use Azure Storage
        config.Sources["parker-twi"] = new
        {
            type = "raster",
            tiles = new[] { $"{_azureTileConfig.GetTileUrl("parker-twi")}/{{z}}/{{x}}/{{y}}.png" },
            tileSize = 256,
            minzoom = 0,
            maxzoom = 18
        };

        return config;
    }
}
```

### 3.3 Update Application Configuration

**appsettings.json**:
```json
{
  "AzureTiles": {
    "BaseUrl": "https://mapsandboxtiles.z13.web.core.windows.net",
    "CdnUrl": "https://mapsandbox-tiles-[random].z01.azurefd.net",
    "UseCdn": true
  }
}
```

**Program.cs** additions:
```csharp
builder.Services.Configure<AzureTileConfig>(
    builder.Configuration.GetSection("AzureTiles"));
```

### 3.4 Update JavaScript Integration

**wwwroot/js/maplibre-deckgl-integration.js**:
```javascript
// Update tile source configuration
function createMapLibreMap(config) {
    // Use Azure Front Door URLs for tile sources
    const tileBaseUrl = config.azureTiles?.cdnUrl || config.azureTiles?.baseUrl;
    
    if (tileBaseUrl && config.sources) {
        Object.keys(config.sources).forEach(sourceKey => {
            if (config.sources[sourceKey].type === 'raster') {
                config.sources[sourceKey].tiles = config.sources[sourceKey].tiles.map(tile => 
                    tile.replace('/tiles/', `${tileBaseUrl}/tiles/`)
                );
            }
        });
    }
    
    // Rest of existing map creation logic...
}
```

## Phase 4: Testing and Validation

### 4.1 Pre-Migration Testing
```bash
# Test local tiles are accessible
curl -I http://localhost:5214/tiles/parker-twi/18/59899/105599.png

# Verify file count
find MapSandBox/wwwroot/tiles -name "*.png" | wc -l
```

### 4.2 Post-Migration Testing
```bash
# Test Azure Storage direct access
curl -I https://mapsandboxtiles.z13.web.core.windows.net/tiles/parker-twi/18/59899/105599.png

# Test Front Door access
curl -I https://mapsandbox-tiles-[random].z01.azurefd.net/tiles/parker-twi/18/59899/105599.png

# Verify headers
curl -I https://mapsandbox-tiles-[random].z01.azurefd.net/tiles/parker-twi/18/59899/105599.png | grep -i cache
```

### 4.3 Performance Testing
```javascript
// Browser console test
const testUrls = [
    'https://mapsandbox-tiles-[random].z01.azurefd.net/tiles/parker-twi/18/59899/105599.png',
    'https://mapsandbox-tiles-[random].z01.azurefd.net/tiles/parker-twi/18/59899/105572.png'
];

testUrls.forEach(async (url, index) => {
    const start = performance.now();
    try {
        const response = await fetch(url);
        const end = performance.now();
        console.log(`Tile ${index + 1}: ${response.status} - ${(end - start).toFixed(2)}ms`);
    } catch (error) {
        console.error(`Tile ${index + 1} failed:`, error);
    }
});
```

## Phase 5: Deployment and Cleanup

### 5.1 Environment Variables
```bash
# Production environment variables
AZURE_TILES_BASE_URL=https://mapsandboxtiles.z13.web.core.windows.net
AZURE_TILES_CDN_URL=https://mapsandbox-tiles-[random].z01.azurefd.net
AZURE_TILES_USE_CDN=true
```

### 5.2 Cleanup Local Files
```bash
# After successful migration and testing
echo "Backing up local tiles..."
tar -czf tiles-backup-$(date +%Y%m%d).tar.gz MapSandBox/wwwroot/tiles/

echo "Removing local tiles..."
rm -rf MapSandBox/wwwroot/tiles/

echo "Updating .gitignore..."
echo "tiles-backup-*.tar.gz" >> .gitignore
```

### 5.3 Monitoring and Maintenance
```bash
# Monitor Front Door usage and analytics
az afd log list \
    --profile-name $FRONTDOOR_PROFILE \
    --resource-group $RESOURCE_GROUP

# Monitor storage costs
az consumption usage list \
    --billing-period-name $(date +%Y%m) \
    --query "[?contains(instanceName, 'mapsandboxtiles')]"

# Get Front Door endpoint hostname
az afd endpoint show \
    --resource-group $RESOURCE_GROUP \
    --profile-name $FRONTDOOR_PROFILE \
    --endpoint-name $FRONTDOOR_ENDPOINT \
    --query "hostName" --output tsv
```

## Cost Estimates

### Monthly Operational Costs:
- **Storage**: $0.14/month (4.8GB)
- **Front Door Standard**: $22/month (base) + $0.12/GB (10GB transfer) = $23.20/month
- **Operations**: $0.05/month (API calls)
- **Total**: ~$23.40/month (Note: Front Door is more expensive than classic CDN but offers better performance and features)

### One-time Migration Cost:
- **Upload operations**: $0.05
- **Initial Front Door cache**: Free

## Timeline

- **Phase 1** (Infrastructure): 1 hour
- **Phase 2** (Upload Utility): 4 hours development + 2 hours upload time
- **Phase 3** (App Integration): 3 hours
- **Phase 4** (Testing): 2 hours
- **Phase 5** (Deployment): 1 hour

**Total**: ~11 hours over 2-3 days

## Rollback Plan

1. Keep local tiles backup for 30 days
2. If issues occur, restore from backup:
   ```bash
   tar -xzf tiles-backup-YYYYMMDD.tar.gz
   ```
3. Revert configuration changes in git
4. Update DNS/Front Door settings if using custom domain

## Success Criteria

- [ ] All 1.25M tiles uploaded successfully
- [ ] Application loads tiles from Azure Front Door
- [ ] Page load times improved (target: <2s initial load)
- [ ] No broken tile requests (404s)
- [ ] Local wwwroot reduced from 4.8GB to <100MB
- [ ] Build times improved (target: <30s)