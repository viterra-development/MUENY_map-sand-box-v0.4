using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;
using TCDS.Importer.Models;

namespace TCDS.Importer.Services;

public class TcdsScrapingService : IAsyncDisposable
{
    private readonly ILogger<TcdsScrapingService> _logger;
    private readonly TcdsConfiguration _config;
    private IPlaywright? _playwright;
    private IBrowser? _browser;
    private IBrowserContext? _context;
    private IPage? _page;

    public TcdsScrapingService(ILogger<TcdsScrapingService> logger, IOptions<TcdsConfiguration> config)
    {
        _logger = logger;
        _config = config.Value;
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
                Timeout = _config.Timeout
            });

            if (response != null && response.Status == 429)
            {
                _logger.LogInformation("Received 429 response, waiting for redirect process...");
                
                await Task.Delay(5000, cancellationToken);
                
                await _page.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
            }

            await Task.Delay(_config.WaitDelay, cancellationToken);

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
                Timeout = _config.Timeout
            });

            // Get the left iframe containing the search form
            var leftFrame = _page.Frame("LeftFrame");
            if (leftFrame == null)
            {
                throw new InvalidOperationException("Could not find LeftFrame iframe");
            }

            _logger.LogInformation("Found search frame, waiting for form elements to load");

            // Wait a bit for the iframe content to load
            await Task.Delay(3000, cancellationToken);

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
            await Task.Delay(1000, cancellationToken);
            
            // Look for and click the Parker suggestion
            _logger.LogInformation("Looking for Parker suggestion in typeahead dropdown");
            try
            {
                await leftFrame.WaitForSelectorAsync(".tt-suggestion, .tt-suggestion p", new FrameWaitForSelectorOptions
                {
                    Timeout = 3000
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
            await Task.Delay(1000, cancellationToken);

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
            await Task.Delay(5000, cancellationToken); // Fixed 5-second wait

            // Try to wait for some indication that results have loaded
            try
            {
                await leftFrame.WaitForSelectorAsync(".station-data, table, .results", new FrameWaitForSelectorOptions
                {
                    Timeout = 5000 // 5 second timeout for results
                });
                _logger.LogInformation("Search results appear to have loaded");
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("Results selector not found after 5 seconds, proceeding anyway - results may still be visible");
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