using System.Net;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.FileProviders;
using Financements.QualityApi;

var builder = WebApplication.CreateBuilder(args);
string root = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../.."));
if (!File.Exists(Path.Combine(root, "agent-settings.json"))) throw new InvalidOperationException("Lancer depuis QualityAutomation/backend/Financements.QualityApi.");
builder.WebHost.UseUrls("http://127.0.0.1:5178");
builder.Services.AddSingleton(new RunCoordinator(root));
var app = builder.Build();
Directory.CreateDirectory(Path.Combine(root, ".local"));
string tokenPath = Path.Combine(root, ".local", "api-token.txt");
if (!File.Exists(tokenPath)) File.WriteAllText(tokenPath, Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));
var tokenAcl = new FileSecurity();
tokenAcl.SetAccessRuleProtection(true, false);
tokenAcl.AddAccessRule(new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.FullControl, AccessControlType.Allow));
new FileInfo(tokenPath).SetAccessControl(tokenAcl);
byte[] expectedToken = Encoding.UTF8.GetBytes(File.ReadAllText(tokenPath).Trim());
if (expectedToken.Length < 64) throw new InvalidOperationException("Jeton local invalide : supprimer .local/api-token.txt pour le régénérer.");

app.Use(async (context, next) =>
{
    if (context.Connection.RemoteIpAddress is not { } remote || !IPAddress.IsLoopback(remote)
        || context.Request.Host.Host != "127.0.0.1" || context.Request.Host.Port != 5178)
    { context.Response.StatusCode = 403; return; }
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Content-Security-Policy"] = "default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'self' blob:; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers.CacheControl = "no-store";
        string origin = context.Request.Headers.Origin.ToString();
        if ((origin.Length > 0 && origin != "http://127.0.0.1:5178")
            || context.Request.Headers["Sec-Fetch-Site"].ToString() == "cross-site")
        { context.Response.StatusCode = 403; return; }
        string authorization = context.Request.Headers.Authorization.ToString();
        byte[] candidate = Encoding.UTF8.GetBytes(authorization.StartsWith("Bearer ", StringComparison.Ordinal) ? authorization[7..] : "");
        if (!CryptographicOperations.FixedTimeEquals(expectedToken, candidate))
        { context.Response.StatusCode = 401; return; }
    }
    await next(context);
});

app.MapGet("/api/scenarios", (RunCoordinator runs) => runs.Scenarios);
app.MapGet("/api/agent/status", (RunCoordinator runs) => runs.AgentStatus());
app.MapGet("/api/runs", (RunCoordinator runs) => runs.History());
app.MapPost("/api/runs", (RunRequest request, RunCoordinator runs) => Start(request.ScenarioId, request.Emprunt, runs));
app.MapPost("/api/agent/launch", (RunCoordinator runs) => Start("lancer-financements", null, runs));
IResult Start(string id, int? emprunt, RunCoordinator runs)
{
    try { var run = runs.Start(id, emprunt); return Results.Accepted($"/api/runs/{run.Id}", run); }
    catch (InvalidOperationException ex) { return Results.Conflict(new { message = ex.Message }); }
    catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
}
app.MapGet("/api/runs/{id:guid}", (Guid id, RunCoordinator runs) => runs.Find(id) is { } run ? Results.Ok(run) : Results.NotFound());
app.MapPost("/api/runs/{id:guid}/cancel", (Guid id, RunCoordinator runs) => runs.Cancel(id) ? Results.Accepted() : Results.NotFound());
app.MapGet("/api/runs/{id:guid}/logs", (Guid id, RunCoordinator runs) => runs.Find(id) is { } run ? Results.Ok(run.Events) : Results.NotFound());
app.MapGet("/api/runs/{id:guid}/result", (Guid id, RunCoordinator runs) => runs.Find(id) is { } run ? Results.Ok(run) : Results.NotFound());
app.MapGet("/api/runs/{id:guid}/screenshots", (Guid id, RunCoordinator runs) => runs.Find(id) is null ? Results.NotFound() : Results.Ok(runs.Screenshots(id)));
app.MapGet("/api/runs/{id:guid}/screenshots/{name}", (Guid id, string name, RunCoordinator runs) =>
    name == "error.png" && runs.Find(id) is not null && File.Exists(runs.ArtifactPath(id, name))
        ? Results.File(runs.ArtifactPath(id, name), "image/png") : Results.NotFound());
app.MapGet("/api/runs/{id:guid}/events", async (Guid id, RunCoordinator runs, HttpContext context) =>
{
    if (runs.Find(id) is null) { context.Response.StatusCode = 404; return; }
    context.Response.ContentType = "text/event-stream";
    try
    {
        while (!context.RequestAborted.IsCancellationRequested)
        {
            var run = runs.Find(id)!;
            await context.Response.WriteAsync($"data: {JsonSerializer.Serialize(run, ApiJson.Options)}\n\n", context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
            if (run.Status is not ("RUNNING" or "QUEUED")) break;
            await Task.Delay(300, context.RequestAborted);
        }
    }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
});
string frontend = Path.Combine(root, "frontend", "dist");
if (Directory.Exists(frontend))
{
    app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(frontend) });
    app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(frontend) });
    app.MapFallbackToFile("index.html", new StaticFileOptions { FileProvider = new PhysicalFileProvider(frontend) });
}
app.Lifetime.ApplicationStopping.Register(() => app.Services.GetRequiredService<RunCoordinator>().Stop());
app.Run();
record RunRequest(string ScenarioId, int? Emprunt);
