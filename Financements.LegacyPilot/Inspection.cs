using System.Diagnostics;
using System.Text.Json;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.Core.Capturing;

namespace Financements.LegacyPilot;

public static class JsonOptions
{
    public static JsonSerializerOptions Value { get; } = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower };
}

public static class FinancementsLauncher
{
    // No automatic launch: legacy startup may connect to LastBase and perform synchronization.
    public static FlaUI.Core.Application AttachForInspection(int pid)
    {
        using var process = Process.GetProcessById(pid);
        if (!string.Equals(process.ProcessName, "Financements", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Le PID ne correspond pas à Financements.exe.");
        return FlaUI.Core.Application.Attach(pid);
    }
}

public sealed class AccessibilityInspector(StructuredLog log, int maxDepth = 30)
{
    private int count;
    public int Errors { get; private set; }
    public object Read(AutomationElement element, int depth)
    {
        if (++count > 5000 || depth > 30)
        {
            Errors++;
            log.Write("inventory_limit", new { depth, count });
            return new { truncated = true };
        }
        try
        {
            var children = new List<object>();
            var snapshot = new
            {
                automation_id = element.Properties.AutomationId.ValueOrDefault,
                name = element.Properties.Name.ValueOrDefault,
                control_type = element.Properties.ControlType.ValueOrDefault.ToString(),
                class_name = element.Properties.ClassName.ValueOrDefault,
                native_window_handle = element.Properties.NativeWindowHandle.ValueOrDefault.ToInt64(),
                enabled = element.Properties.IsEnabled.ValueOrDefault,
                offscreen = element.Properties.IsOffscreen.ValueOrDefault,
                value = element.Patterns.Value.IsSupported ? element.Patterns.Value.Pattern.Value.ValueOrDefault : null,
                bounds = element.BoundingRectangle.ToString(),
                depth_limit = depth >= maxDepth,
                patterns = new
                {
                    value = element.Patterns.Value.IsSupported,
                    invoke = element.Patterns.Invoke.IsSupported,
                    selection_item = element.Patterns.SelectionItem.IsSupported,
                    grid = element.Patterns.Grid.IsSupported,
                    table = element.Patterns.Table.IsSupported,
                    text = element.Patterns.Text.IsSupported
                },
                children
            };
            if (depth < maxDepth)
            {
                try
                {
                    var observedChildren = element.FindAllChildren();
                    if (observedChildren.Length > 100)
                    {
                        Errors++;
                        log.Write("children_limit", new { depth, total = observedChildren.Length, retained = 100 });
                    }
                    children.AddRange(observedChildren.Take(100).Select(c => Read(c, depth + 1)));
                }
                catch (Exception ex)
                {
                    Errors++;
                    log.Write("children_read_failed", new { depth, message = ex.Message });
                    children.Add(new { error = ex.Message });
                }
            }
            return snapshot;
        }
        catch (Exception ex)
        {
            Errors++;
            log.Write("element_read_failed", new { depth, message = ex.Message });
            return new { error = ex.Message };
        }
    }
}

public sealed class StructuredLog(string path) : IDisposable
{
    private readonly StreamWriter writer = new(path) { AutoFlush = true };
    public void Write(string eventName, object data) => writer.WriteLine(JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, event_name = eventName, data }));
    public void Dispose() => writer.Dispose();
}

public static class ScreenshotService
{
    public static string Capture(string directory)
    {
        var path = Path.Combine(directory, "error.png");
        FlaUI.Core.Capturing.Capture.Screen().ToFile(path);
        return path;
    }
}

public sealed class ExecutionResult
{
    public string Scenario { get; init; } = "";
    public string Application { get; init; } = "Financements";
    // Unknown until active physical connection is proven, never report a requested target as an observed database.
    public string? Database { get; init; }
    public int? Emprunt { get; init; }
    public string Status { get; set; } = "FAILED";
    public int ProcessId { get; init; }
    public string Backend { get; init; } = "UIA3";
    public List<StepResult> Steps { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Screenshots { get; } = [];
}
public sealed record StepResult(string Name, string Status);
