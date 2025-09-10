# TCDS.Importer Implementation Plan

## Overview
This document outlines the implementation plan for creating a CLI-based .NET application that uses Playwright to navigate to https://txdot.public.ms2soft.com/tcds/tsearch.asp?mod=tcds, take a screenshot, and serve as the foundation for future automation of multiple pages on the site.

## Phase 1: Initial Setup and Screenshot Capability

### Project Structure
The project will be added to the existing MapSandBox.sln and follow a simplified version of the reference implementation structure from `playwright-example/`, adapted for CLI usage rather than Web API.

```
TCDS.Importer/
├── TCDS.Importer.csproj (Console Application)
├── Program.cs (Main entry point)
├── Services/
│   └── TcdsScrapingService.cs (Core scraping logic)
├── Models/
│   ├── PageData.cs (Data models)
│   └── TcdsConfiguration.cs (Configuration)
├── appsettings.json (Configuration file)
└── Screenshots/ (Output directory for screenshots)
```

### dotnet CLI Commands

```bash
# 1. Create console application in the existing solution directory
dotnet new console -n TCDS.Importer

# 2. Add project to existing MapSandBox.sln
dotnet sln MapSandBox.sln add TCDS.Importer/TCDS.Importer.csproj

# 3. Add required NuGet packages
cd TCDS.Importer
dotnet add package Microsoft.Playwright --version 1.53.0
dotnet add package Microsoft.Extensions.Hosting --version 9.0.0
dotnet add package Microsoft.Extensions.Logging --version 9.0.0
dotnet add package Microsoft.Extensions.Logging.Console --version 9.0.0
dotnet add package Microsoft.Extensions.Configuration --version 9.0.0
dotnet add package Microsoft.Extensions.Configuration.Json --version 9.0.0

# 4. Install Playwright browsers
dotnet run --project TCDS.Importer -- install chromium

# 5. Create required directories
mkdir -p TCDS.Importer/Screenshots
mkdir -p TCDS.Importer/Services
mkdir -p TCDS.Importer/Models
```

### Key Components

#### 1. TcdsConfiguration.cs
Based on the reference `PlaywrightConfig.cs`, adapted for TCDS-specific needs:
- Headless mode configuration (default: false for debugging)
- Timeout settings (60000ms like reference)
- Screenshot output directory
- Target URL configuration
- Wait delay configuration (5000ms like reference)

#### 2. TcdsScrapingService.cs
Simplified version of `PlaywrightBrowserService.cs` focused on:
- Browser initialization with Chrome using same configuration as reference
- Navigation to TCDS website with 429 response handling
- Screenshot capture functionality
- Proper resource cleanup with IAsyncDisposable pattern

#### 3. PageData.cs
Based on reference model but simplified for CLI usage:
- URL
- Title
- Content (HTML)
- Metadata dictionary
- Timestamp
- Screenshot path

#### 4. Program.cs
Console application entry point with:
- Dependency injection setup (using Microsoft.Extensions.Hosting)
- Configuration loading from appsettings.json
- Service execution
- Error handling and comprehensive logging

### Implementation Details

#### Browser Configuration
Matching the reference implementation for maximum compatibility:
- System Chrome usage (`Channel = "chrome"`)
- Headed mode by default for debugging (`Headless = false`)
- Same user agent and viewport settings as reference (1920x1080)
- All the same Chrome arguments for anti-detection
- Same timeout and wait delay settings

#### Screenshot Strategy
- Full page screenshots saved to `Screenshots/` directory
- Filename format: `tcds_screenshot_{timestamp:yyyyMMdd_HHmmss}.png`
- Element-specific screenshot capability for future phases
- Base64 encoding option for integration scenarios

#### Error Handling
- Same 429 response handling as reference implementation
- Graceful navigation failure handling
- Retry logic with exponential backoff
- Comprehensive logging for debugging
- Proper async disposal pattern

#### Anti-Detection Measures
Using the same proven approach from reference implementation:
- Realistic user agent strings
- Standard browser headers
- Chrome-specific arguments to avoid detection
- Natural timing delays
- DOM content loaded waiting strategy

### Configuration Files

#### appsettings.json
```json
{
  "TcdsConfiguration": {
    "Headless": false,
    "Browser": "chromium",
    "Timeout": 60000,
    "UseSystemChrome": true,
    "WaitDelay": 5000,
    "TargetUrl": "https://txdot.public.ms2soft.com/tcds/tsearch.asp?mod=tcds",
    "ScreenshotDirectory": "Screenshots"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  }
}
```

### Testing Approach
- Console application execution with verbose logging
- Screenshot verification to confirm successful page loading
- Network request logging (similar to reference implementation)
- Manual verification of anti-bot detection bypass

## Future Phases (Not Implemented Initially)

### Phase 2: Form Interaction
- Form field identification and population
- Search functionality implementation
- Result page navigation

### Phase 3: Data Extraction
- HTML parsing for search results
- Data model creation for extracted information
- CSV/JSON export functionality

### Phase 4: Multi-Page Automation
- Pagination handling
- Bulk data extraction
- Progress tracking and resumption

## Technical Considerations

### Browser Selection
- Using Chromium/Chrome for best compatibility (same as reference)
- Headed mode during development for visual debugging
- System Chrome usage for better compatibility

### Performance
- Single-threaded approach for Phase 1
- Proper async/await patterns throughout
- Resource cleanup to prevent memory leaks
- IAsyncDisposable implementation

### Resilience
- Network timeout handling (60-second timeout)
- Anti-bot detection mitigation (proven reference approach)
- 429 response handling with wait periods
- Graceful degradation on failures

### Security
- No sensitive data storage in initial implementation
- Secure configuration management via appsettings.json
- Network traffic logging for analysis

## Dependencies
- .NET 9.0 (matching existing solution)
- Microsoft.Playwright 1.53.0 (same version as reference)
- Microsoft.Extensions.* packages for dependency injection and configuration
- System Chrome browser (automatically detected by Playwright)

## Integration with Existing Solution
- Added to MapSandBox.sln alongside existing MapSandBox project
- Independent console application that doesn't interfere with existing Blazor app
- Shared solution structure for easier maintenance
- Potential for future integration with existing mapping capabilities

## Output
Phase 1 will produce:
1. Functional CLI application integrated into existing solution
2. Screenshot of the TCDS search page saved to Screenshots/ directory
3. Console logs showing successful navigation and timing information
4. Foundation for future automation phases
5. Proven anti-detection approach for Texas DOT website

## Success Criteria
- Application successfully navigates to https://txdot.public.ms2soft.com/tcds/tsearch.asp?mod=tcds
- Screenshot is captured and saved with timestamp
- No unhandled exceptions during execution
- Clean resource disposal with proper async patterns
- Clear logging output showing navigation steps and timing
- Successfully bypasses any anti-bot measures (based on proven reference implementation)