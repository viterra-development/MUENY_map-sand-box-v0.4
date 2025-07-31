using System.Text.Json.Serialization;

namespace TCDS.Importer.Models;

public class TrafficCountData
{
    [JsonPropertyName("locationId")]
    public string LocationId { get; set; } = string.Empty;

    [JsonPropertyName("locationInfo")]
    public LocationInfo LocationInfo { get; set; } = new();

    [JsonPropertyName("aadtData")]
    public List<AadtRecord> AadtData { get; set; } = new();

    [JsonPropertyName("volumeCountData")]
    public List<VolumeCountRecord> VolumeCountData { get; set; } = new();

    [JsonPropertyName("volumeTrendData")]
    public List<VolumeTrendRecord> VolumeTrendData { get; set; } = new();

    [JsonPropertyName("speedData")]
    public List<SpeedRecord> SpeedData { get; set; } = new();

    [JsonPropertyName("classificationData")]
    public List<ClassificationRecord> ClassificationData { get; set; } = new();

    [JsonPropertyName("extractedAt")]
    public DateTime ExtractedAt { get; set; } = DateTime.UtcNow;
}

public class LocationInfo
{
    [JsonPropertyName("locationId")]
    public string LocationId { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("sfGroup")]
    public string SfGroup { get; set; } = string.Empty;

    [JsonPropertyName("afGroup")]
    public string AfGroup { get; set; } = string.Empty;

    [JsonPropertyName("gfGroup")]
    public string GfGroup { get; set; } = string.Empty;

    [JsonPropertyName("wimGroup")]
    public string WimGroup { get; set; } = string.Empty;

    [JsonPropertyName("qcGroup")]
    public string QcGroup { get; set; } = string.Empty;

    [JsonPropertyName("fnctClass")]
    public string FnctClass { get; set; } = string.Empty;

    [JsonPropertyName("locatedOn")]
    public string LocatedOn { get; set; } = string.Empty;

    [JsonPropertyName("locOnAlias")]
    public string LocOnAlias { get; set; } = string.Empty;

    [JsonPropertyName("mpoId")]
    public string MpoId { get; set; } = string.Empty;

    [JsonPropertyName("hpmsId")]
    public string HpmsId { get; set; } = string.Empty;

    [JsonPropertyName("routeType")]
    public string RouteType { get; set; } = string.Empty;

    [JsonPropertyName("route")]
    public string Route { get; set; } = string.Empty;

    [JsonPropertyName("active")]
    public string Active { get; set; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("latitude")]
    public decimal? Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public decimal? Longitude { get; set; }
}

public class AadtRecord
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("aadt")]
    public int? Aadt { get; set; }

    [JsonPropertyName("dhv30")]
    public int? Dhv30 { get; set; }

    [JsonPropertyName("kPercent")]
    public decimal? KPercent { get; set; }

    [JsonPropertyName("dPercent")]
    public decimal? DPercent { get; set; }

    [JsonPropertyName("pa")]
    public string Pa { get; set; } = string.Empty;

    [JsonPropertyName("bc")]
    public string Bc { get; set; } = string.Empty;

    [JsonPropertyName("src")]
    public string Src { get; set; } = string.Empty;
}

public class VolumeCountRecord
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class VolumeTrendRecord
{
    [JsonPropertyName("year")]
    public int Year { get; set; }

    [JsonPropertyName("annualGrowth")]
    public decimal? AnnualGrowth { get; set; }
}

public class SpeedRecord
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("pace")]
    public decimal? Pace { get; set; }

    [JsonPropertyName("mph85th")]
    public decimal? Mph85th { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}

public class ClassificationRecord
{
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }

    [JsonPropertyName("interval")]
    public int Interval { get; set; }

    [JsonPropertyName("total")]
    public int Total { get; set; }
}