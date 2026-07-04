using WolfGameServer.Hubs;
using WolfGameServer.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Render.com / any host: listen on the port given via the PORT env var ---
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// --- Services ---
builder.Services.AddSignalR();
builder.Services.AddSingleton<GameLoopService>();

const string CorsPolicy = "AllowClient";
builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        // SignalR requires a specific origin (not "*") when credentials are allowed.
        // SetIsOriginAllowed(_ => true) lets any origin connect, which is convenient
        // for a game like this that may be embedded/hosted from different places.
        policy.SetIsOriginAllowed(_ => true)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});

var app = builder.Build();

app.UseCors(CorsPolicy);
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapHub<GameHub>("/gamehub");

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.Run();
