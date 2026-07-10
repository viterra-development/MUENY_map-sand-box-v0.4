using Microsoft.Extensions.Logging;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Classifies parcels by mapping Parker County CAD state_cd codes
/// to ITE land use codes. Handles edge cases, exempt properties,
/// and provides intelligent fallbacks.
/// </summary>
public class LandUseClassifier
{
    private readonly ILogger<LandUseClassifier> _logger;
    private int _classifiedCount;
    private int _fallbackCount;
    private int _skippedCount;

    public LandUseClassifier(ILogger<LandUseClassifier> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Classify a parcel's land use and determine its ITE code.
    /// Returns null if the parcel generates 0 trips.
    ///
    /// When state_cd is populated, uses the canonical PCAD→ITE lookup.
    /// When state_cd is null/empty (as with TxGIO's bulk parcels export),
    /// falls back to a heuristic ladder using OWNER_NAME markers, LEGAL_DESC
    /// keywords, IMP_VALUE tiers, and acreage.
    /// </summary>
    public (int? iteCode, string classification, string? notes) Classify(CadParcel parcel)
    {
        var stateCd = (parcel.StateCd ?? "").Trim().ToUpperInvariant();

        // ── No state_cd (TxGIO bulk export path) → run heuristic classifier ──
        if (string.IsNullOrEmpty(stateCd))
        {
            return ClassifyByHeuristics(parcel);
        }

        // Direct lookup
        if (IteRateLookup.CadToIte.TryGetValue(stateCd, out var iteCode))
        {
            if (iteCode == null)
            {
                _skippedCount++;
                return (null, GetCategoryName(stateCd), "Zero-trip land use");
            }

            // Refine exempt properties
            if (stateCd.StartsWith("X"))
            {
                var refined = RefineExemptProperty(parcel, stateCd);
                _classifiedCount++;
                return refined;
            }

            _classifiedCount++;
            return (iteCode, GetCategoryName(stateCd), null);
        }

        // Fallback: try to match first character
        if (stateCd.Length >= 1)
        {
            var prefix = stateCd[0];
            var fallback = prefix switch
            {
                'A' => (210, "Residential", "Fallback: assumed single-family"),
                'B' => (220, "Residential", "Fallback: assumed multi-family"),
                'F' => (820, "Commercial", "Fallback: assumed retail/commercial"),
                'L' => (820, "Commercial", "Fallback: assumed commercial personal property"),
                _ => ((int?)null, "Unknown", $"Unrecognized state_cd: {stateCd}")
            };

            if (fallback.Item1.HasValue)
            {
                _fallbackCount++;
                return (fallback.Item1, fallback.Item2, fallback.Item3);
            }
        }

        _skippedCount++;
        return (null, "Unknown", $"Unrecognized state_cd: {stateCd}");
    }

    /// <summary>
    /// Heuristic classifier used when state_cd is not populated (TxGIO bulk export).
    /// Ladder — first match wins:
    ///   1. Owner-name markers (city/county government, schools, churches)
    ///   2. Legal-description keywords (commercial, industrial, apartments)
    ///   3. Improvement value + acreage tiers (residential vs. commercial vs. ag)
    ///   4. Default: residential single-family if any improvement value, otherwise 0-trip.
    /// </summary>
    private (int? iteCode, string classification, string? notes) ClassifyByHeuristics(CadParcel parcel)
    {
        var owner   = (parcel.OwnerName  ?? "").ToUpperInvariant();
        var legal   = (parcel.LegalDesc  ?? "").ToUpperInvariant();
        var address = (parcel.SitusStreet ?? "").ToUpperInvariant();
        var acres   = parcel.LegalAcreage;
        var impVal  = parcel.ImprvVal;

        // 1a. Government / municipal / county property → General Office (710)
        if (owner.Contains("CITY OF ") || owner.Contains(" CITY OF") ||
            owner.Contains("COUNTY OF") || owner.Contains("PARKER COUNTY") ||
            owner.Contains("TXDOT") || owner.Contains("STATE OF TEXAS") ||
            owner.Contains("PUBLIC LIBRARY") || owner.Contains("MUNICIPAL"))
        {
            _fallbackCount++;
            return (710, "Institutional", "Heuristic: government owner");
        }
        // 1b. Schools → Elementary (520) as default; middle/high refined by name
        if (owner.Contains("ISD") || owner.Contains("SCHOOL DIST") ||
            owner.Contains("SCHOOL DISTRICT") || legal.Contains("HIGH SCHOOL") ||
            legal.Contains("ELEMENTARY") || legal.Contains("MIDDLE SCHOOL"))
        {
            var probe = owner + " " + legal;
            if (probe.Contains("HIGH")) { _fallbackCount++; return (530, "Institutional", "Heuristic: high school"); }
            if (probe.Contains("MIDDLE") || probe.Contains("JUNIOR"))
                                        { _fallbackCount++; return (522, "Institutional", "Heuristic: middle school"); }
            _fallbackCount++;
            return (520, "Institutional", "Heuristic: school");
        }
        // 1c. Churches / religious → 560
        if (owner.Contains("CHURCH") || owner.Contains("BAPTIST") ||
            owner.Contains("METHODIST") || owner.Contains("CATHOLIC") ||
            owner.Contains("PRESBYTERIAN") || owner.Contains("LUTHERAN") ||
            owner.Contains("ASSEMBLY OF GOD") || owner.Contains("MINISTRIES"))
        {
            _fallbackCount++;
            return (560, "Institutional", "Heuristic: religious owner");
        }
        // 1d. Explicit multi-family / apartments
        if (owner.Contains("APARTMENTS") || owner.Contains("APT ") ||
            legal.Contains("APARTMENTS") || legal.Contains("MULTIFAMILY") ||
            legal.Contains("MULTI-FAMILY") || legal.Contains("DUPLEX"))
        {
            _fallbackCount++;
            return (220, "Residential", "Heuristic: multi-family owner/legal");
        }

        // 2. Franchise / brand names in legal desc → retail commercial
        if (ContainsAny(legal, KnownFranchises))
        {
            _fallbackCount++;
            return (820, "Commercial", "Heuristic: brand name in legal desc");
        }

        // 3. Strong commercial owner markers (specific business types)
        if (ContainsAny(owner, StrongCommercialOwnerMarkers))
        {
            _fallbackCount++;
            return (820, "Commercial", "Heuristic: commercial-specific owner");
        }

        // 4. Legal-description keywords for commercial / industrial / office
        if (legal.Contains("COMMERCIAL") || legal.Contains("RETAIL") ||
            legal.Contains("BUSINESS") || legal.Contains("STORE") ||
            legal.Contains("SHOPPING") || legal.Contains("PLAZA") ||
            legal.Contains("STRIP CENTER") || legal.Contains("OUTLET"))
        {
            _fallbackCount++;
            return (820, "Commercial", "Heuristic: commercial legal desc");
        }
        if (legal.Contains("INDUSTRIAL") || legal.Contains("MANUFACTURING") ||
            legal.Contains("WAREHOUSE") || legal.Contains("DISTRIBUTION"))
        {
            _fallbackCount++;
            return (110, "Industrial", "Heuristic: industrial legal desc");
        }
        if (legal.Contains("OFFICE") || legal.Contains("PROFESSIONAL BUILDING") ||
            legal.Contains("MEDICAL BUILDING"))
        {
            _fallbackCount++;
            return (710, "Commercial", "Heuristic: office legal desc");
        }

        // 5. Business-form owner (LLC/INC/CORP/LP) — signal ONLY.
        //    Per Notion canonical spec, owner-name inference is not authorized to
        //    override state_cd; we allow it only when paired with an unambiguous
        //    second signal (highway address or a commercial-function keyword).
        //    Personal excludes: FAMILY / TRUST / REVOCABLE / LIVING / ETUX / ET UX.
        var isBusinessForm = ContainsAny(owner, BusinessFormMarkers);
        var isPersonal = ContainsAny(owner, PersonalExclusions);

        if (isBusinessForm && !isPersonal)
        {
            if (IsHighwayAddress(address))
            {
                _fallbackCount++;
                return (820, "Commercial", "Heuristic (stopgap): business owner on highway");
            }
            if (owner.Contains("STORAGE") || owner.Contains("RETAIL") ||
                owner.Contains("OUTLET") || owner.Contains("MART"))
            {
                _fallbackCount++;
                return (820, "Commercial", "Heuristic (stopgap): business + commercial function");
            }
        }

        // 6. Value + acreage tiers — RESTRICTED to acknowledged Notion patterns:
        //    zero-value → vacant/ag (skip), large low-value → ag (skip). We do NOT
        //    upgrade high-value small-lot parcels to commercial by rule alone —
        //    that produced ~550 false positives across Aledo subdivisions where a
        //    $500K house on 1-2 acres is baseline residential, not commercial.
        if (impVal <= 0)
        {
            _skippedCount++;
            return (null, "Vacant", "Heuristic (stopgap): no improvement value");
        }
        if (acres > 20 && impVal < 200_000)
        {
            _skippedCount++;
            return (null, "Agricultural", "Heuristic (stopgap): large parcel, low improvement");
        }

        // 7. Default: residential single-family. Parker County is majority residential,
        //    so the safest default when no positive commercial signal fires is a house.
        //    Correct classification for THIS parcel awaits real state_cd from Parker CAD.
        _fallbackCount++;
        return (210, "Residential", "Heuristic (stopgap): default single-family — awaits real state_cd from Parker CAD");
    }

    // Big-box, franchise, and known local commercial brands. Case-insensitive substring match.
    private static readonly string[] KnownFranchises = new[]
    {
        "MCDONALDS", "MCDONALD'S", "WALMART", "WAL-MART", "WAL MART", "TARGET", "STARBUCKS",
        "H-E-B", "H E B", "HEB", "WALGREENS", "CVS", "DOLLAR GENERAL", "DOLLAR TREE",
        "FAMILY DOLLAR", "SONIC", "WHATABURGER", "TACO BELL", "CHILIS", "CHILI'S",
        "WENDYS", "WENDY'S", "SUBWAY", "PIZZA HUT", "APPLEBEES", "APPLEBEE'S", "IHOP",
        "DENNYS", "DENNY'S", "CRACKER BARREL", "HOME DEPOT", "LOWES", "LOWE'S",
        "AUTOZONE", "OREILLY", "O'REILLY", "ADVANCE AUTO", "SHELL", "EXXON", "CHEVRON",
        "TEXACO", "VALERO", "7-ELEVEN", "SEVEN ELEVEN", "CIRCLE K", "QUIKTRIP", "QT",
        "RACETRAC", "MURPHY USA", "TEXAS RV", "DOMINO", "PAPA JOHN", "CHICK-FIL",
        "CHICK FIL", "BUC-EE", "BUCEES", "KROGER", "TOM THUMB", "ALBERTSONS", "SAM'S CLUB",
        "SAMS CLUB", "COSTCO", "WHOLE FOODS", "TRACTOR SUPPLY"
    };

    // Owner names that specifically indicate a commercial function (not just business form).
    private static readonly string[] StrongCommercialOwnerMarkers = new[]
    {
        "RESTAURANT", "MOTEL", "HOTEL", "HOSPITALITY", "AUTOMOTIVE ",
        "TIRE ", "COLLISION", "COLLISION CENTER", "BODY SHOP", "AUTO SALES",
        "USED CARS", "CAR WASH", "CARWASH",
        "MEDICAL CENTER", "DENTAL", "CLINIC", "PHARMACY", "URGENT CARE", "HOSPITAL",
        "BANK ", " BANK", "CREDIT UNION",
        "REAL ESTATE", "REALTY",
        "SHOPPING CENTER", "PLAZA",
        "GAS STATION", "CONVENIENCE STORE", "TRUCK STOP",
        "SELF STORAGE", "MINI STORAGE", "STORAGE ",
        "FITNESS", "GYM ",
        "SALON", "BARBERSHOP", "SPA "
    };

    // Ownership through a business entity form. Signal, not conclusion — must be paired with another marker.
    private static readonly string[] BusinessFormMarkers = new[]
    {
        " LLC", "LLC.", "L.L.C", " INC", "INC.", "INCORPORATED",
        " CORP", "CORP.", "CORPORATION", " CO ", " CO.", "COMPANY",
        "PARTNERSHIP", " LP ", " LP.", "L.P.", " LTD", "LTD.",
        "PROPERTIES", "HOLDINGS", "HOLDING", "INVESTMENT", "CAPITAL",
        "FUNDING", "MANAGEMENT", "SERVICES", "ENTERPRISES"
    };

    // Personal / trust ownership — usually a home even if wrapped in an LLC-looking name.
    private static readonly string[] PersonalExclusions = new[]
    {
        "FAMILY ", "TRUST", "REVOCABLE", "LIVING", "ESTATE OF",
        " ETUX", "ET UX", "ET AL", " ETVIR", "ET VIR"
    };

    private static bool ContainsAny(string haystack, string[] needles)
    {
        foreach (var n in needles) if (haystack.Contains(n)) return true;
        return false;
    }

    // Highway street name patterns. IH/US/SH/FM prefixes plus " HWY" and " HIGHWAY"
    // suffixes (Weatherford uses "FT WORTH HWY", "MINERAL WELLS HWY", "RANGER HWY").
    // Named residential streets like "PALO PINTO ST" don't match — the space before
    // HWY is significant.
    private static bool IsHighwayAddress(string address)
    {
        if (string.IsNullOrEmpty(address)) return false;
        return address.Contains("IH ") || address.Contains("IH-") ||
               address.Contains("I 20") || address.Contains("I-20") ||
               address.Contains("INTERSTATE") ||
               address.Contains("US HWY") || address.Contains("US-") ||
               address.Contains("US HIGHWAY") ||
               address.Contains("STATE HWY") || address.Contains("SH ") ||
               address.Contains("STATE HIGHWAY") ||
               address.Contains("FM ") || address.Contains("FM-") ||
               address.Contains(" HWY") || address.EndsWith("HWY") ||
               address.Contains(" HIGHWAY") ||
               address.Contains("BUSINESS 80") || address.Contains("BANKHEAD");
    }

    /// <summary>
    /// Determine the unit count for a parcel based on its ITE code.
    /// This is the key multiplier in the trip generation formula.
    /// </summary>
    public double GetUnits(CadParcel parcel, int iteCode, double defaultDuPerParcel, double defaultSqftPerAcreFactor)
    {
        var rate = IteRateLookup.Rates.GetValueOrDefault(iteCode);
        if (rate == null) return 0;

        return rate.Unit switch
        {
            "Dwelling Unit" => GetDwellingUnits(parcel, iteCode, defaultDuPerParcel),
            "1000 sqft GFA" or "1000 sqft GLA" => GetThousandSqft(parcel, defaultSqftPerAcreFactor),
            "Student" => EstimateStudents(parcel),
            "Room" => EstimateRooms(parcel),
            "Pump" => EstimatePumps(parcel),
            _ => 1.0
        };
    }

    public void LogStats()
    {
        _logger.LogInformation(
            "Classification stats: {Classified} classified, {Fallback} fallbacks, {Skipped} skipped (0-trip)",
            _classifiedCount, _fallbackCount, _skippedCount);
    }

    private double GetDwellingUnits(CadParcel parcel, int iteCode, double defaultDuPerParcel)
    {
        // Use explicit dwelling units if available
        if (parcel.DwellingUnits.HasValue && parcel.DwellingUnits.Value > 0)
            return parcel.DwellingUnits.Value;

        // For single-family (210), default to 1 DU per parcel
        if (iteCode == 210)
            return defaultDuPerParcel;

        // For multi-family, estimate from improvement size or acreage
        if (iteCode == 220 || iteCode == 230)
        {
            if (parcel.ImprvSqft > 0)
                return Math.Max(1, Math.Floor(parcel.ImprvSqft / 900)); // ~900 sqft per unit

            if (parcel.LegalAcreage > 0)
                return Math.Max(1, Math.Floor(parcel.LegalAcreage * 12)); // ~12 units/acre for low-rise
        }

        // Mobile home parks — estimate from acreage
        if (iteCode == 240 && parcel.LegalAcreage > 0)
            return Math.Max(1, Math.Floor(parcel.LegalAcreage * 6)); // ~6 pads/acre

        return defaultDuPerParcel;
    }

    private double GetThousandSqft(CadParcel parcel, double defaultSqftPerAcreFactor)
    {
        // Use improvement sqft if available
        if (parcel.ImprvSqft > 0)
            return parcel.ImprvSqft / 1000.0;

        // Estimate from acreage with a coverage factor
        if (parcel.LegalAcreage > 0)
        {
            // Typical commercial: 25% lot coverage, so sqft = acres × 43560 × 0.25
            var estimatedSqft = parcel.LegalAcreage * 43560.0 * 0.25;
            return Math.Max(0.5, estimatedSqft / 1000.0);
        }

        // Last resort: use configured default
        return defaultSqftPerAcreFactor / 1000.0;
    }

    private double EstimateStudents(CadParcel parcel)
    {
        // Schools are hard to estimate without enrollment data.
        // Use improvement size as proxy: ~150 sqft per student
        if (parcel.ImprvSqft > 0)
            return Math.Max(50, Math.Floor(parcel.ImprvSqft / 150.0));

        // Fallback: 500 students for average school
        return 500;
    }

    private double EstimateRooms(CadParcel parcel)
    {
        if (parcel.ImprvSqft > 0)
            return Math.Max(10, Math.Floor(parcel.ImprvSqft / 500.0)); // ~500 sqft per room
        return 80; // average hotel
    }

    private double EstimatePumps(CadParcel parcel)
    {
        // Gas stations: estimate from lot size
        if (parcel.LegalAcreage > 0)
            return Math.Max(4, Math.Floor(parcel.LegalAcreage * 43560 / 2000)); // rough estimate
        return 8; // typical gas station
    }

    /// <summary>
    /// Refine exempt (X-code) properties using situs address and improvement data.
    /// </summary>
    private (int? iteCode, string classification, string? notes) RefineExemptProperty(CadParcel parcel, string stateCd)
    {
        var address = (parcel.SitusStreet ?? "").ToUpperInvariant();

        return stateCd switch
        {
            "X4" => RefineSchool(parcel, address),
            "X3" => (560, "Institutional", "Church/Religious"),
            "X1" or "X2" => RefineGovernment(parcel, address),
            _ => (710, "Institutional", $"Exempt: {stateCd}")
        };
    }

    private (int? iteCode, string classification, string? notes) RefineSchool(CadParcel parcel, string address)
    {
        // Try to determine school level from address or name
        if (address.Contains("ELEM") || address.Contains("PRIMARY"))
            return (520, "Institutional", "Elementary School");
        if (address.Contains("MIDDLE") || address.Contains("JUNIOR"))
            return (522, "Institutional", "Middle School");
        if (address.Contains("HIGH") || address.Contains("SENIOR"))
            return (530, "Institutional", "High School");

        // Default to elementary
        return (520, "Institutional", "School (level unknown, defaulted to elementary)");
    }

    private (int? iteCode, string classification, string? notes) RefineGovernment(CadParcel parcel, string address)
    {
        if (address.Contains("FIRE") || address.Contains("STATION"))
            return (710, "Institutional", "Fire Station → General Office");
        if (address.Contains("POLICE") || address.Contains("SHERIFF"))
            return (710, "Institutional", "Law Enforcement → General Office");
        if (address.Contains("LIBRARY"))
            return (710, "Institutional", "Library → General Office");
        if (address.Contains("PARK") || address.Contains("RECREATION"))
            return (null, "Institutional", "Park/Recreation — minimal trip gen");

        return (710, "Institutional", "Government building");
    }

    private static string GetCategoryName(string stateCd) => stateCd switch
    {
        var s when s.StartsWith("A") => "Residential",
        var s when s.StartsWith("B") => "Residential",
        var s when s.StartsWith("C") => "Vacant",
        var s when s.StartsWith("D") => "Agricultural",
        var s when s.StartsWith("E") => "Farm/Ranch",
        var s when s.StartsWith("F") && s == "F1" => "Commercial",
        var s when s.StartsWith("F") && s == "F2" => "Industrial",
        var s when s.StartsWith("G") => "Mineral",
        var s when s.StartsWith("J") => "Utility",
        var s when s.StartsWith("L") && s == "L1" => "Commercial",
        var s when s.StartsWith("L") && s == "L2" => "Industrial",
        var s when s.StartsWith("X") => "Institutional",
        _ => "Unknown"
    };
}
