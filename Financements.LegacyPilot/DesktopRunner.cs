using System.Diagnostics;
using System.Text.Json;
using System.Text;
using System.Runtime.InteropServices;
using FlaUI.Core;
using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace Financements.LegacyPilot;

public sealed record DesktopSettings(string ExecutablePath, string SqlServer, string TargetDatabase,
    string FinancementsConnectionLabel, int WindowTimeoutSeconds = 30, int RunTimeoutSeconds = 90, string AutomationBackend = "UIA3");

public sealed record DesktopEvent(string Type, string Name, string Status, string Message, long DurationMs, DateTimeOffset Time);

public sealed class DesktopRunner
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private static readonly string[] NavigationSteps = ["lancer_financements", "verifier_connexion", "ouvrir_gestion_emprunts", "rechercher_emprunt", "ouvrir_fiche", "verifier_fiche_ouverte"];

    private CancellationToken cancellation;
    private Action<DesktopEvent> onEvent = _ => { };

    public DesktopResult Execute(DesktopSettings settings, string output, string scenario, int borrower,
        CancellationToken cancellation, Action<DesktopEvent> onEvent)
    {
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            throw new InvalidOperationException("Le pilote FlaUI doit être appelé sur un thread STA.");
        this.cancellation = cancellation;
        this.onEvent = onEvent;
        Directory.CreateDirectory(output);
        var result = new DesktopResult { Scenario = scenario, Emprunt = scenario == "ouvrir-emprunt" && borrower > 0 ? borrower : null };
        using var desktopLock = new Mutex(false, @"Local\FinancementsQualityAutomationDesktop");
        bool locked = false;
        try
        {
            cancellation.ThrowIfCancellationRequested();
            try { locked = desktopLock.WaitOne(0); } catch (AbandonedMutexException) { locked = true; }
            if (!locked) throw new DesktopBlockedException("Une autre exécution utilise déjà le bureau Financements.");
            if (!Environment.UserInteractive || Process.GetCurrentProcess().SessionId == 0)
                throw new DesktopBlockedException("L'agent doit fonctionner dans une session Windows interactive, hors session 0.");
            if (scenario is not ("ouvrir-emprunt" or "lancer-financements"))
                throw new DesktopBlockedException("Ce scénario n'est pas implémenté.");
            if (scenario == "ouvrir-emprunt" && borrower <= 0)
                throw new ArgumentException("L'identifiant de l'emprunt doit être positif.");
            // QA_FIN is explicitly authorized for read-only navigation in the latest request.
            // No modifying scenario is executable by this runner.
            if (settings.TargetDatabase is not ("QA_LEGACY" or "QA_FIN"))
                throw new DesktopBlockedException("Cible non autorisée pour la lecture seule : QA_LEGACY ou QA_FIN attendue.");
            if (settings.WindowTimeoutSeconds is < 1 or > 120)
                throw new ArgumentException("Délai fenêtre attendu entre 1 et 120 secondes.");
            using AutomationBase automation = settings.AutomationBackend switch
            {
                "UIA3" => new UIA3Automation(),
                "UIA2" => new FlaUI.UIA2.UIA2Automation(),
                _ => throw new ArgumentException("Backend attendu : UIA3 ou UIA2.")
            };
            var watch = Stopwatch.StartNew();
            Window? window = null;
            using var app = Step(result, "lancer_financements", () =>
            {
                var attached = LaunchOrAttach(settings);
                try
                {
                    while (window == null && watch.Elapsed.TotalSeconds < settings.WindowTimeoutSeconds)
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var windows = attached.GetAllTopLevelWindows(automation);
                        window = windows.FirstOrDefault(w => w.Properties.ClassName.ValueOrDefault == "Loan2000Class");
                        if (window == null && windows.Any(w => w.Title == "Login"))
                            throw new DesktopBlockedException("Financements attend une connexion dans la fenêtre Login. Connecter l'application à " + settings.FinancementsConnectionLabel + " puis relancer le scénario.");
                        if (window == null && cancellation.WaitHandle.WaitOne(200)) cancellation.ThrowIfCancellationRequested();
                    }
                    if (window == null) throw new TimeoutException("La fenêtre principale Financements n'a pas été détectée dans le délai.");
                    if (!window.IsEnabled)
                    {
                        var modals = window.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Window));
                        var modalNames = modals.Select(w => w.Properties.Name.ValueOrDefault).Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
                        var details = modals.SelectMany(m => m.FindAllDescendants(cf => cf.ByControlType(FlaUI.Core.Definitions.ControlType.Text)))
                            .Select(t => t.Properties.Name.ValueOrDefault).Where(n => !string.IsNullOrWhiteSpace(n));
                        throw new DesktopBlockedException("Une fenêtre modale bloque Financements : " + (modalNames.Length > 0 ? string.Join(" ; ", modalNames) : "dialogue non identifié") + ". " + string.Join(" ", details) + " Aucun dialogue n'a été validé automatiquement.");
                    }
                    return attached;
                }
                catch { attached.Dispose(); throw; }
            });
            result.ProcessId = app.ProcessId;
            result.WindowTitle = window!.Title;
            Emit("information", "fenetre_principale", "SUCCESS", $"Fenêtre réelle détectée : {window.Title} (PID {app.ProcessId}).", watch.ElapsedMilliseconds);
            if (scenario == "lancer-financements") result.Status = "SUCCESS";
            else
            {
                Step(result, "verifier_connexion", () =>
                {
                    var evidence = SqlConnectionVerifier.Verify(settings, app.ProcessId);
                    File.WriteAllText(Path.Combine(output, "connection-evidence.json"), JsonSerializer.Serialize(evidence, Json));
                    result.Database = evidence.Database;
                    Emit("information", "catalogue_sql", "SUCCESS", $"Catalogue physique {evidence.Database} vérifié sur {evidence.Server} pour le PID {app.ProcessId}.", 0);
                });
                Step(result, "ouvrir_gestion_emprunts", () => EmpruntPage.OpenList(window));
                result.WindowTitle = window.Title;
                Step(result, "rechercher_emprunt", () => EmpruntPage.SearchByNumber(window, borrower, cancellation));
                Step(result, "ouvrir_fiche", () => EmpruntPage.OpenEmprunt(window, borrower, cancellation));
                Step(result, "verifier_fiche_ouverte", () => EmpruntPage.VerifyOpened(window, borrower, cancellation));
                result.WindowTitle = window.Title;
                result.Status = "SUCCESS";
            }
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { FinishError(result, output, "BLOCKED", "Exécution interrompue entre deux opérations UI."); }
        catch (DesktopBlockedException ex) { FinishError(result, output, "BLOCKED", ex.Message); }
        catch (Exception ex) { FinishError(result, output, "FAILED", ex.Message); }
        finally { if (locked) desktopLock.ReleaseMutex(); }
        foreach (string name in scenario == "ouvrir-emprunt" ? NavigationSteps : ["lancer_financements"])
            if (!result.Steps.Any(s => s.Name == name)) result.Steps.Add(new(name, "NOT_EXECUTED", "Étape dépendante non exécutée.", 0));
        File.WriteAllText(Path.Combine(output, "agent-result.json"), JsonSerializer.Serialize(result, Json));
        Emit("completed", "execution", result.Status, result.Errors.FirstOrDefault() ?? "Commande terminée et vérifiée.", 0);
        return result;
    }

    private static FlaUI.Core.Application LaunchOrAttach(DesktopSettings settings)
    {
        string expectedPath = Path.GetFullPath(settings.ExecutablePath);
        if (!File.Exists(expectedPath) || !string.Equals(Path.GetFileName(expectedPath), "Financements.exe", StringComparison.OrdinalIgnoreCase))
            throw new FileNotFoundException("L'exécutable configuré doit être un fichier Financements.exe existant.");
        int currentSession = Process.GetCurrentProcess().SessionId;
        var matches = new List<int>();
        foreach (var process in Process.GetProcessesByName("Financements"))
        {
            using (process)
            {
                if (process.SessionId != currentSession) continue;
                string? path;
                try { path = ProcessImagePath.Read(process.Id); }
                catch { throw new DesktopBlockedException("Impossible de vérifier le chemin du processus Financements existant."); }
                if (!string.Equals(path, expectedPath, StringComparison.OrdinalIgnoreCase))
                    throw new DesktopBlockedException("Une instance Financements d'une autre installation est déjà ouverte.");
                matches.Add(process.Id);
            }
        }
        if (matches.Count > 1) throw new DesktopBlockedException("Plusieurs instances Financements sont présentes : sélection ambiguë.");
        if (matches.Count == 1) return FlaUI.Core.Application.Attach(matches[0]);
        return FlaUI.Core.Application.Launch(new ProcessStartInfo(expectedPath) { WorkingDirectory = Path.GetDirectoryName(expectedPath)!, UseShellExecute = true });
    }

    private T Step<T>(DesktopResult result, string name, Func<T> action)
    {
        var timer = Stopwatch.StartNew();
        cancellation.ThrowIfCancellationRequested();
        Emit("step", name, "RUNNING", "Étape en cours.", 0);
        try
        {
            T value = action();
            if (cancellation.IsCancellationRequested)
            {
                if (value is IDisposable disposable) disposable.Dispose();
                cancellation.ThrowIfCancellationRequested();
            }
            result.Steps.Add(new(name, "SUCCESS", "Étape terminée.", timer.ElapsedMilliseconds));
            Emit("step", name, "SUCCESS", "Étape terminée.", timer.ElapsedMilliseconds);
            return value;
        }
        catch (Exception ex)
        {
            string status = ex is DesktopBlockedException or OperationCanceledException ? "BLOCKED" : "FAILED";
            var message = string.IsNullOrWhiteSpace(ex.Message) ? ex.GetType().Name : ex.Message;
            result.Steps.Add(new(name, status, message, timer.ElapsedMilliseconds));
            Emit("step", name, status, message, timer.ElapsedMilliseconds);
            throw;
        }
    }
    private void Step(DesktopResult result, string name, Action action) => Step(result, name, () => { action(); return true; });
    private void FinishError(DesktopResult result, string output, string status, string message)
    {
        result.Status = status;
        result.Errors.Add(string.IsNullOrWhiteSpace(message) ? "Erreur du pilote sans message détaillé." : message);
        try { result.Screenshots.Add(Path.GetFileName(ScreenshotService.Capture(output))); }
        catch (Exception ex) { Emit("information", "capture", "FAILED", ex.Message, 0); }
    }
    private void Emit(string type, string name, string status, string message, long durationMs) =>
        onEvent(new DesktopEvent(type, name, status, message, durationMs, DateTimeOffset.UtcNow));
}

public sealed class DesktopBlockedException(string message) : Exception(message);
internal static class ProcessImagePath
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inheritHandle, int processId);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool QueryFullProcessImageName(IntPtr process, int flags, StringBuilder path, ref int size);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
    public static string Read(int pid)
    {
        var handle = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION, no module enumeration.
        if (handle == IntPtr.Zero) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            int size = 32768; var path = new StringBuilder(size);
            if (!QueryFullProcessImageName(handle, 0, path, ref size)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
            return path.ToString();
        }
        finally { CloseHandle(handle); }
    }
}
public sealed record DesktopStep(string Name, string Status, string Message, long DurationMs);
public sealed class DesktopResult
{
    public string Scenario { get; init; } = "";
    public string Application { get; } = "Financements";
    public string? Database { get; set; }
    public int? Emprunt { get; init; }
    public int? ProcessId { get; set; }
    public string? WindowTitle { get; set; }
    public string Status { get; set; } = "FAILED";
    public List<DesktopStep> Steps { get; } = [];
    public List<string> Errors { get; } = [];
    public List<string> Screenshots { get; } = [];
}
