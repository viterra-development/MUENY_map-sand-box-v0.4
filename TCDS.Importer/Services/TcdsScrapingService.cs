using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

public class TcdsScrapingService : IAsyncDisposable
{
    private readonly ILogger<TcdsScrapingService> _logger;
    private readonly TcdsConfiguration _config;
    private readonly Random _random;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    public TcdsScrapingService(ILogger<TcdsScrapingService> logger, IOptions<TcdsConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
        _random = new Random();
    }

    private async Task FuzzyWaitAsync(CancellationToken cancellationToken = default)
    {
        if (!_config.UseFuzzyWaits)
        {
            await Task.Delay(_config.WaitDelay, cancellationToken);
            return;
        }

        var waitTime = _random.Next(_config.MinWaitDelay, _config.MaxWaitDelay);
        _logger.LogDebug("Fuzzy wait: {WaitTime}ms", waitTime);
        await Task.Delay(waitTime, cancellationToken);
    }

    private async Task FuzzyWaitAsync(int baseDelay, CancellationToken cancellationToken = default)
    {
        if (!_config.UseFuzzyWaits)
        {
            await Task.Delay(baseDelay, cancellationToken);
            return;
        }

        var variance = (int)(baseDelay * 0.3); // 30% variance
        var minWait = Math.Max(1000, baseDelay - variance);
        var maxWait = baseDelay + variance;
        var waitTime = _random.Next(minWait, maxWait);
        _logger.LogDebug("Fuzzy wait (base {BaseDelay}ms): {WaitTime}ms", baseDelay, waitTime);
        await Task.Delay(waitTime, cancellationToken);
    }

    public async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing Playwright browser with system Chrome");
            
            _playwright = await Playwright.CreateAsync();
            
            var browserType = _playwright.Chromium;
            
            var launchOptions = new BrowserTypeLaunchOptions
            {
                Headless = _config.Headless,
                Channel = "chrome",
                Args = new[]
                {
                    "--no-sandbox",
                    "--disable-blink-features=AutomationControlled",
                    "--disable-web-security",
                    "--disable-features=VizDisplayCompositor",
                    "--disable-dev-shm-usage",
                    "--disable-gpu",
                    "--no-first-run",
                    "--no-default-browser-check",
                    "--disable-default-apps",
                    "--disable-extensions",
                    "--disable-plugins",
                    "--disable-background-timer-throttling",
                    "--disable-backgrounding-occluded-windows",
                    "--disable-renderer-backgrounding",
                    "--disable-field-trial-config",
                    "--disable-ipc-flooding-protection",
                    "--enable-features=NetworkService,NetworkServiceLogging",
                    "--force-color-profile=srgb",
                    "--metrics-recording-only",
                    "--disable-background-networking",
                    "--disable-sync",
                    "--disable-translate",
                    "--hide-scrollbars",
                    "--mute-audio",
                    "--no-zygote",
                    "--disable-logging",
                    "--disable-notifications",
                    "--disable-permissions-api",
                    "--disable-background-media-suspend",
                    "--disable-component-extensions-with-background-pages",
                    "--disable-background-mode",
                    "--disable-features=TranslateUI",
                    "--disable-ignore-certificate-errors",
                    "--disable-single-click-autofill",
                    "--disable-autofill-keyboard-accessory-view",
                    "--disable-domain-reliability"
                }
            };

            _browser = await browserType.LaunchAsync(launchOptions);
            
            var contextOptions = new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/138.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1920, Height = 1080 },
                DeviceScaleFactor = 1,
                IsMobile = false,
                HasTouch = false,
                Locale = "en-US",
                TimezoneId = "America/New_York",
                Permissions = new[] { "geolocation" },
                ExtraHTTPHeaders = new Dictionary<string, string>
                {
                    ["Accept"] = "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8,application/signed-exchange;v=b3;q=0.7",
                    ["Accept-Language"] = "en-US,en;q=0.9",
                    ["Accept-Encoding"] = "gzip, deflate, br",
                    ["Cache-Control"] = "no-cache",
                    ["Pragma"] = "no-cache",
                    ["Sec-Ch-Ua"] = "\"Not_A Brand\";v=\"8\", \"Chromium\";v=\"120\", \"Google Chrome\";v=\"120\"",
                    ["Sec-Ch-Ua-Mobile"] = "?0",
                    ["Sec-Ch-Ua-Platform"] = "\"macOS\"",
                    ["Sec-Fetch-Dest"] = "document",
                    ["Sec-Fetch-Mode"] = "navigate",
                    ["Sec-Fetch-Site"] = "none",
                    ["Sec-Fetch-User"] = "?1",
                    ["Upgrade-Insecure-Requests"] = "1"
                }
            };

            _context = await _browser.NewContextAsync(contextOptions);
            _page = await _context.NewPageAsync();

            _page.Console += (sender, e) =>
            {
                _logger.LogDebug("Console: {Message}", e.Text);
            };

            _logger.LogInformation("Playwright browser initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Playwright browser");
            throw;
        }
    }

    public async Task<PageData> NavigateToPageAsync(string url, CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            await InitializeAsync();
        }

        _logger.LogInformation("Navigating to page: {Url}", url);
        
        try
        {
            var response = await _page!.GotoAsync(url, new PageGotoOptions
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _config.NavigationTimeout
            });

            if (response != null && response.Status == 429)
            {
                _logger.LogInformation("Received 429 response, waiting for redirect process...");
                
                await FuzzyWaitAsync(8000, cancellationToken);
                
                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded, new PageWaitForLoadStateOptions
                {
                    Timeout = _config.NavigationTimeout
                });
            }

            await FuzzyWaitAsync(cancellationToken);

            var title = await _page.TitleAsync();
            var content = await _page.ContentAsync();

            return new PageData
            {
                Url = url,
                Title = title,
                Content = content,
                Metadata = new Dictionary<string, string>(),
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to page: {Url}", url);
            throw;
        }
    }

    public async Task<PageData> SearchParkerCountyAsync(CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        try
        {
            _logger.LogInformation("Searching for Parker County in TCDS");

            // Wait for the left iframe to load
            await _page.WaitForSelectorAsync("iframe[name='LeftFrame']", new PageWaitForSelectorOptions
            {
                Timeout = _config.NavigationTimeout
            });

            // Get the left iframe containing the search form
            var leftFrame = _page.Frame("LeftFrame");
            if (leftFrame == null)
            {
                throw new InvalidOperationException("Could not find LeftFrame iframe");
            }

            _logger.LogInformation("Found search frame, waiting for form elements to load");

            // Wait a bit for the iframe content to load
            await FuzzyWaitAsync(4000, cancellationToken);

            // Look for the typeahead input field for county
            string[] possibleSelectors = {
                "#ddlCounty",
                "input[name='County']",
                "input.typeahead",
                "input[placeholder*='County']"
            };

            IElementHandle? countyInput = null;
            string usedSelector = "";
            
            foreach (var selector in possibleSelectors)
            {
                try
                {
                    _logger.LogDebug("Trying county input selector: {Selector}", selector);
                    countyInput = await leftFrame.QuerySelectorAsync(selector);
                    if (countyInput != null)
                    {
                        usedSelector = selector;
                        _logger.LogInformation("Found county input field with selector: {Selector}", selector);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Selector {Selector} failed: {Error}", selector, ex.Message);
                }
            }

            if (countyInput == null)
            {
                // Let's see what's actually in the iframe
                var frameContent = await leftFrame.ContentAsync();
                _logger.LogWarning("Could not find county input field. Frame content length: {Length}", frameContent.Length);
                _logger.LogDebug("Frame content: {Content}", frameContent.Length > 1000 ? frameContent.Substring(0, 1000) + "..." : frameContent);
                throw new InvalidOperationException("Could not find county input field in search frame");
            }

            // Type Parker into the typeahead field
            _logger.LogInformation("Typing 'Parker' into county field");
            await leftFrame.ClickAsync(usedSelector); // Focus the input
            await leftFrame.FillAsync(usedSelector, "Parker");
            
            // Wait for typeahead suggestions to appear
            await FuzzyWaitAsync(1500, cancellationToken);
            
            // Look for and click the Parker suggestion
            _logger.LogInformation("Looking for Parker suggestion in typeahead dropdown");
            try
            {
                await leftFrame.WaitForSelectorAsync(".tt-suggestion, .tt-suggestion p", new FrameWaitForSelectorOptions
                {
                    Timeout = 5000
                });
                
                // Click the suggestion
                await leftFrame.ClickAsync(".tt-suggestion");
                _logger.LogInformation("Clicked Parker suggestion");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Could not find Parker suggestion, but field may be filled");
            }

            // Wait a moment for any dynamic updates
            await FuzzyWaitAsync(1500, cancellationToken);

            // Find and click the Search button
            _logger.LogInformation("Looking for Search button");
            string[] searchButtonSelectors = {
                "#btnSubmit",
                "input[value='Search']",
                "input[type='submit']",
                "button[type='submit']",
                "input[name='Search']",
                "#Search"
            };

            bool searchClicked = false;
            foreach (var buttonSelector in searchButtonSelectors)
            {
                try
                {
                    var searchButton = await leftFrame.QuerySelectorAsync(buttonSelector);
                    if (searchButton != null)
                    {
                        _logger.LogInformation("Clicking Search button with selector: {Selector}", buttonSelector);
                        await leftFrame.ClickAsync(buttonSelector);
                        searchClicked = true;
                        break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogDebug("Search button selector {Selector} failed: {Error}", buttonSelector, ex.Message);
                }
            }

            if (!searchClicked)
            {
                throw new InvalidOperationException("Could not find or click search button");
            }

            // Wait for search results to load - this might take a while
            _logger.LogInformation("Waiting for search results to load...");
            await FuzzyWaitAsync(7000, cancellationToken);

            // Try to wait for some indication that results have loaded
            try
            {
                await leftFrame.WaitForSelectorAsync(".station-data, table, .results", new FrameWaitForSelectorOptions
                {
                    Timeout = 10000 // 10 second timeout for results
                });
                _logger.LogInformation("Search results appear to have loaded");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Results selector not found after 10 seconds, proceeding anyway - results may still be visible");
            }

            // Get updated page data
            var title = await _page.TitleAsync();
            var content = await _page.ContentAsync();

            return new PageData
            {
                Url = _page.Url,
                Title = title,
                Content = content,
                Metadata = new Dictionary<string, string>
                {
                    ["SearchType"] = "ParkerCounty",
                    ["SearchTimestamp"] = DateTime.UtcNow.ToString("O")
                },
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search Parker County");
            throw;
        }
    }

    public async Task<TrafficCountData> ExtractTrafficDataAsync(CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        try
        {
            _logger.LogInformation("Extracting traffic count data from current page");

            var leftFrame = _page.Frame("LeftFrame");
            if (leftFrame == null)
            {
                throw new InvalidOperationException("Could not find LeftFrame iframe");
            }

            // Wait for AJAX data to load - look for actual data instead of "Loading Data..." messages
            _logger.LogInformation("Waiting for AJAX data to load...");
            await WaitForAjaxDataToLoad(leftFrame, cancellationToken);

            var trafficData = new TrafficCountData();

            // Extract location information
            trafficData.LocationInfo = await ExtractLocationInfoAsync(leftFrame, cancellationToken);
            trafficData.LocationId = trafficData.LocationInfo.LocationId ?? trafficData.LocationInfo.LocatedOn; // Use Location ID if available, otherwise fall back to Located On

            // Extract AADT data
            trafficData.AadtData = await ExtractAadtDataAsync(leftFrame);

            // Extract Volume Count data
            trafficData.VolumeCountData = await ExtractVolumeCountDataAsync(leftFrame);

            // Extract Volume Trend data
            trafficData.VolumeTrendData = await ExtractVolumeTrendDataAsync(leftFrame);

            _logger.LogInformation("Successfully extracted traffic data with {AadtCount} AADT records, {VolumeCount} volume records, {TrendCount} trend records",
                trafficData.AadtData.Count, trafficData.VolumeCountData.Count, trafficData.VolumeTrendData.Count);

            return trafficData;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to extract traffic data");
            throw;
        }
    }

    private async Task WaitForAjaxDataToLoad(IFrame frame, CancellationToken cancellationToken)
    {
        var maxWaitTime = _config.AjaxWaitTimeout; // Use configurable timeout
        var baseCheckInterval = 1000; // Base check interval
        var elapsed = 0;

        while (elapsed < maxWaitTime)
        {
            try 
            {
                // Check if "Loading Data..." messages are gone and actual data is present
                var loadingElements = await frame.QuerySelectorAllAsync("img[src*='icnBusy.gif'], td:has-text('Loading Data...')");
                
                if (loadingElements.Count == 0)
                {
                    // No loading indicators found, check if we have actual data
                    var hasAadtData = await frame.QuerySelectorAsync("#TCDS_TDETAIL_AADT_DIV table tr:nth-child(3)") != null;
                    var hasVolumeData = await frame.QuerySelectorAsync("#TCDS_TDETAIL_VOL_DIV table tr:nth-child(3)") != null;
                    
                    if (hasAadtData || hasVolumeData)
                    {
                        _logger.LogInformation("AJAX data appears to be loaded after {ElapsedSeconds} seconds", elapsed / 1000);
                        await FuzzyWaitAsync(3000, cancellationToken); // Give it a bit more time to fully settle
                        return;
                    }
                }

                var checkInterval = _config.UseFuzzyWaits ? 
                    _random.Next((int)(baseCheckInterval * 0.8), (int)(baseCheckInterval * 1.2)) : 
                    baseCheckInterval;
                await Task.Delay(checkInterval, cancellationToken);
                elapsed += checkInterval;
            }
            catch (Exception ex)
            {
                _logger.LogDebug("Error checking for AJAX data load: {Error}", ex.Message);
                var checkInterval = _config.UseFuzzyWaits ? 
                    _random.Next((int)(baseCheckInterval * 0.8), (int)(baseCheckInterval * 1.2)) : 
                    baseCheckInterval;
                await Task.Delay(checkInterval, cancellationToken);
                elapsed += checkInterval;
            }
        }

        _logger.LogWarning("AJAX data may not have fully loaded after {MaxWaitSeconds} seconds, proceeding anyway", maxWaitTime / 1000);
    }

    private async Task<LocationInfo> ExtractLocationInfoAsync(IFrame frame, CancellationToken cancellationToken = default)
    {
        var locationInfo = new LocationInfo();

        try
        {
            _logger.LogInformation("Extracting location information from page");
            
            // Look for the main data table that contains location information
            var rows = await frame.QuerySelectorAllAsync("table.frmDtl tr");
            _logger.LogDebug("Found {RowCount} rows in frmDtl tables", rows.Count);
            
            foreach (var row in rows)
            {
                var cells = await row.QuerySelectorAllAsync("th, td");
                
                // Process pairs of th/td elements in each row
                for (int i = 0; i < cells.Count - 1; i += 2)
                {
                    var headerCell = cells[i];
                    var dataCell = cells[i + 1];
                    
                    var headerText = await headerCell.TextContentAsync();
                    var dataText = await dataCell.TextContentAsync();
                    
                    if (string.IsNullOrWhiteSpace(headerText)) continue;
                    
                    var header = headerText.Trim();
                    var data = dataText?.Trim() ?? "";
                    
                    _logger.LogDebug("Found location field: {Header} = {Data}", header, data);
                    
                    switch (header)
                    {
                        case "Location ID":
                        case "Location ID ": // Handle the trailing space
                            locationInfo.LocationId = data;
                            break;
                        case "Type":
                            locationInfo.Type = data;
                            break;
                        case "SF Group":
                            locationInfo.SfGroup = data;
                            break;
                        case "AF Group":
                            locationInfo.AfGroup = data;
                            break;
                        case "GF Group":
                            locationInfo.GfGroup = data;
                            break;
                        case "WIM Group":
                            locationInfo.WimGroup = data;
                            break;
                        case "QC Group":
                            locationInfo.QcGroup = data;
                            break;
                        case "Fnct'l Class":
                            locationInfo.FnctClass = data;
                            break;
                        case "Located On":
                            locationInfo.LocatedOn = data;
                            break;
                        case "Loc On Alias":
                            locationInfo.LocOnAlias = data;
                            break;
                        case "MPO ID":
                            locationInfo.MpoId = data;
                            break;
                        case "HPMS ID":
                            locationInfo.HpmsId = data;
                            break;
                        case "Route Type":
                            locationInfo.RouteType = data;
                            break;
                        case "Route":
                            locationInfo.Route = data;
                            break;
                        case "Active":
                            locationInfo.Active = data;
                            break;
                        case "Category":
                            locationInfo.Category = data;
                            break;
                    }
                }
            }
            
            // If we didn't extract much data, try a more specific approach
            if (string.IsNullOrEmpty(locationInfo.LocatedOn) && string.IsNullOrEmpty(locationInfo.Type))
            {
                _logger.LogWarning("Standard extraction didn't find data, trying alternative selectors...");
                
                // Try to find Location ID specifically
                var locationIdElement = await frame.QuerySelectorAsync("th:has-text('Location ID') + td");
                if (locationIdElement != null)
                {
                    locationInfo.LocationId = (await locationIdElement.TextContentAsync())?.Trim() ?? "";
                    _logger.LogDebug("Found Location ID via alternative method: {LocationId}", locationInfo.LocationId);
                }
                
                // Try to find Type specifically  
                var typeElement = await frame.QuerySelectorAsync("th:has-text('Type') + td");
                if (typeElement != null)
                {
                    locationInfo.Type = (await typeElement.TextContentAsync())?.Trim() ?? "";
                    _logger.LogDebug("Found Type via alternative method: {Type}", locationInfo.Type);
                }
                
                // Try to find Located On specifically
                var locatedOnElement = await frame.QuerySelectorAsync("th:has-text('Located On') + td");
                if (locatedOnElement != null)
                {
                    locationInfo.LocatedOn = (await locatedOnElement.TextContentAsync())?.Trim() ?? "";
                    _logger.LogDebug("Found Located On via alternative method: {LocatedOn}", locationInfo.LocatedOn);
                }
            }
            
            // Try to expand the "More Detail" section to get Latitude and Longitude
            await ExpandMoreDetailAndExtractCoordinatesAsync(frame, locationInfo, cancellationToken);
            
            _logger.LogInformation("Extracted location info - Location ID: {LocationId}, Located On: {LocatedOn}, Type: {Type}, Lat: {Latitude}, Lng: {Longitude}", 
                locationInfo.LocationId, locationInfo.LocatedOn, locationInfo.Type, locationInfo.Latitude, locationInfo.Longitude);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract some location information");
        }

        return locationInfo;
    }

    private async Task ExpandMoreDetailAndExtractCoordinatesAsync(IFrame frame, LocationInfo locationInfo, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Attempting to expand More Detail section for coordinates");
            
            // Look for the "More Detail" expand button (e_detail)
            var expandButton = await frame.QuerySelectorAsync("#e_detail");
            if (expandButton == null)
            {
                _logger.LogWarning("More Detail expand button not found");
                return;
            }

            // Check if the detail section is already expanded
            var detailTable = await frame.QuerySelectorAsync("#detail");
            if (detailTable != null)
            {
                var displayStyle = await detailTable.GetAttributeAsync("style");
                if (displayStyle != null && displayStyle.Contains("display:none") || displayStyle != null && displayStyle.Contains("display: none"))
                {
                    _logger.LogInformation("Detail section is collapsed, clicking to expand");
                    await expandButton.ClickAsync();
                    
                    // Wait for the section to expand
                    await FuzzyWaitAsync(1500, cancellationToken);
                }
                else
                {
                    _logger.LogInformation("Detail section appears to already be expanded");
                }
            }

            // Now extract Latitude and Longitude from the expanded detail table
            var detailRows = await frame.QuerySelectorAllAsync("#detail tr");
            _logger.LogDebug("Found {RowCount} rows in detail table", detailRows.Count);

            foreach (var row in detailRows)
            {
                var cells = await row.QuerySelectorAllAsync("th, td");
                
                // Process pairs of th/td elements in each row
                for (int i = 0; i < cells.Count - 1; i += 2)
                {
                    var headerCell = cells[i];
                    var dataCell = cells[i + 1];
                    
                    var headerText = await headerCell.TextContentAsync();
                    var dataText = await dataCell.TextContentAsync();
                    
                    if (string.IsNullOrWhiteSpace(headerText)) continue;
                    
                    var header = headerText.Trim();
                    var data = dataText?.Trim() ?? "";
                    
                    switch (header)
                    {
                        case "Latitude":
                            if (decimal.TryParse(data, out decimal latitude))
                            {
                                locationInfo.Latitude = latitude;
                                _logger.LogDebug("Extracted Latitude: {Latitude}", latitude);
                            }
                            break;
                        case "Longitude":
                            if (decimal.TryParse(data, out decimal longitude))
                            {
                                locationInfo.Longitude = longitude;
                                _logger.LogDebug("Extracted Longitude: {Longitude}", longitude);
                            }
                            break;
                    }
                }
            }

            if (locationInfo.Latitude.HasValue && locationInfo.Longitude.HasValue)
            {
                _logger.LogInformation("Successfully extracted coordinates: {Latitude}, {Longitude}", locationInfo.Latitude, locationInfo.Longitude);
            }
            else
            {
                _logger.LogWarning("Could not extract latitude and/or longitude from detail section");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to expand More Detail section or extract coordinates");
        }
    }

    private async Task<List<AadtRecord>> ExtractAadtDataAsync(IFrame frame)
    {
        var aadtData = new List<AadtRecord>();

        try
        {
            // Look specifically in the AADT DIV that gets populated by AJAX
            var aadtDiv = await frame.QuerySelectorAsync("#TCDS_TDETAIL_AADT_DIV");
            if (aadtDiv == null)
            {
                _logger.LogWarning("AADT DIV not found");
                return aadtData;
            }

            var rows = await aadtDiv.QuerySelectorAllAsync("table tr");
            _logger.LogDebug("Found {RowCount} rows in AADT table", rows.Count);

            foreach (var row in rows)
            {
                var cells = await row.QuerySelectorAllAsync("td");
                if (cells.Count >= 3)
                {
                    var yearText = await cells[1].TextContentAsync(); // Year is in column 2 (index 1)
                    var aadtText = await cells[2].TextContentAsync(); // AADT is in column 3 (index 2)
                    
                    if (int.TryParse(yearText?.Trim(), out int year) && year > 1900)
                    {
                        var record = new AadtRecord { Year = year };
                        
                        if (int.TryParse(aadtText?.Trim().Replace(",", ""), out int aadt))
                            record.Aadt = aadt;
                        
                        if (cells.Count > 3)
                        {
                            var dhvText = await cells[3].TextContentAsync();
                            if (int.TryParse(dhvText?.Trim().Replace(",", ""), out int dhv))
                                record.Dhv30 = dhv;
                        }

                        if (cells.Count > 4)
                        {
                            var kText = await cells[4].TextContentAsync();
                            if (decimal.TryParse(kText?.Trim(), out decimal k))
                                record.KPercent = k;
                        }

                        if (cells.Count > 5)
                        {
                            var dText = await cells[5].TextContentAsync();
                            if (decimal.TryParse(dText?.Trim(), out decimal d))
                                record.DPercent = d;
                        }

                        aadtData.Add(record);
                        _logger.LogDebug("Extracted AADT record: Year={Year}, AADT={Aadt}", year, aadt);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract AADT data");
        }

        return aadtData;
    }

    private async Task<List<VolumeCountRecord>> ExtractVolumeCountDataAsync(IFrame frame)
    {
        var volumeData = new List<VolumeCountRecord>();

        try
        {
            // Look specifically in the Volume Count DIV that gets populated by AJAX
            var volumeDiv = await frame.QuerySelectorAsync("#TCDS_TDETAIL_VOL_DIV");
            if (volumeDiv == null)
            {
                _logger.LogWarning("Volume Count DIV not found");
                return volumeData;
            }

            var rows = await volumeDiv.QuerySelectorAllAsync("table tr");
            _logger.LogDebug("Found {RowCount} rows in Volume Count table", rows.Count);

            foreach (var row in rows)
            {
                var cells = await row.QuerySelectorAllAsync("td");
                if (cells.Count >= 4)
                {
                    var dateCell = await cells[1].TextContentAsync(); // Date is in column 2 (index 1)
                    var intervalCell = await cells[2].TextContentAsync(); // Interval is in column 3 (index 2)
                    var totalCell = await cells[3].TextContentAsync(); // Total is in column 4 (index 3)

                    if (DateTime.TryParse(dateCell?.Trim(), out DateTime date) &&
                        int.TryParse(intervalCell?.Trim(), out int interval) &&
                        int.TryParse(totalCell?.Trim().Replace(",", ""), out int total))
                    {
                        volumeData.Add(new VolumeCountRecord
                        {
                            Date = date,
                            Interval = interval,
                            Total = total
                        });
                        _logger.LogDebug("Extracted Volume Count record: Date={Date}, Total={Total}", date.ToShortDateString(), total);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract volume count data");
        }

        return volumeData;
    }

    private async Task<List<VolumeTrendRecord>> ExtractVolumeTrendDataAsync(IFrame frame)
    {
        var trendData = new List<VolumeTrendRecord>();

        try
        {
            // Look specifically in the Volume Trend DIV that gets populated by AJAX
            var trendDiv = await frame.QuerySelectorAsync("#TCDS_TDETAIL_VOLTREND_DIV");
            if (trendDiv == null)
            {
                _logger.LogWarning("Volume Trend DIV not found");
                return trendData;
            }

            var rows = await trendDiv.QuerySelectorAllAsync("table tr");
            _logger.LogDebug("Found {RowCount} rows in Volume Trend table", rows.Count);

            foreach (var row in rows)
            {
                var cells = await row.QuerySelectorAllAsync("td");
                if (cells.Count >= 2)
                {
                    var yearCell = await cells[0].TextContentAsync(); // Year is in column 1 (index 0)
                    var growthCell = await cells[1].TextContentAsync(); // Growth is in column 2 (index 1)

                    if (int.TryParse(yearCell?.Trim(), out int year) && year > 1900)
                    {
                        var record = new VolumeTrendRecord { Year = year };
                        
                        var growthText = growthCell?.Trim().Replace("%", "");
                        if (decimal.TryParse(growthText, out decimal growth))
                        {
                            record.AnnualGrowth = growth;
                        }

                        trendData.Add(record);
                        _logger.LogDebug("Extracted Volume Trend record: Year={Year}, Growth={Growth}%", year, growth);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract volume trend data");
        }

        return trendData;
    }

    public async Task<PageData> GotoRecordAsync(int recordNumber, CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        try
        {
            _logger.LogInformation("Going to record number {RecordNumber} using Goto Record functionality", recordNumber);

            var leftFrame = _page.Frame("LeftFrame");
            if (leftFrame == null)
            {
                throw new InvalidOperationException("Could not find LeftFrame iframe");
            }

            // Find the "jump" input field for record number
            var jumpInput = await leftFrame.QuerySelectorAsync("input[name='jump']");
            if (jumpInput == null)
            {
                throw new InvalidOperationException("Could not find 'jump' input field for Goto Record");
            }

            // Clear the input and enter the record number
            _logger.LogInformation("Entering record number {RecordNumber} in Goto Record field", recordNumber);
            await leftFrame.FillAsync("input[name='jump']", recordNumber.ToString());

            // Find and click the "go" button
            var goButton = await leftFrame.QuerySelectorAsync("input[name='jump_btn']");
            if (goButton == null)
            {
                throw new InvalidOperationException("Could not find 'go' button for Goto Record");
            }

            _logger.LogInformation("Clicking 'go' button to navigate to record {RecordNumber}", recordNumber);
            await goButton.ClickAsync();

            // Wait for the page to navigate to the specified record
            _logger.LogInformation("Waiting for record {RecordNumber} to load...", recordNumber);
            await FuzzyWaitAsync(7000, cancellationToken);

            // Wait for AJAX data to load on the new page
            await WaitForAjaxDataToLoad(leftFrame, cancellationToken);

            // Get updated page data
            var title = await _page.TitleAsync();
            var content = await _page.ContentAsync();

            return new PageData
            {
                Url = _page.Url,
                Title = title,
                Content = content,
                Metadata = new Dictionary<string, string>
                {
                    ["NavigationType"] = "GotoRecord",
                    ["RecordNumber"] = recordNumber.ToString(),
                    ["NavigationTimestamp"] = DateTime.UtcNow.ToString("O")
                },
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to navigate to record {RecordNumber} using Goto Record", recordNumber);
            throw;
        }
    }

    public async Task<PageData> ClickNextRecordAsync(CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        try
        {
            _logger.LogInformation("Clicking Next button to navigate to next record");

            var leftFrame = _page.Frame("LeftFrame");
            if (leftFrame == null)
            {
                throw new InvalidOperationException("Could not find LeftFrame iframe");
            }

            // First check if there's a disabled Next button (indicates we're at the end)
            var disabledNextImg = await leftFrame.QuerySelectorAsync("img[src*='Next-disabled.gif']");
            if (disabledNextImg != null)
            {
                throw new InvalidOperationException("Next button is disabled - reached end of records");
            }
            
            // Look for the Next button by finding the img and its parent td
            var nextButtonImg = await leftFrame.QuerySelectorAsync("img[src*='Next.gif']");
            if (nextButtonImg == null)
            {
                throw new InvalidOperationException("Could not find Next button image");
            }
            
            // Find the parent td element that contains the onclick event
            var allTds = await leftFrame.QuerySelectorAllAsync("td[onclick*='tdetail.asp?offset=']");
            IElementHandle? nextButtonTd = null;
            
            foreach (var td in allTds)
            {
                // Check if this td contains the Next.gif image
                var imgInTd = await td.QuerySelectorAsync("img[src*='Next.gif']");
                if (imgInTd != null)
                {
                    nextButtonTd = td;
                    break;
                }
            }
            
            if (nextButtonTd == null)
            {
                throw new InvalidOperationException("Could not find clickable Next button");
            }

            _logger.LogInformation("Found Next button, clicking...");
            await nextButtonTd.ClickAsync();

            // Wait for the page to navigate to the next record
            _logger.LogInformation("Waiting for next record to load...");
            await FuzzyWaitAsync(7000, cancellationToken); // Wait for navigation

            // Wait for AJAX data to load again on the new page
            await WaitForAjaxDataToLoad(leftFrame, cancellationToken);

            // Get updated page data
            var title = await _page.TitleAsync();
            var content = await _page.ContentAsync();

            return new PageData
            {
                Url = _page.Url,
                Title = title,
                Content = content,
                Metadata = new Dictionary<string, string>
                {
                    ["NavigationType"] = "NextRecord",
                    ["NavigationTimestamp"] = DateTime.UtcNow.ToString("O")
                },
                Timestamp = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to click Next button and navigate to next record");
            throw;
        }
    }

    public async Task<string> TakeScreenshotAsync(string? selector = null, CancellationToken cancellationToken = default)
    {
        if (_page == null)
        {
            throw new InvalidOperationException("Browser not initialized");
        }

        try
        {
            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var filename = $"tcds_screenshot_{timestamp}.png";
            var screenshotPath = Path.Combine(_config.ScreenshotDirectory, filename);

            Directory.CreateDirectory(_config.ScreenshotDirectory);

            if (string.IsNullOrEmpty(selector))
            {
                await _page.ScreenshotAsync(new PageScreenshotOptions
                {
                    Path = screenshotPath,
                    FullPage = true
                });
            }
            else
            {
                var element = await _page.QuerySelectorAsync(selector);
                if (element != null)
                {
                    await element.ScreenshotAsync(new ElementHandleScreenshotOptions
                    {
                        Path = screenshotPath
                    });
                }
                else
                {
                    _logger.LogWarning("Element with selector '{Selector}' not found, taking full page screenshot", selector);
                    await _page.ScreenshotAsync(new PageScreenshotOptions
                    {
                        Path = screenshotPath,
                        FullPage = true
                    });
                }
            }

            _logger.LogInformation("Screenshot saved to: {Path}", screenshotPath);
            return screenshotPath;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to take screenshot");
            throw;
        }
    }

    public async Task CloseAsync()
    {
        try
        {
            if (_page != null)
            {
                await _page.CloseAsync();
                _page = null;
            }

            if (_context != null)
            {
                await _context.CloseAsync();
                _context = null;
            }

            if (_browser != null)
            {
                await _browser.CloseAsync();
                _browser = null;
            }

            if (_playwright != null)
            {
                _playwright.Dispose();
                _playwright = null;
            }

            _logger.LogInformation("Browser resources closed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error closing browser resources");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }
}