namespace TripGenProcessor.Models;

/// <summary>
/// ITE Trip Generation rate for a specific land use code.
/// Rates from ITE Trip Generation Manual, 10th/11th Edition.
/// </summary>
public record IteRate(
    int IteCode,
    string LandUse,
    string Unit,
    double DailyRate,
    double AmPeakKFactor,
    double PmPeakKFactor
);

/// <summary>
/// Static lookup table of ITE rates for Parker County land uses.
/// </summary>
public static class IteRateLookup
{
    public static readonly Dictionary<int, IteRate> Rates = new()
    {
        // Residential
        [210] = new(210, "Single-Family Detached", "Dwelling Unit", 9.44, 0.075, 0.100),
        [220] = new(220, "Multifamily (Low-Rise)", "Dwelling Unit", 6.65, 0.071, 0.087),
        [230] = new(230, "Condo/Townhouse", "Dwelling Unit", 5.81, 0.068, 0.082),
        [240] = new(240, "Mobile Home Park", "Dwelling Unit", 4.99, 0.060, 0.078),

        // Lodging
        [310] = new(310, "Hotel", "Room", 8.36, 0.053, 0.060),

        // Institutional
        [520] = new(520, "Elementary School", "Student", 1.29, 0.250, 0.170),
        [522] = new(522, "Middle School", "Student", 1.19, 0.200, 0.160),
        [530] = new(530, "High School", "Student", 1.71, 0.190, 0.140),
        [560] = new(560, "Church", "1000 sqft GFA", 9.11, 0.150, 0.100),

        // Office
        [710] = new(710, "General Office", "1000 sqft GFA", 9.74, 0.140, 0.170),
        [720] = new(720, "Medical Office", "1000 sqft GFA", 34.80, 0.085, 0.098),

        // Retail / Commercial
        [820] = new(820, "Shopping Center/Retail", "1000 sqft GLA", 37.75, 0.030, 0.090),
        [850] = new(850, "Supermarket", "1000 sqft GFA", 106.78, 0.060, 0.096),
        [912] = new(912, "Drive-in Bank", "1000 sqft GFA", 148.15, 0.120, 0.230),
        [932] = new(932, "Restaurant (Sit-Down)", "1000 sqft GFA", 83.84, 0.080, 0.090),
        [934] = new(934, "Fast Food (w/ Drive-Through)", "1000 sqft GFA", 470.95, 0.100, 0.070),
        [944] = new(944, "Gas Station w/ Store", "Pump", 168.56, 0.066, 0.088),

        // Industrial / Warehouse
        [110] = new(110, "Light Industrial", "1000 sqft GFA", 6.97, 0.085, 0.085),
        [140] = new(140, "Manufacturing", "1000 sqft GFA", 3.93, 0.085, 0.085),
        [151] = new(151, "Mini-Warehouse", "1000 sqft GFA", 2.50, 0.070, 0.080),

        // Recreation
        [430] = new(430, "Golf Course", "Hole", 30.38, 0.074, 0.091),
    };

    /// <summary>
    /// Maps Parker County CAD state_cd to ITE code.
    /// Returns null for land uses that generate 0 trips (vacant, ag, utilities, minerals).
    /// </summary>
    public static readonly Dictionary<string, int?> CadToIte = new(StringComparer.OrdinalIgnoreCase)
    {
        // Residential
        ["A1"] = 210,   // Single Family Residence
        ["A2"] = 240,   // Mobile Home
        ["B1"] = 220,   // Multi-Family (2-4 units)
        ["B2"] = 220,   // Multi-Family (5+ units)

        // Vacant / Agricultural / Mineral — 0 trips
        ["C1"] = null,  // Vacant Residential
        ["C2"] = null,  // Vacant Commercial
        ["D1"] = null,  // Qualified Agricultural
        ["D2"] = null,  // Non-Qualified Agricultural
        ["E1"] = null,  // Farm/Ranch Improvement (rural)
        ["E2"] = null,  // Farm/Ranch Improvement (non-qual)
        ["G1"] = null,  // Oil/Gas/Minerals

        // Commercial / Industrial
        ["F1"] = 820,   // Commercial Real Property
        ["F2"] = 110,   // Industrial Real Property

        // Personal Property
        ["L1"] = 820,   // Commercial Personal Property
        ["L2"] = 110,   // Industrial Personal Property

        // Utilities — 0 trips
        ["J1"] = null, ["J2"] = null, ["J3"] = null, ["J4"] = null,
        ["J5"] = null, ["J6"] = null, ["J7"] = null, ["J8"] = null, ["J9"] = null,

        // Exempt — context-dependent, default to office for government
        ["X1"] = 710,   // Exempt (government) → General Office
        ["X2"] = 710,   // Exempt (government) → General Office
        ["X3"] = 560,   // Exempt (religious) → Church
        ["X4"] = 520,   // Exempt (school) → Elementary (refined in classifier)

        // Pseudo-code emitted by the OSM enrichment step (not a real PCAD code)
        ["GOLF"] = 430, // Golf course → ITE 430
    };
}
