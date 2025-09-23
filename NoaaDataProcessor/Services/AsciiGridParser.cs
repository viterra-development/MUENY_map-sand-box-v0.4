using Microsoft.Extensions.Logging;
using NoaaDataProcessor.Models;

namespace NoaaDataProcessor.Services;

public class AsciiGridParser
{
    private readonly ILogger<AsciiGridParser> _logger;

    public AsciiGridParser(ILogger<AsciiGridParser> logger)
    {
        _logger = logger;
    }

    public async Task<(GridHeader Header, List<GridCell> Cells)> ParseAsync(string filePath)
    {
        _logger.LogInformation("Parsing ASCII grid file: {FilePath}", filePath);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"ASCII grid file not found: {filePath}");
        }

        var lines = await File.ReadAllLinesAsync(filePath);
        var header = ParseHeader(lines);
        var cells = ParseGridData(lines, header);

        _logger.LogInformation("Parsed grid: {NCols}x{NRows} cells, {ValidCells} valid cells",
            header.NCols, header.NRows, cells.Count);

        return (header, cells);
    }

    private GridHeader ParseHeader(string[] lines)
    {
        var header = new GridHeader();

        foreach (var line in lines.Take(6))
        {
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var key = parts[0].ToLowerInvariant();
                var value = parts[1];

                switch (key)
                {
                    case "ncols":
                        header.NCols = int.Parse(value);
                        break;
                    case "nrows":
                        header.NRows = int.Parse(value);
                        break;
                    case "xllcorner":
                        header.XllCorner = double.Parse(value);
                        break;
                    case "yllcorner":
                        header.YllCorner = double.Parse(value);
                        break;
                    case "cellsize":
                        header.CellSize = double.Parse(value);
                        break;
                    case "nodata_value":
                        header.NoDataValue = double.Parse(value);
                        break;
                }
            }
        }

        return header;
    }

    private List<GridCell> ParseGridData(string[] lines, GridHeader header)
    {
        var cells = new List<GridCell>();
        var dataLines = lines.Skip(6).ToArray();

        for (int row = 0; row < header.NRows && row < dataLines.Length; row++)
        {
            var values = dataLines[row].Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);

            for (int col = 0; col < header.NCols && col < values.Length; col++)
            {
                if (double.TryParse(values[col], out var value))
                {
                    var cell = new GridCell
                    {
                        Row = row,
                        Col = col,
                        Value = value,
                        Longitude = header.XllCorner + (col * header.CellSize) + (header.CellSize / 2),
                        Latitude = header.YurCorner - (row * header.CellSize) - (header.CellSize / 2)
                    };

                    // Only include non-no-data values
                    if (!cell.IsNoData(header.NoDataValue))
                    {
                        cells.Add(cell);
                    }
                }
            }
        }

        return cells;
    }
}