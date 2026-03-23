using Cobalt.Watcher.Services;

var builder = Host.CreateApplicationBuilder(args);

var apiBaseUrl = builder.Configuration["ApiBaseUrl"] ?? "http://localhost:5031";

builder.Services.AddHttpClient("Api", c =>
{
    c.BaseAddress = new Uri(apiBaseUrl);
    // Authorization header is set dynamically per-request in ApiEventSender
    // after the OAuth token is obtained.
});

builder.Services.AddSingleton<WatcherAuthService>();
builder.Services.AddSingleton<ApiEventSender>();
builder.Services.AddHostedService<LogTailWorker>();

await builder.Build().RunAsync();
