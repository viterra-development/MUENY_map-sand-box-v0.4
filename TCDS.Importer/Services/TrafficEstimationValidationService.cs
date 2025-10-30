using Microsoft.Extensions.Logging;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

/// <summary>
/// Validates traffic estimation quality using cross-validation and statistical metrics
/// </summary>
public class TrafficEstimationValidationService
{
    private readonly ILogger<TrafficEstimationValidationService> _logger;
    private readonly SpatialInterpolationEstimator _estimator;

    public TrafficEstimationValidationService(
        ILogger<TrafficEstimationValidationService> logger,
        SpatialInterpolationEstimator estimator)
    {
        _logger = logger;
        _estimator = estimator;
    }

    /// <summary>
    /// Performs spatial k-fold cross-validation on roads with existing AADT data
    /// </summary>
    public ValidationMetrics PerformCrossValidation(List<RoadSegment> roadsWithData, int folds = 5)
    {
        _logger.LogInformation("Performing {Folds}-fold cross-validation on {Count} roads",
            folds, roadsWithData.Count);

        var allErrors = new List<double>();
        var allSquaredErrors = new List<double>();
        var allPercentErrors = new List<double>();
        var allActual = new List<int>();
        var allPredicted = new List<int>();

        var hierarchyResults = new Dictionary<RoadHierarchy, List<(int Actual, int Predicted)>>();

        // Shuffle and split into folds
        var shuffled = roadsWithData.OrderBy(r => Guid.NewGuid()).ToList();
        var foldSize = shuffled.Count / folds;

        for (int fold = 0; fold < folds; fold++)
        {
            var startIdx = fold * foldSize;
            var endIdx = (fold == folds - 1) ? shuffled.Count : (fold + 1) * foldSize;

            var testSet = shuffled.Skip(startIdx).Take(endIdx - startIdx).ToList();
            var trainSet = shuffled.Take(startIdx).Concat(shuffled.Skip(endIdx)).ToList();

            _logger.LogDebug("Fold {Fold}: Training on {TrainCount} roads, testing on {TestCount} roads",
                fold + 1, trainSet.Count, testSet.Count);

            // Estimate AADT for test set using training set
            foreach (var testRoad in testSet)
            {
                var actualAadt = testRoad.ExistingAadt!.Value;

                // Temporarily hide the actual value
                var tempAadt = testRoad.ExistingAadt;
                testRoad.ExistingAadt = null;

                // Estimate
                var estimation = _estimator.EstimateAadt(testRoad, trainSet);

                // Restore actual value
                testRoad.ExistingAadt = tempAadt;

                if (estimation != null)
                {
                    var predictedAadt = estimation.EstimatedAadt;

                    // Calculate errors
                    var error = predictedAadt - actualAadt;
                    var squaredError = error * error;
                    var percentError = Math.Abs(error) * 100.0 / actualAadt;

                    allErrors.Add(error);
                    allSquaredErrors.Add(squaredError);
                    allPercentErrors.Add(percentError);
                    allActual.Add(actualAadt);
                    allPredicted.Add(predictedAadt);

                    // Track by hierarchy
                    if (!hierarchyResults.ContainsKey(testRoad.Hierarchy))
                    {
                        hierarchyResults[testRoad.Hierarchy] = new List<(int, int)>();
                    }
                    hierarchyResults[testRoad.Hierarchy].Add((actualAadt, predictedAadt));
                }
            }
        }

        // Calculate overall metrics
        var metrics = CalculateMetrics(allActual, allPredicted, allErrors, allSquaredErrors, allPercentErrors);

        // Calculate metrics by hierarchy
        foreach (var kvp in hierarchyResults)
        {
            var hierarchy = kvp.Key;
            var results = kvp.Value;

            var actual = results.Select(r => r.Actual).ToList();
            var predicted = results.Select(r => r.Predicted).ToList();
            var errors = results.Select(r => r.Predicted - r.Actual).ToList();

            var hierarchyMetrics = new HierarchyMetrics
            {
                Count = results.Count,
                MAE = errors.Average(e => Math.Abs(e)),
                AverageError = errors.Average(),
                MedianError = CalculateMedian(errors.Select(e => (double)e).ToList()),
                R2 = CalculateR2(actual, predicted)
            };

            metrics.ByHierarchy[hierarchy] = hierarchyMetrics;
        }

        LogValidationResults(metrics);

        return metrics;
    }

    /// <summary>
    /// Validates estimation results against known AADT values
    /// </summary>
    public ValidationMetrics ValidateEstimations(List<RoadSegment> segments)
    {
        _logger.LogInformation("Validating estimation results");

        var segmentsWithActualAndEstimated = segments
            .Where(s => s.ExistingAadt.HasValue && s.Estimation != null)
            .ToList();

        if (!segmentsWithActualAndEstimated.Any())
        {
            _logger.LogWarning("No segments found with both actual and estimated AADT values");
            return new ValidationMetrics();
        }

        var actual = segmentsWithActualAndEstimated.Select(s => s.ExistingAadt!.Value).ToList();
        var predicted = segmentsWithActualAndEstimated.Select(s => s.Estimation!.EstimatedAadt).ToList();

        var errors = actual.Zip(predicted, (a, p) => (double)(p - a)).ToList();
        var squaredErrors = errors.Select(e => e * e).ToList();
        var percentErrors = actual.Zip(predicted, (a, p) => Math.Abs(p - a) * 100.0 / a).ToList();

        var metrics = CalculateMetrics(actual, predicted, errors, squaredErrors, percentErrors);

        LogValidationResults(metrics);

        return metrics;
    }

    private ValidationMetrics CalculateMetrics(
        List<int> actual,
        List<int> predicted,
        List<double> errors,
        List<double> squaredErrors,
        List<double> percentErrors)
    {
        if (!actual.Any() || actual.Count != predicted.Count)
        {
            return new ValidationMetrics { SampleSize = 0 };
        }

        var mae = errors.Average(Math.Abs);
        var rmse = Math.Sqrt(squaredErrors.Average());
        var mape = percentErrors.Average();
        var r2 = CalculateR2(actual, predicted);

        return new ValidationMetrics
        {
            R2 = r2,
            MAE = mae,
            RMSE = rmse,
            MAPE = mape,
            SampleSize = actual.Count
        };
    }

    private double CalculateR2(List<int> actual, List<int> predicted)
    {
        if (actual.Count != predicted.Count || !actual.Any())
            return 0.0;

        var meanActual = actual.Average();
        var ssTotal = actual.Sum(a => Math.Pow(a - meanActual, 2));
        var ssResidual = actual.Zip(predicted, (a, p) => Math.Pow(a - p, 2)).Sum();

        if (ssTotal == 0)
            return 0.0;

        return 1.0 - (ssResidual / ssTotal);
    }

    private double CalculateMedian(List<double> values)
    {
        if (!values.Any())
            return 0.0;

        var sorted = values.OrderBy(v => v).ToList();
        var count = sorted.Count;

        if (count % 2 == 0)
        {
            return (sorted[count / 2 - 1] + sorted[count / 2]) / 2.0;
        }
        else
        {
            return sorted[count / 2];
        }
    }

    private void LogValidationResults(ValidationMetrics metrics)
    {
        _logger.LogInformation("Validation Results:");
        _logger.LogInformation("  📊 R² Score: {R2:F3} (target: > 0.70)", metrics.R2);
        _logger.LogInformation("  📏 MAE: {MAE:F1} vehicles/day (target: < 1,800)", metrics.MAE);
        _logger.LogInformation("  📐 RMSE: {RMSE:F1} vehicles/day", metrics.RMSE);
        _logger.LogInformation("  📈 MAPE: {MAPE:F1}%", metrics.MAPE);
        _logger.LogInformation("  📋 Sample Size: {SampleSize}", metrics.SampleSize);

        // Quality assessment
        var qualityLevel = metrics.R2 switch
        {
            >= 0.80 => "Excellent",
            >= 0.70 => "Good (meets Phase 1 target)",
            >= 0.60 => "Fair",
            _ => "Needs Improvement"
        };

        _logger.LogInformation("  ✓ Overall Quality: {Quality}", qualityLevel);

        if (metrics.ByHierarchy.Any())
        {
            _logger.LogInformation("  By Road Hierarchy:");
            foreach (var kvp in metrics.ByHierarchy.OrderBy(k => k.Key))
            {
                var hierarchy = kvp.Key;
                var hMetrics = kvp.Value;

                _logger.LogInformation("    - {Hierarchy}: R²={R2:F3}, MAE={MAE:F1}, n={Count}",
                    hierarchy, hMetrics.R2, hMetrics.MAE, hMetrics.Count);
            }
        }
    }

    /// <summary>
    /// Checks functional class compliance for estimated AADT values
    /// </summary>
    public Dictionary<RoadHierarchy, (int Min, int Max, int Violations)> CheckFunctionalClassCompliance(
        List<RoadSegment> segments)
    {
        _logger.LogInformation("Checking functional class compliance for estimated AADT values");

        // Expected AADT ranges by hierarchy (based on FHWA guidance)
        var expectedRanges = new Dictionary<RoadHierarchy, (int Min, int Max)>
        {
            { RoadHierarchy.Interstate, (20000, 200000) },
            { RoadHierarchy.USHighway, (10000, 50000) },
            { RoadHierarchy.StateHighway, (5000, 25000) },
            { RoadHierarchy.Arterial, (1000, 8000) },
            { RoadHierarchy.LocalRoad, (50, 2000) },
            { RoadHierarchy.Ramp, (5000, 30000) }
        };

        var complianceResults = new Dictionary<RoadHierarchy, (int Min, int Max, int Violations)>();

        foreach (var kvp in expectedRanges)
        {
            var hierarchy = kvp.Key;
            var range = kvp.Value;

            var estimatedForHierarchy = segments
                .Where(s => s.Hierarchy == hierarchy && s.Estimation != null)
                .ToList();

            if (!estimatedForHierarchy.Any())
                continue;

            var violations = estimatedForHierarchy
                .Count(s => s.Estimation!.EstimatedAadt < range.Min ||
                           s.Estimation!.EstimatedAadt > range.Max);

            complianceResults[hierarchy] = (range.Min, range.Max, violations);

            var complianceRate = (estimatedForHierarchy.Count - violations) * 100.0 / estimatedForHierarchy.Count;

            _logger.LogInformation("  {Hierarchy}: {Compliance:F1}% within range [{Min:N0}, {Max:N0}] ({Violations} violations)",
                hierarchy, complianceRate, range.Min, range.Max, violations);
        }

        return complianceResults;
    }
}
