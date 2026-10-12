using System.Diagnostics;
using System.Text.Json;
using Financements.LegacyPilot;
using YamlDotNet.RepresentationModel;

namespace Financements.QualityApi;

public static class ApiJson { public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web) { WriteIndented = false }; }
public sealed record Scenario(string Id, string Nom, string Description, string Application, int Emprunt,
    string Operation, string BaseSql, bool Disponible, string? Limitation, string[] Etapes, object? Parametres);
public sealed record RunEvent(DateTimeOffset Time, string Name, string Status, string Message, long DurationMs);
public sealed record RunStep(string Name, string Status, string Message = "", long DurationMs = 0);
public sealed class RunSnapshot
{
    public Guid Id { get; set; }
    public string ScenarioId { get; set; } = "";
    public string Scenario { get; set; } = "";
    public string Application { get; set; } = "Financements";
    public string? Database { get; set; }
    public string RequestedDatabase { get; set; } = "QA_FIN";
    public int? Emprunt { get; set; }
    public string Status { get; set; } = "QUEUED";
    public DateTimeOffset StartedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? FinishedAt { get; set; }
    public List<RunStep> Steps { get; set; } = [];
    public List<RunEvent> Events { get; set; } = [];
    public List<string> Errors { get; set; } = [];
    public List<string> Screenshots { get; set; } = [];
}

public sealed class RunCoordinator
{
    private readonly object gate = new();
    private readonly string root;
    private readonly Dictionary<Guid, RunSnapshot> runs = [];
    private CancellationTokenSource? cancellation;
    private Guid? activeId;
    private bool stopping;
    public Scenario[] Scenarios { get; }

    public RunCoordinator(string root)
    {
        this.root = root;
        Directory.CreateDirectory(Path.Combine(root, "results"));
        Scenarios = Directory.GetFiles(Path.Combine(root, "scenarios"), "*.yaml").Select(LoadScenario).OrderBy(s => s.Id != "ouvrir-emprunt").ThenBy(s => s.Nom).ToArray();
        foreach (var path in Directory.GetFiles(Path.Combine(root, "results"), "result.json", SearchOption.AllDirectories))
        {
            if (!Guid.TryParse(Path.GetFileName(Path.GetDirectoryName(path)), out Guid directoryId)) continue;
            try
            {
                var run = JsonSerializer.Deserialize<RunSnapshot>(File.ReadAllText(path), ApiJson.Options);
                if (run is null || run.Id != directoryId) continue;
                if (run.Status is "RUNNING" or "QUEUED")
                {
                    run.Status = "FAILED"; run.FinishedAt = DateTimeOffset.UtcNow;
                    run.Errors.Add("L'API a été interrompue avant la fin de l'exécution.");
                    foreach (var step in run.Steps.Where(s => s.Status == "RUNNING").ToArray()) run.Steps[run.Steps.IndexOf(step)] = step with { Status = "FAILED" };
                }
                runs[run.Id] = run;
                Save(run);
            }
            catch (JsonException) { }
        }
    }

    private static Scenario LoadScenario(string path)
    {
        var yaml = new YamlStream(); using var reader = File.OpenText(path); yaml.Load(reader);
        var map = (YamlMappingNode)yaml.Documents.Single().RootNode;
        string Get(string key, string fallback = "") => map.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode)value).Value ?? fallback : fallback;
        object? ConvertNode(YamlNode node) => node switch
        {
            YamlScalarNode scalar => scalar.Value,
            YamlSequenceNode seq => seq.Children.Select(ConvertNode).ToArray(),
            YamlMappingNode dict => dict.Children.ToDictionary(p => ((YamlScalarNode)p.Key).Value!, p => ConvertNode(p.Value)),
            _ => null
        };
        var steps = (YamlSequenceNode)map.Children[new YamlScalarNode("etapes")];
        string[] names = steps.Children.Select(n => n is YamlScalarNode scalar ? scalar.Value! : ((YamlScalarNode)((YamlMappingNode)n).Children.First().Key).Value!).ToArray();
        string id = Path.GetFileNameWithoutExtension(path);
        string[] supported = ["lancer_financements", "verifier_connexion", "ouvrir_gestion_emprunts", "rechercher_emprunt", "ouvrir_fiche", "verifier_fiche_ouverte"];
        bool available = id == "ouvrir-emprunt" && names.SequenceEqual(supported);
        if (id == "ouvrir-emprunt" && !available) throw new InvalidOperationException("Le scénario ouvrir-emprunt doit conserver les six étapes de navigation prises en charge.");
        return new(id, Get("nom"), Get("description"), Get("application", "Financements"), int.Parse(Get("emprunt", "2261")),
            Get("operation", id), Get("base_sql", "QA_FIN"), available,
            available ? null : "Automatisation métier non encore validée sur les écrans réels.",
            names, ConvertNode(steps));
    }

    public object AgentStatus()
    {
        var settings = ReadSettings();
        var processes = Process.GetProcessesByName("Financements").Where(p => p.SessionId == Process.GetCurrentProcess().SessionId).ToArray();
        try
        {
            return new { available = Environment.UserInteractive && Process.GetCurrentProcess().SessionId != 0,
                interactive = Environment.UserInteractive, sessionId = Process.GetCurrentProcess().SessionId,
                financementsOpen = processes.Length > 0, processIds = processes.Select(p => p.Id).ToArray(),
                executablePath = settings.GetProperty("executablePath").GetString(), sqlServer = settings.GetProperty("sqlServer").GetString(),
                requestedDatabase = settings.GetProperty("targetDatabase").GetString(), database = (string?)null,
                connectionVerified = false, connectionStatus = "UNKNOWN", busy = IsBusy() };
        }
        finally { foreach (var process in processes) process.Dispose(); }
    }
    private bool IsBusy() { lock (gate) return activeId.HasValue; }
    private JsonElement ReadSettings() => JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(Path.Combine(root, "agent-settings.json")));
    public RunSnapshot Start(string id, int? borrower)
    {
        lock (gate)
        {
            if (stopping || activeId.HasValue) throw new InvalidOperationException("Une exécution est déjà en cours ou l'agent est en arrêt.");
            var scenario = Scenarios.SingleOrDefault(s => s.Id == id);
            if (id != "lancer-financements" && (scenario is null || !scenario.Disponible)) throw new ArgumentException("Scénario inconnu ou non disponible.");
            if (borrower is <= 0 or > 999999999) throw new ArgumentException("Identifiant d'emprunt invalide.");
            var settings = ReadSettings();
            string target = settings.GetProperty("targetDatabase").GetString() ?? "";
            if (target is not ("QA_LEGACY" or "QA_FIN"))
                throw new ArgumentException("Cible non autorisée pour la lecture seule : QA_LEGACY ou QA_FIN attendue.");
            if (scenario is not null && scenario.BaseSql != target)
                throw new ArgumentException("La base du scénario ne correspond pas à la cible configurée.");
            int timeout = settings.GetProperty("runTimeoutSeconds").GetInt32();
            if (timeout is < 5 or > 600) throw new ArgumentException("Délai d'exécution attendu entre 5 et 600 secondes.");
            var run = new RunSnapshot { Id = Guid.NewGuid(), ScenarioId = id, Scenario = scenario?.Nom ?? "Lancer Financements", RequestedDatabase = target,
                Emprunt = id == "lancer-financements" ? null : borrower ?? scenario!.Emprunt,
                Steps = (scenario?.Etapes ?? ["lancer_financements"]).Select(n => new RunStep(n, "NOT_EXECUTED")).ToList() };
            runs.Add(run.Id, run); activeId = run.Id; cancellation = new();
            Directory.CreateDirectory(Path.GetDirectoryName(ArtifactPath(run.Id, "result.json"))!);
            // Snapshot trusted configuration, never accept a command or path from HTTP input.
            File.WriteAllText(ArtifactPath(run.Id, "settings.json"), settings.GetRawText());
            Save(run);
            var stopToken = cancellation.Token;
            var worker = new Thread(() => Execute(run.Id, timeout, stopToken))
            {
                IsBackground = true,
                Name = $"Financements-STA-{run.Id:N}"
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            return Clone(run);
        }
    }
    private void Execute(Guid id, int timeout, CancellationToken requestedStop)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(timeout));
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(requestedStop, deadline.Token);
        try
        {
            RunSnapshot run;
            lock (gate) { run = runs[id]; run.Status = "RUNNING"; Save(run); }
            var settings = JsonSerializer.Deserialize<DesktopSettings>(
                File.ReadAllText(ArtifactPath(id, "settings.json")), ApiJson.Options)
                ?? throw new InvalidOperationException("Configuration du pilote absente.");

            void OnEvent(DesktopEvent e)
            {
                var item = new RunEvent(e.Time, e.Name, e.Status, e.Message, e.DurationMs);
                lock (gate)
                {
                    File.AppendAllText(ArtifactPath(id, "agent.jsonl"),
                        JsonSerializer.Serialize(e, ApiJson.Options) + Environment.NewLine);
                    run.Events.Add(item);
                    if (e.Type == "step")
                    {
                        int index = run.Steps.FindIndex(s => s.Name == item.Name);
                        if (index >= 0) run.Steps[index] = new(item.Name, item.Status, item.Message, item.DurationMs);
                    }
                    Save(run);
                }
            }

            var outcome = new DesktopRunner().Execute(settings,
                Path.GetDirectoryName(ArtifactPath(id, "result.json"))!,
                run.ScenarioId, run.Emprunt ?? 0, limit.Token, OnEvent);
            if (!File.Exists(ArtifactPath(id, "agent-result.json")))
                throw new InvalidOperationException("Le pilote n'a pas produit de résultat vérifiable.");

            if (limit.IsCancellationRequested)
            {
                outcome.Status = deadline.IsCancellationRequested && !requestedStop.IsCancellationRequested
                    ? "FAILED" : "BLOCKED";
                string reason = outcome.Status == "BLOCKED"
                    ? "Arrêt demandé ; Financements laissé ouvert."
                    : "Délai global dépassé ; l'opération UI s'est enfin terminée.";
                outcome.Errors.Add(reason);
                File.WriteAllText(ArtifactPath(id, "agent-result.json"),
                    JsonSerializer.Serialize(outcome, ApiJson.Options));
                OnEvent(new DesktopEvent("information", "execution", outcome.Status,
                    reason, 0, DateTimeOffset.UtcNow));
            }
            lock (gate)
            {
                run.Status = outcome.Status;
                if (run.Status is not ("SUCCESS" or "FAILED" or "BLOCKED"))
                    throw new InvalidOperationException("Statut pilote invalide.");
                run.Errors = outcome.Errors.ToList();
                run.Screenshots = Screenshots(id).ToList();
                run.Database = outcome.Database;
            }
        }
        catch (OperationCanceledException)
        {
            Fail(id, requestedStop.IsCancellationRequested ? "BLOCKED" : "FAILED",
                requestedStop.IsCancellationRequested
                    ? "Arrêt demandé ; Financements laissé ouvert."
                    : "Délai global dépassé.");
        }
        catch (Exception ex) { Fail(id, "FAILED", ex.Message); }
        finally
        {
            lock (gate)
            {
                var run = runs[id];
                foreach (var step in run.Steps.Where(s => s.Status == "RUNNING").ToArray())
                    run.Steps[run.Steps.IndexOf(step)] = step with { Status = run.Status };
                run.FinishedAt = DateTimeOffset.UtcNow;
                Save(run);
                activeId = null;
                cancellation?.Dispose();
                cancellation = null;
            }
        }
    }
    private void Fail(Guid id, string status, string message)
    {
        lock (gate) { var run = runs[id]; run.Status = status; run.Errors.Add(message); run.Events.Add(new(DateTimeOffset.UtcNow, "execution", status, message, 0)); Save(run); }
    }
    public bool Cancel(Guid id)
    {
        lock (gate)
        {
            if (!runs.ContainsKey(id)) return false;
            if (activeId == id && cancellation is { IsCancellationRequested: false })
            {
                var e = new DesktopEvent("information", "annulation", "RUNNING",
                    "Arrêt demandé ; attente de la fin de l'opération UI en cours.", 0, DateTimeOffset.UtcNow);
                File.AppendAllText(ArtifactPath(id, "agent.jsonl"), JsonSerializer.Serialize(e, ApiJson.Options) + Environment.NewLine);
                runs[id].Events.Add(new(e.Time, e.Name, e.Status, e.Message, e.DurationMs));
                Save(runs[id]);
                cancellation.Cancel();
            }
            return true;
        }
    }
    public void Stop() { lock (gate) { stopping = true; cancellation?.Cancel(); } }
    public RunSnapshot? Find(Guid id) { lock (gate) return runs.TryGetValue(id, out var run) ? Clone(run) : null; }
    public RunSnapshot[] History() { lock (gate) return runs.Values.OrderByDescending(r => r.StartedAt).Select(Clone).ToArray(); }
    private static RunSnapshot Clone(RunSnapshot run) => JsonSerializer.Deserialize<RunSnapshot>(JsonSerializer.Serialize(run, ApiJson.Options), ApiJson.Options)!;
    public string ArtifactPath(Guid id, string name) => Path.Combine(root, "results", id.ToString("D"), name);
    public string[] Screenshots(Guid id) => File.Exists(ArtifactPath(id, "error.png")) ? ["error.png"] : [];
    private void Save(RunSnapshot run)
    {
        string path = ArtifactPath(run.Id, "result.json");
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(run, ApiJson.Options)); File.Move(path + ".tmp", path, true);
    }
}
