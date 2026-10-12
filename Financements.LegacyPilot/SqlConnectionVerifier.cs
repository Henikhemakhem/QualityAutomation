using System.Diagnostics;
using System.Globalization;

namespace Financements.LegacyPilot;

internal sealed record SqlConnectionEvidence(string Server, string Database, int ProcessId, DateTimeOffset VerifiedAt, int[] SessionIds);

internal static class SqlConnectionVerifier
{
    public static SqlConnectionEvidence Verify(DesktopSettings settings, int processId)
    {
        // A remote host could reuse the same PID. This proof is restricted to the local instance.
        if (!string.Equals(settings.SqlServer, @"localhost\SQLEXPRESS", StringComparison.OrdinalIgnoreCase))
            throw new DesktopBlockedException("La vérification SQL est disponible uniquement pour localhost\\SQLEXPRESS.");
        string executable = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            @"Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE");
        if (!File.Exists(executable)) throw new DesktopBlockedException("SQLCMD est absent : connexion physique non vérifiée.");
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
        };
        string host = Environment.MachineName.Replace("'", "''");
        // Read session metadata only; never query or modify business tables.
        string query = "SET NOCOUNT ON; SELECT session_id, DB_NAME(database_id), program_name FROM sys.dm_exec_sessions " +
            "WHERE is_user_process = 1 AND host_process_id = " + processId.ToString(CultureInfo.InvariantCulture) +
            " AND host_name = N'" + host + "';";
        foreach (string argument in new[] { "-S", settings.SqlServer, "-E", "-d", "master", "-l", "5", "-t", "5", "-b", "-W", "-h", "-1", "-s", "|", "-Q", query })
            start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new DesktopBlockedException("Impossible de lancer la vérification SQL.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(15000))
        {
            process.Kill(entireProcessTree: true);
            throw new DesktopBlockedException("Délai de vérification SQL dépassé.");
        }
        string output = stdout.GetAwaiter().GetResult();
        string error = stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0)
            throw new DesktopBlockedException("Connexion physique non vérifiée : SQLCMD a échoué. " + error.Trim());
        var rows = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split('|', StringSplitOptions.TrimEntries)).ToArray();
        if (rows.Length == 0 || rows.Any(row => row.Length != 3 || !int.TryParse(row[0], out _)))
            throw new DesktopBlockedException("Aucune preuve SQL exploitable pour le processus Financements actif.");
        if (!rows.Any(row => row[2] == "Application Financements") || rows.Any(row => row[1] != settings.TargetDatabase))
            throw new DesktopBlockedException("Le catalogue actif du processus Financements ne correspond pas exclusivement à " + settings.TargetDatabase + ".");
        return new(settings.SqlServer, settings.TargetDatabase, processId, DateTimeOffset.UtcNow,
            rows.Select(row => int.Parse(row[0], CultureInfo.InvariantCulture)).ToArray());
    }
}
