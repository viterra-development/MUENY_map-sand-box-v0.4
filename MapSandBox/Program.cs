using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MapSandBox;
using MapSandBox.Services;
using MapSandBox.Models;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<MapService>();
builder.Services.AddScoped<MapLibreService>();
builder.Services.AddScoped<CrisService>();

// Configure Azure Tiles
var azureTileConfig = builder.Configuration.GetSection("AzureTiles").Get<AzureTileConfig>() ?? new AzureTileConfig();
builder.Services.AddSingleton(azureTileConfig);

// Soil data will be served as static files from wwwroot/soil-data/
// No additional services needed for static file approach

// Enable console logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);

await builder.Build().RunAsync();
