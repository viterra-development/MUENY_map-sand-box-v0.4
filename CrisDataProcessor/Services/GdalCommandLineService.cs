using System.Diagnostics;
using System.Globalization;
using MapSandBox.Models;
using Microsoft.Extensions.Logging;

namespace CrisDataProcessor.Services;

public class GdalCommandLineService : IDisposable
{
    private readonly ILogger<GdalCommandLineService> _logger;
    private readonly CrisModelConfiguration _config;
    private bool _isInitialized = false;
    private string _slopeRasterPath = "";

    // Data quality tracking
    private int _totalSamples = 0;
    private int _successfulSamples = 0;
    private int _outOfBoundsSamples = 0;
    private int _errorSamples = 0;
    private int _invalidValueSamples = 0;

    public GdalCommandLineService(ILogger<GdalCommandLineService> logger, CrisModelConfiguration config)
    {
        _logger = logger;
        _config = config;
    }

    public Task<bool> InitializeAsync()
    {
        try
        {
            if (!_config.DEMConfiguration.EnableDEMSampling)
            {
                _logger.LogInformation("DEM sampling is disabled in configuration");
                return Task.FromResult(false);
            }

            _slopeRasterPath = _config.DEMConfiguration.SlopeRasterPath;

            if (!File.Exists(_slopeRasterPath))
            {
                _logger.LogError("Slope raster file not found at: {SlopePath}", _slopeRasterPath);
                return Task.FromResult(false);
            }

            // Test if gdalinfo is available
            if (!IsGdalAvailable())
            {
                _logger.LogError("GDAL command-line tools are not available");
                return Task.FromResult(false);
            }

            // Test if we can read the raster file
            if (!CanReadRasterFile(_slopeRasterPath))
            {
                _logger.LogError("Cannot read slope raster file with GDAL");
                return Task.FromResult(false);
            }

            _logger.LogInformation("GDAL command-line DEM service initialized successfully");
            _logger.LogInformation("Using slope raster: {SlopePath}", _slopeRasterPath);

            _isInitialized = true;
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize GDAL command-line service");
            return Task.FromResult(false);
        }
    }

    public decimal GetSlopeAtCoordinate(double latitude, double longitude)
    {
        _totalSamples++;

        if (!_isInitialized)
        {
            _logger.LogWarning("GDAL service not initialized, cannot sample slope");
            _errorSamples++;
            return 0;
        }

        try
        {
            // Use gdallocationinfo to sample the raster at the given coordinates
            var startInfo = new ProcessStartInfo
            {
                FileName = "gdallocationinfo",
                Arguments = $"-wgs84 -valonly \"{_slopeRasterPath}\" {longitude.ToString(CultureInfo.InvariantCulture)} {latitude.ToString(CultureInfo.InvariantCulture)}",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                _logger.LogWarning("Failed to start gdallocationinfo process");
                _errorSamples++;
                return 0;
            }

            if (!process.WaitForExit(5000)) // 5 second timeout
            {
                try { process.Kill(entireProcessTree: true); } catch { /* process may have just exited */ }
                _errorSamples++;
                _logger.LogWarning("gdallocationinfo timed out after 5s for ({Lat}, {Lon}); returning 0", latitude, longitude);
                return 0;
            }

            if (process.ExitCode != 0)
            {
                var error = process.StandardError.ReadToEnd();

                // Check if this is specifically an out-of-bounds error
                if (error.Contains("Location is off this file") || error.Contains("outside") || string.IsNullOrEmpty(error))
                {
                    _outOfBoundsSamples++;
                    _logger.LogDebug("Coordinates ({Lat}, {Lon}) are outside DEM raster bounds", latitude, longitude);
                }
                else
                {
                    _errorSamples++;
                    _logger.LogDebug("gdallocationinfo failed for ({Lat}, {Lon}): {Error}", latitude, longitude, error);
                }
                return 0;
            }

            var output = process.StandardOutput.ReadToEnd().Trim();

            // Parse the slope value
            if (decimal.TryParse(output, NumberStyles.Float, CultureInfo.InvariantCulture, out var slopeDegrees))
            {
                // Validate slope value (should be 0-90 degrees)
                if (slopeDegrees < 0 || slopeDegrees > 90)
                {
                    _invalidValueSamples++;
                    _logger.LogDebug("Invalid slope value {Slope} at ({Lat}, {Lon}), using 0", slopeDegrees, latitude, longitude);
                    return 0;
                }

                _successfulSamples++;
                _logger.LogDebug("Sampled slope: {Slope}° at ({Lat}, {Lon})", slopeDegrees, latitude, longitude);
                return slopeDegrees;
            }
            else
            {
                _invalidValueSamples++;
                _logger.LogDebug("Could not parse slope value '{Output}' at ({Lat}, {Lon})", output, latitude, longitude);
                return 0;
            }
        }
        catch (Exception ex)
        {
            _errorSamples++;
            _logger.LogWarning(ex, "Failed to sample slope at ({Lat}, {Lon})", latitude, longitude);
            return 0;
        }
    }

    public void LogDataQualityMetrics()
    {
        if (_totalSamples == 0)
        {
            _logger.LogInformation("No slope sampling attempts recorded");
            return;
        }

        var successRate = (double)_successfulSamples / _totalSamples * 100;
        var outOfBoundsRate = (double)_outOfBoundsSamples / _totalSamples * 100;
        var errorRate = (double)_errorSamples / _totalSamples * 100;
        var invalidRate = (double)_invalidValueSamples / _totalSamples * 100;

        _logger.LogInformation("DEM Slope Sampling Quality Metrics:");
        _logger.LogInformation("  Total samples attempted: {Total}", _totalSamples);
        _logger.LogInformation("  Successful samples: {Success} ({SuccessRate:F1}%)", _successfulSamples, successRate);
        _logger.LogInformation("  Out of bounds: {OutOfBounds} ({OutOfBoundsRate:F1}%)", _outOfBoundsSamples, outOfBoundsRate);
        _logger.LogInformation("  Processing errors: {Errors} ({ErrorRate:F1}%)", _errorSamples, errorRate);
        _logger.LogInformation("  Invalid values: {Invalid} ({InvalidRate:F1}%)", _invalidValueSamples, invalidRate);

        if (successRate < 50)
        {
            _logger.LogWarning("Low success rate ({SuccessRate:F1}%) indicates DEM coverage or coordinate system issues", successRate);
        }

        if (outOfBoundsRate > 80)
        {
            _logger.LogWarning("High out-of-bounds rate ({OutOfBoundsRate:F1}%) suggests DEM raster may be too small for data extent", outOfBoundsRate);
        }
    }

    public (int Total, int Successful, int OutOfBounds, int Errors, int Invalid) GetQualityMetrics()
    {
        return (_totalSamples, _successfulSamples, _outOfBoundsSamples, _errorSamples, _invalidValueSamples);
    }

    public decimal ConvertSlopeToPercentage(decimal slopeDegrees)
    {
        // Convert slope from degrees to percentage: tan(degrees) * 100
        var radians = (double)slopeDegrees * Math.PI / 180.0;
        var percentage = (decimal)(Math.Tan(radians) * 100.0);
        return Math.Round(percentage, 2);
    }

    public string CategorizeSlopeValue(decimal slopeDegrees)
    {
        return slopeDegrees switch
        {
            <= 2 => "Flat",
            <= 5 => "Moderate",
            _ => "Steep"
        };
    }

    private bool IsGdalAvailable()
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gdalinfo",
                Arguments = "--version",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(3000);
            var output = process.StandardOutput.ReadToEnd();

            _logger.LogInformation("GDAL version: {Version}", output.Trim());
            return process.ExitCode == 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check GDAL availability");
            return false;
        }
    }

    private bool CanReadRasterFile(string filePath)
    {
        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "gdalinfo",
                Arguments = $"\"{filePath}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(startInfo);
            if (process == null) return false;

            process.WaitForExit(5000);

            if (process.ExitCode == 0)
            {
                var output = process.StandardOutput.ReadToEnd();
                _logger.LogDebug("Raster file info: {Info}", output.Substring(0, Math.Min(500, output.Length)));
                return true;
            }
            else
            {
                var error = process.StandardError.ReadToEnd();
                _logger.LogWarning("Failed to read raster file: {Error}", error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to test raster file reading");
            return false;
        }
    }

    public void Dispose()
    {
        _isInitialized = false;
    }
}