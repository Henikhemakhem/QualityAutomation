using System.Text.Json;
using FlaUI.Core.AutomationElements;

namespace Financements.LegacyPilot;

public static class DesktopDiagnostics
{
    public static ExecutionResult Inspect(int pid, string backend, string output, int depth = 30, bool listOnly = false, CancellationToken cancellation = default)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("L'inspection FlaUI doit être appelée sur un thread STA.");
        if (pid <= 0 || backend is not ("UIA3" or "UIA2") || depth is < 0 or > 30)
            throw new ArgumentException("PID, backend ou profondeur invalide.");
        cancellation.ThrowIfCancellationRequested();
        output = Path.GetFullPath(output);
        Directory.CreateDirectory(output);
        using var log = new StructuredLog(Path.Combine(output, "execution.jsonl"));
        var result = new ExecutionResult { Scenario = "inspection_accessibilite", ProcessId = pid, Backend = backend };
        try
        {
            using var app = FinancementsLauncher.AttachForInspection(pid);
            using FlaUI.Core.AutomationBase automation = backend == "UIA3"
                ? new FlaUI.UIA3.UIA3Automation() : new FlaUI.UIA2.UIA2Automation();
            log.Write("attached", new { pid, backend });
            var windows = app.GetAllTopLevelWindows(automation);
            var inspector = new AccessibilityInspector(log, depth);
            AutomationElement[] roots = windows;
            if (listOnly)
            {
                var main = windows.Single(w => w.Properties.ClassName.ValueOrDefault == "Loan2000Class");
                var mdi = main.FindFirstChild(cf => cf.ByAutomationId("59648")) ?? throw new InvalidOperationException("Conteneur MDI absent.");
                roots = mdi.FindAllChildren().Where(e => e.Name.Trim() == "Liste complète").ToArray();
                if (roots.Length != 1) throw new InvalidOperationException("Liste complète absente ou ambiguë.");
            }
            var tree = roots.Select(w => { cancellation.ThrowIfCancellationRequested(); return inspector.Read(w, 0); }).ToArray();
            File.WriteAllText(Path.Combine(output, "uia-tree.json"), JsonSerializer.Serialize(tree, JsonOptions.Value));
            result.Steps.Add(new("inventorier_fenetres", windows.Length == 0 ? "FAILED" : "SUCCESS"));
            if (windows.Length == 0)
                throw new InvalidOperationException("Aucune fenêtre UI Automation du processus. Session, bureau, droits ou mode invisible à vérifier.");
            if (inspector.Errors > 0)
                throw new InvalidOperationException($"Inventaire partiel : {inspector.Errors} erreur(s) ou limite(s). Voir execution.jsonl.");
            result.Status = "SUCCESS";
            log.Write("inspection_completed", new { windows = windows.Length });
        }
        catch (Exception ex)
        {
            result.Status = "FAILED";
            result.Errors.Add(ex.Message);
            log.Write("inspection_failed", new { type = ex.GetType().Name, message = ex.Message });
            try { result.Screenshots.Add(ScreenshotService.Capture(output)); }
            catch (Exception captureError) { log.Write("screenshot_failed", new { message = captureError.Message }); }
        }
        File.WriteAllText(Path.Combine(output, "result.json"), JsonSerializer.Serialize(result, JsonOptions.Value));
        return result;
    }
}
