using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using System.Collections.Concurrent;

namespace TileUploadUtility;

class Program
{
    private static async Task Main(string[] args)
    {
        var config = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var connectionString =
            Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING")
            ?? config.GetConnectionString("AzureStorage")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__AzureStorage");
        var cliSourceDir = GetSourceDirectoryFromArgs(args);
        var envSourceDir = Environment.GetEnvironmentVariable("UPLOAD_SOURCE_DIR");
        var sourceDirectory = cliSourceDir
            ?? envSourceDir
            ?? config["SourceDirectory"]
            ?? "../MapSandBox/wwwroot/tiles";
        var containerName = config["ContainerName"] ?? "$web";
        
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            Console.WriteLine("ERROR: Azure Storage connection string not found.\n"
                + "Set AZURE_STORAGE_CONNECTION_STRING in your .env or environment, "
                + "or provide ConnectionStrings:AzureStorage in appsettings.json.");
            return;
        }
        
        Console.WriteLine($"Using source directory: {sourceDirectory}");

        var uploader = new TileUploader(connectionString, containerName);
        await uploader.UploadTilesAsync(sourceDirectory);
    }

    private static string? GetSourceDirectoryFromArgs(string[] args)
    {
        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            // Forms: --source-dir=/path or --source-dir /path
            if (arg.StartsWith("--source-dir="))
            {
                return arg.Substring("--source-dir=".Length);
            }
            if (arg == "--source-dir" && i + 1 < args.Length)
            {
                return args[i + 1];
            }

            // Short forms: -s=/path or -s /path
            if (arg.StartsWith("-s="))
            {
                return arg.Substring(3);
            }
            if (arg == "-s" && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }

        return null;
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
        if (!Directory.Exists(sourceDirectory))
        {
            Console.WriteLine($"ERROR: Source directory not found: {sourceDirectory}");
            return;
        }

        var tileFiles = Directory.GetFiles(sourceDirectory, "*.png", SearchOption.AllDirectories);
        Console.WriteLine($"Found {tileFiles.Length:N0} tile files to upload");

        if (tileFiles.Length == 0)
        {
            Console.WriteLine("No PNG files found to upload.");
            return;
        }

        var semaphore = new SemaphoreSlim(50); // Limit concurrent uploads
        var uploaded = 0;
        var failed = new ConcurrentBag<string>();
        var startTime = DateTime.Now;

        var tasks = tileFiles.Select(async filePath =>
        {
            await semaphore.WaitAsync();
            try
            {
                await UploadTileAsync(filePath, sourceDirectory);
                var count = Interlocked.Increment(ref uploaded);
                
                if (count % 1000 == 0)
                {
                    var elapsed = DateTime.Now - startTime;
                    var rate = count / elapsed.TotalMinutes;
                    Console.WriteLine($"Uploaded {count:N0} / {tileFiles.Length:N0} files ({rate:F0} files/min)");
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
        
        var totalTime = DateTime.Now - startTime;
        Console.WriteLine($"Upload completed: {uploaded:N0} successful, {failed.Count} failed");
        Console.WriteLine($"Total time: {totalTime.TotalMinutes:F1} minutes");
        
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
        var headers = new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            CacheControl = "public, max-age=31536000", // 1 year
            ContentType = "image/png"
        };

        // Overwrite existing blob and then apply headers
        await blobClient.UploadAsync(filePath, overwrite: true);
        await blobClient.SetHttpHeadersAsync(headers);
    }
}
