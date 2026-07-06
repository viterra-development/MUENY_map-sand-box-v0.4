using Microsoft.Extensions.Logging;
using TripGenProcessor.Models;

namespace TripGenProcessor.Services;

/// <summary>
/// Core trip generation calculator.
/// Applies ITE rates to classified parcels to produce trip estimates.
/// Formula: daily_trips = ite_rate[ite_code] × units
/// </summary>
public class TripCalculator
{
    private readonly ILogger<TripCalculator> _logger;
    private readonly LandUseClassifier _classifier;
    private readonly double _directionalSplit;
    private readonly double _defaultDuPerParcel;
    private readonly double _defaultSqftFactor;

    public TripCalculator(
        ILogger<TripCalculator> logger,
        LandUseClassifier classifier,
        double directionalSplit = 0.50,
        double defaultDuPerParcel = 1.0,
        double defaultSqftFactor = 10000.0)
    {
        _logger = logger;
        _classifier = classifier;
        _directionalSplit = directionalSplit;
        _defaultDuPerParcel = defaultDuPerParcel;
        _defaultSqftFactor = defaultSqftFactor;
    }

    /// <summary>
    /// Calculate trip generation for a single parcel.
    /// Returns null if the parcel generates 0 trips.
    /// </summary>
    public TripGenerationResult? Calculate(CadParcel parcel)
    {
        // Step 1: Classify land use
        var (iteCode, classification, notes) = _classifier.Classify(parcel);

        if (iteCode == null)
        {
            return new TripGenerationResult
            {
                ParcelId = parcel.ParcelId,
                StateCd = parcel.StateCd,
                IteCode = null,
                Classification = classification,
                Notes = notes,
                DailyTrips = 0,
                AmPeakTrips = 0,
                PmPeakTrips = 0
            };
        }

        // Step 2: Look up ITE rate
        if (!IteRateLookup.Rates.TryGetValue(iteCode.Value, out var rate))
        {
            _logger.LogWarning("ITE code {Code} not found in rate table for parcel {Id}",
                iteCode, parcel.ParcelId);
            return null;
        }

        // Step 3: Determine units
        var units = _classifier.GetUnits(parcel, iteCode.Value, _defaultDuPerParcel, _defaultSqftFactor);

        // Step 4: Apply formula
        var dailyTrips = rate.DailyRate * units;
        var amPeakTrips = dailyTrips * rate.AmPeakKFactor;
        var pmPeakTrips = dailyTrips * rate.PmPeakKFactor;

        return new TripGenerationResult
        {
            ParcelId = parcel.ParcelId,
            StateCd = parcel.StateCd,
            IteCode = iteCode,
            IteLandUse = rate.LandUse,
            IteUnit = rate.Unit,
            Units = Math.Round(units, 2),
            DailyRate = rate.DailyRate,
            DailyTrips = Math.Round(dailyTrips, 1),
            AmPeakTrips = Math.Round(amPeakTrips, 1),
            PmPeakTrips = Math.Round(pmPeakTrips, 1),
            DirectionalSplit = _directionalSplit,
            Classification = classification,
            Notes = notes
        };
    }

    /// <summary>
    /// Calculate trip generation for all parcels.
    /// </summary>
    public List<TripGenerationResult> CalculateAll(List<CadParcel> parcels)
    {
        _logger.LogInformation("Calculating trips for {Count} parcels...", parcels.Count);

        var results = new List<TripGenerationResult>();
        int withTrips = 0;
        int zeroTrips = 0;

        foreach (var parcel in parcels)
        {
            var result = Calculate(parcel);
            if (result != null)
            {
                results.Add(result);
                if (result.DailyTrips > 0) withTrips++;
                else zeroTrips++;
            }
        }

        _classifier.LogStats();

        _logger.LogInformation(
            "Trip calculation complete: {WithTrips} parcels generating trips, {ZeroTrips} zero-trip parcels",
            withTrips, zeroTrips);

        var totalDaily = results.Sum(r => r.DailyTrips);
        var totalAm = results.Sum(r => r.AmPeakTrips);
        var totalPm = results.Sum(r => r.PmPeakTrips);

        _logger.LogInformation(
            "Total trips — Daily: {Daily:N0}, AM Peak: {AM:N0}, PM Peak: {PM:N0}",
            totalDaily, totalAm, totalPm);

        return results;
    }
}
