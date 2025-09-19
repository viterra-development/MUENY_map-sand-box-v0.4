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

// Configure Soil Data URLs
var soilDataConfig = builder.Configuration.GetSection("SoilData").Get<SoilDataConfig>() ?? new SoilDataConfig();
builder.Services.AddSingleton(soilDataConfig);

// Enable console logging
builder.Logging.SetMinimumLevel(LogLevel.Debug);

await builder.Build().RunAsync();
