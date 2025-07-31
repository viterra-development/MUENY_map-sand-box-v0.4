// More robust data extraction approach
private async Task<List<AadtRecord>> ExtractAadtDataRobustAsync(IFrame frame)
{
    var aadtData = new List<AadtRecord>();

    try
    {
        // Strategy 1: Look for specific table structures
        var tables = await frame.QuerySelectorAllAsync("table");
        
        foreach (var table in tables)
        {
            // Check if this table contains AADT data by looking for year patterns
            var tableText = await table.TextContentAsync();
            if (!ContainsYearPattern(tableText) || !tableText.Contains("AADT", StringComparison.OrdinalIgnoreCase))
                continue;

            // Strategy 2: Flexible column detection
            var rows = await table.QuerySelectorAllAsync("tr");
            var headerRow = await FindHeaderRow(rows);
            if (headerRow == null) continue;

            var columnMap = await MapColumns(headerRow);
            
            // Strategy 3: Flexible data extraction
            foreach (var row in rows.Skip(1)) // Skip header
            {
                var record = await ExtractAadtRecord(row, columnMap);
                if (record != null) aadtData.Add(record);
            }
            
            if (aadtData.Any()) break; // Found AADT table, stop looking
        }

        // Strategy 4: Fallback to current text-based approach
        if (!aadtData.Any())
        {
            _logger.LogWarning("Structured AADT extraction failed, falling back to text parsing");
            aadtData = await ExtractAadtDataFallback(frame);
        }
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Robust AADT extraction failed");
    }

    return aadtData;
}

private bool ContainsYearPattern(string text)
{
    // Look for 4-digit years in recent range
    var yearRegex = new Regex(@"\b(20[12][0-9])\b");
    return yearRegex.IsMatch(text);
}

private async Task<IElementHandle?> FindHeaderRow(IElementHandle[] rows)
{
    foreach (var row in rows)
    {
        var text = await row.TextContentAsync();
        if (text.Contains("Year", StringComparison.OrdinalIgnoreCase) && 
            text.Contains("AADT", StringComparison.OrdinalIgnoreCase))
        {
            return row;
        }
    }
    return null;
}

private async Task<Dictionary<string, int>> MapColumns(IElementHandle headerRow)
{
    var columnMap = new Dictionary<string, int>();
    var headers = await headerRow.QuerySelectorAllAsync("th, td");
    
    for (int i = 0; i < headers.Length; i++)
    {
        var headerText = await headers[i].TextContentAsync();
        var normalizedHeader = headerText?.Trim().ToLowerInvariant() ?? "";
        
        // Map various possible column names
        if (normalizedHeader.Contains("year")) columnMap["year"] = i;
        else if (normalizedHeader.Contains("aadt")) columnMap["aadt"] = i;
        else if (normalizedHeader.Contains("dhv")) columnMap["dhv"] = i;
        else if (normalizedHeader.Contains("k%") || normalizedHeader.Contains("k ")) columnMap["k"] = i;
        else if (normalizedHeader.Contains("d%") || normalizedHeader.Contains("d ")) columnMap["d"] = i;
    }
    
    return columnMap;
}