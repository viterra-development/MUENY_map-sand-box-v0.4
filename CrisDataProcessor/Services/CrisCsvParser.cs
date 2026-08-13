using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;
using MapSandBox.Models;
using Microsoft.Extensions.Logging;
using NetTopologySuite.Geometries;
using NetTopologySuite.IO;
using NetTopologySuite.Features;

namespace CrisDataProcessor.Services;

public class CrisCsvParser
{
    private readonly ILogger<CrisCsvParser> _logger;
    private readonly Polygon _parkerCountyPolygon;

    public CrisCsvParser(ILogger<CrisCsvParser> logger)
    {
        _logger = logger;
        _parkerCountyPolygon = LoadParkerCountyPolygon();
    }

    private Polygon LoadParkerCountyPolygon()
    {
        try
        {
            var geoJsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "parker-county-boundary.geojson");
            var geoJsonText = File.ReadAllText(geoJsonPath);
            var reader = new GeoJsonReader();
            var featureCollection = reader.Read<NetTopologySuite.Features.FeatureCollection>(geoJsonText);

            if (featureCollection.Count > 0 && featureCollection[0].Geometry is Polygon polygon)
            {
                _logger.LogInformation("Loaded Parker County polygon with {PointCount} boundary points",
                    polygon.ExteriorRing.NumPoints);
                return polygon;
            }

            throw new InvalidOperationException("Parker County GeoJSON does not contain a valid polygon");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load Parker County boundary polygon. Falling back to bounding box check.");
            // Fallback: create a simple rectangular polygon using the original bounds
            var geometryFactory = new GeometryFactory();
            var coordinates = new[]
            {
                new Coordinate(-98.0, 32.5),
                new Coordinate(-97.0, 32.5),
                new Coordinate(-97.0, 33.0),
                new Coordinate(-98.0, 33.0),
                new Coordinate(-98.0, 32.5)
            };
            return geometryFactory.CreatePolygon(coordinates);
        }
    }

    public async Task<List<CrashCsvRecord>> ParseCrashCsvAsync(string filePath)
    {
        _logger.LogInformation("Parsing crash CSV file: {FilePath}", filePath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLower(),
            HeaderValidated = null,
            MissingFieldFound = null
        };

        using var reader = new FileReader(filePath);
        using var csv = new CsvReader(reader, config);

        var records = new List<CrashCsvRecord>();
        await foreach (var record in csv.GetRecordsAsync<CrashCsvRecord>())
        {
            records.Add(record);
        }

        _logger.LogInformation("Parsed {Count} crash records from CSV", records.Count);
        return records;
    }

    public async Task<List<PersonCsvRecord>> ParsePersonCsvAsync(string filePath)
    {
        _logger.LogInformation("Parsing person CSV file: {FilePath}", filePath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLower(),
            HeaderValidated = null,
            MissingFieldFound = null
        };

        using var reader = new FileReader(filePath);
        using var csv = new CsvReader(reader, config);

        var records = new List<PersonCsvRecord>();
        await foreach (var record in csv.GetRecordsAsync<PersonCsvRecord>())
        {
            records.Add(record);
        }

        _logger.LogInformation("Parsed {Count} person records from CSV", records.Count);
        return records;
    }

    public async Task<List<UnitCsvRecord>> ParseUnitCsvAsync(string filePath)
    {
        _logger.LogInformation("Parsing unit CSV file: {FilePath}", filePath);

        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            PrepareHeaderForMatch = args => args.Header.ToLower(),
            HeaderValidated = null,
            MissingFieldFound = null
        };

        using var reader = new FileReader(filePath);
        using var csv = new CsvReader(reader, config);

        var records = new List<UnitCsvRecord>();
        await foreach (var record in csv.GetRecordsAsync<UnitCsvRecord>())
        {
            records.Add(record);
        }

        _logger.LogInformation("Parsed {Count} unit records from CSV", records.Count);
        return records;
    }

    public CrashRecord ConvertToCrashRecord(CrashCsvRecord csvRecord,
        List<PersonCsvRecord> personRecords,
        List<UnitCsvRecord> unitRecords)
    {
        var crash = new CrashRecord
        {
            CrashId = csvRecord.CrashId ?? ""
        };

        // Parse date and time (CRIS exports use US-format MM/dd/yyyy regardless of host locale)
        if (DateTime.TryParse($"{csvRecord.CrashDate} {csvRecord.CrashTime}", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var crashDateTime))
        {
            crash.CrashDateTime = crashDateTime;
        }

        // Parse coordinates (only if both fields have actual values)
        if (!string.IsNullOrWhiteSpace(csvRecord.Latitude) && !string.IsNullOrWhiteSpace(csvRecord.Longitude) &&
            decimal.TryParse(csvRecord.Latitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lat) &&
            decimal.TryParse(csvRecord.Longitude, NumberStyles.Float, CultureInfo.InvariantCulture, out var lng))
        {
            crash.Latitude = lat;
            crash.Longitude = lng;
        }
        // If coordinates are missing/empty, leave them as 0 but we'll filter these out in validation

        crash.Severity = ParseCrisSeverity(csvRecord.CrashSeverity);

        // Parse weather condition with code mapping
        crash.WeatherCondition = MapWeatherConditionCode(csvRecord.WeatherCondition ?? "");
        crash.LightCondition = MapLightConditionCode(csvRecord.LightCondition ?? "");
        crash.RoadwayCondition = MapSurfaceConditionCode(csvRecord.SurfaceCondition ?? "");

        // Parse private property flag
        crash.IsPrivateProperty = csvRecord.PrivatePropertyFlag?.ToUpper() == "Y";

        // Parse located flag (whether crash was successfully geocoded)
        crash.IsLocated = csvRecord.LocatedFlag?.ToUpper() == "Y";

        // Add related persons
        crash.Persons = personRecords
            .Where(p => p.CrashId == crash.CrashId)
            .Select(ConvertToPersonInfo)
            .ToList();

        // Add related vehicles
        crash.Vehicles = unitRecords
            .Where(u => u.CrashId == crash.CrashId)
            .Select(u => ConvertToVehicleInfo(u, personRecords))
            .ToList();

        return crash;
    }

    public bool ValidateCrashRecord(CrashRecord crash)
    {
        // Exclude private property crashes (they often have 0,0 coordinates for privacy)
        if (crash.IsPrivateProperty)
        {
            _logger.LogDebug("Excluding crash {CrashId}: Private property crash", crash.CrashId);
            return false;
        }

        // Exclude crashes that couldn't be accurately located/geocoded
        if (!crash.IsLocated)
        {
            _logger.LogDebug("Excluding crash {CrashId}: Could not be accurately located (Located_Fl = N)", crash.CrashId);
            return false;
        }

        var validationErrors = new List<string>();

        // Validate required fields
        if (string.IsNullOrWhiteSpace(crash.CrashId))
            validationErrors.Add("CrashId is required");

        if (crash.CrashDateTime == default)
            validationErrors.Add("CrashDateTime is required");

        // Validate coordinates exist and are within Parker County bounds
        if (crash.Latitude == 0 && crash.Longitude == 0)
            validationErrors.Add("Missing coordinates (0, 0)");
        else if (!IsWithinParkerCounty(crash.Latitude, crash.Longitude))
            validationErrors.Add($"Coordinates ({crash.Latitude}, {crash.Longitude}) are outside Parker County bounds");

        // Warn about missing environmental data (don't fail, just warn)
        if (string.IsNullOrWhiteSpace(crash.WeatherCondition))
            _logger.LogWarning("Crash {CrashId} has missing weather condition data", crash.CrashId);

        if (string.IsNullOrWhiteSpace(crash.RoadwayCondition))
            _logger.LogWarning("Crash {CrashId} has missing roadway condition data", crash.CrashId);

        if (validationErrors.Any())
        {
            _logger.LogWarning("Validation failed for crash {CrashId}: {Errors}",
                crash.CrashId, string.Join(", ", validationErrors));
            return false;
        }

        return true;
    }

    private bool IsWithinParkerCounty(decimal latitude, decimal longitude)
    {
        try
        {
            var geometryFactory = new GeometryFactory();
            var point = geometryFactory.CreatePoint(new Coordinate((double)longitude, (double)latitude));
            return _parkerCountyPolygon.Contains(point);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error checking if point ({Latitude}, {Longitude}) is within Parker County polygon. Using fallback bounds check.", latitude, longitude);
            // Fallback to simple bounds check if polygon operation fails
            return latitude >= 32.5m && latitude <= 33.0m &&
                   longitude >= -98.0m && longitude <= -97.0m;
        }
    }

    private KabcoSeverity ParseCrisSeverity(string? severity)
    {
        // TxDOT CRIS public-extract lookup for Crash_Sev_ID:
        //   0 = Unknown, 1 = Suspected Serious Injury (A), 2 = Suspected Minor Injury (B),
        //   3 = Possible Injury (C), 4 = Killed/Fatal (K), 5 = Not Injured (O)
        // Note: fatal is code 4, NOT code 1.
        return severity switch
        {
            "1" => KabcoSeverity.A_IncapacitatingInjury,
            "2" => KabcoSeverity.B_NonIncapacitatingInjury,
            "3" => KabcoSeverity.C_PossibleInjury,
            "4" => KabcoSeverity.K_Fatal,
            "5" => KabcoSeverity.O_NoInjury,
            _ => KabcoSeverity.Unknown
        };
    }

    private KabcoSeverity ParseKabcoSeverity(string? severity)
    {
        // TxDOT CRIS public-extract lookup for Prsn_Injry_Sev_ID (same code order
        // as Crash_Sev_ID): 1=A, 2=B, 3=C, 4=K (fatal), 5=O, else Unknown.
        return severity switch
        {
            "1" => KabcoSeverity.A_IncapacitatingInjury,
            "2" => KabcoSeverity.B_NonIncapacitatingInjury,
            "3" => KabcoSeverity.C_PossibleInjury,
            "4" => KabcoSeverity.K_Fatal,
            "5" => KabcoSeverity.O_NoInjury,
            _ => KabcoSeverity.Unknown
        };
    }

    private PersonInfo ConvertToPersonInfo(PersonCsvRecord personRecord)
    {
        return new PersonInfo
        {
            PersonId = $"{personRecord.CrashId}-{personRecord.PersonNumber}", // Create unique ID
            PersonNumber = int.TryParse(personRecord.PersonNumber, out var num) ? num : 0,
            PersonType = personRecord.PersonType ?? "",
            InjurySeverity = ParseKabcoSeverity(personRecord.InjurySeverity),
            Age = int.TryParse(personRecord.Age, out var age) ? age : null,
            Gender = personRecord.Gender ?? "",
            EthnicStatus = personRecord.EthnicStatus ?? "",
            AlcoholSuspected = ParseBooleanField(personRecord.AlcoholSuspected),
            DrugSuspected = ParseBooleanField(personRecord.DrugSuspected)
        };
    }

    private VehicleInfo ConvertToVehicleInfo(UnitCsvRecord unitRecord, List<PersonCsvRecord> personRecords)
    {
        var vehicle = new VehicleInfo
        {
            UnitId = $"{unitRecord.CrashId}-{unitRecord.UnitNumber}", // Create unique ID
            UnitNumber = int.TryParse(unitRecord.UnitNumber, out var num) ? num : 0,
            VehicleType = unitRecord.VehicleType ?? "",
            VehicleModel = unitRecord.VehicleModel ?? "",
            VehicleYear = int.TryParse(unitRecord.VehicleYear, out var year) ? year : null,
            TravelDirection = unitRecord.TravelDirection ?? "",
            MovementPriorToCrash = unitRecord.MovementPriorToCrash ?? ""
        };

        // Add occupants for this vehicle
        vehicle.Occupants = personRecords
            .Where(p => p.CrashId == unitRecord.CrashId) // Simple association - could be enhanced
            .Select(ConvertToPersonInfo)
            .ToList();

        return vehicle;
    }

    private bool ParseBooleanField(string? value)
    {
        return value?.ToUpper() switch
        {
            "YES" or "Y" or "TRUE" or "1" => true,
            _ => false
        };
    }

    public CrashStatistics CalculateStatistics(List<CrashRecord> crashes)
    {
        var stats = new CrashStatistics
        {
            TotalCrashes = crashes.Count,
            FatalCrashes = crashes.Count(c => c.Severity == KabcoSeverity.K_Fatal),
            InjuryCrashes = crashes.Count(c => c.Severity is KabcoSeverity.A_IncapacitatingInjury
                or KabcoSeverity.B_NonIncapacitatingInjury
                or KabcoSeverity.C_PossibleInjury),
            PropertyDamageOnlyCrashes = crashes.Count(c => c.Severity == KabcoSeverity.O_NoInjury)
        };

        // Calculate severity breakdown
        stats.SeverityBreakdown = crashes
            .GroupBy(c => c.Severity)
            .ToDictionary(g => g.Key, g => g.Count());

        // Calculate contributing factors
        stats.ContributingFactorCounts = crashes
            .SelectMany(c => c.ContributingFactors)
            .GroupBy(f => f.Description)
            .ToDictionary(g => g.Key, g => g.Count());

        _logger.LogInformation("Calculated statistics: {TotalCrashes} total crashes, {FatalCrashes} fatal, {InjuryCrashes} injury",
            stats.TotalCrashes, stats.FatalCrashes, stats.InjuryCrashes);

        return stats;
    }

    /// <summary>
    /// Maps CRIS weather condition codes to descriptive text
    /// Based on TxDOT CRIS system codes
    /// </summary>
    private string MapWeatherConditionCode(string code)
    {
        return code switch
        {
            "0" => "Not Reported",
            "1" => "Clear",
            "2" => "Rain",
            "3" => "Sleet/Hail",
            "4" => "Snow",
            "5" => "Fog/Smoke/Smog",
            "6" => "Dust/Sand",
            "7" => "Severe Crosswinds",
            "8" => "Blowing Sand/Soil/Dirt/Snow",
            "9" => "Freezing Rain/Drizzle",
            "10" => "Other",
            "11" => "Clear", // Most common in our data
            "12" => "Cloudy", // Second most common
            _ => code // Return original code if unknown
        };
    }

    /// <summary>
    /// Maps CRIS surface condition codes to descriptive text
    /// </summary>
    private string MapSurfaceConditionCode(string code)
    {
        return code switch
        {
            "0" => "Not Reported",
            "1" => "Dry", // Most common
            "2" => "Wet", // Second most common
            "3" => "Snow",
            "4" => "Ice",
            "5" => "Slush",
            "6" => "Sand/Mud/Dirt/Oil/Gravel",
            "7" => "Water (Standing/Moving)",
            "8" => "Debris",
            "9" => "Other",
            "10" => "Unknown",
            _ => code // Return original code if unknown
        };
    }

    /// <summary>
    /// Maps CRIS light condition codes to descriptive text
    /// </summary>
    private string MapLightConditionCode(string code)
    {
        return code switch
        {
            "0" => "Not Reported",
            "1" => "Daylight", // Most common
            "2" => "Dusk",
            "3" => "Dawn",
            "4" => "Dark (Lighted)",
            "5" => "Dark (Not Lighted)",
            "6" => "Dark (Unknown Lighting)",
            "7" => "Other",
            "8" => "Unknown",
            _ => code // Return original code if unknown
        };
    }
}

internal class FileReader : StreamReader
{
    public FileReader(string path) : base(path)
    {
    }
}