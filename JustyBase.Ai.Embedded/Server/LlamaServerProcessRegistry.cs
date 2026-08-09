using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace JustyBase.Ai.Embedded.Server;

/// <summary>
/// Tracks llama-server child processes on disk so that servers orphaned by a crashed host can be
/// cleaned up on the next application start. Registry updates take an exclusive file lock, which
/// keeps simultaneous application instances from overwriting one another's entries.
/// </summary>
public static class LlamaServerProcessRegistry
{
    private const string FileName = "justybase-llama-servers.txt";

    public static string GetRegistryPath()
    {
        var baseDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "JustyBase",
            "llama-server");
        return Path.Combine(baseDir, FileName);
    }

    /// <summary>Records a spawned server with enough identity data to avoid killing a reused PID.</summary>
    public static void Register(Process serverProcess, string? expectedExecutablePath = null)
    {
        if (serverProcess is null || serverProcess.Id <= 0)
        {
            return;
        }

        try
        {
            var server = ReadProcess(serverProcess);
            var owner = ReadProcess(Environment.ProcessId);
            var entry = new RegistryEntry(
                serverProcess.Id,
                Environment.ProcessId,
                server?.StartTimeUtcTicks,
                owner?.StartTimeUtcTicks,
                NormalizePath(expectedExecutablePath ?? server?.ExecutablePath));

            using var file = OpenRegistryFile();
            var lines = ReadLines(file);
            lines.RemoveAll(line => TryParse(line, out var existing)
                && existing.ServerPid == entry.ServerPid
                && existing.OwnerPid == entry.OwnerPid);
            lines.Add(Serialize(entry));
            WriteLines(file, lines);
        }
        catch
        {
            // Ownership tracking is best-effort and must never break server start.
        }
    }

    /// <summary>
    /// Compatibility overload for callers/tests that only have a PID. New server starts should
    /// pass the Process instance so cleanup can verify its start time and executable path.
    /// </summary>
    public static void Register(int serverPid)
    {
        if (serverPid <= 0)
        {
            return;
        }

        try
        {
            var server = ReadProcess(serverPid);
            var owner = ReadProcess(Environment.ProcessId);
            var entry = new RegistryEntry(
                serverPid,
                Environment.ProcessId,
                server?.StartTimeUtcTicks,
                owner?.StartTimeUtcTicks,
                NormalizePath(server?.ExecutablePath));

            using var file = OpenRegistryFile();
            var lines = ReadLines(file);
            lines.RemoveAll(line => TryParse(line, out var existing)
                && existing.ServerPid == entry.ServerPid
                && existing.OwnerPid == entry.OwnerPid);
            lines.Add(Serialize(entry));
            WriteLines(file, lines);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>Removes the entry for a server that was shut down cleanly.</summary>
    public static void Unregister(int serverPid)
    {
        if (serverPid <= 0)
        {
            return;
        }

        try
        {
            using var file = OpenRegistryFile();
            var lines = ReadLines(file);
            lines.RemoveAll(line => TryParse(line, out var entry) && entry.ServerPid == serverPid);
            WriteLines(file, lines);
        }
        catch
        {
            // best-effort
        }
    }

    /// <summary>
    /// Kills previously registered servers whose owner process is no longer the same process.
    /// Entries for live owners and entries that cannot be safely verified are retained.
    /// </summary>
    public static int CleanupOrphans()
    {
        var killed = 0;
        try
        {
            using var file = OpenRegistryFile();
            var retained = new List<string>();
            foreach (var line in ReadLines(file))
            {
                if (!TryParse(line, out var entry))
                {
                    continue;
                }

                var owner = ReadProcess(entry.OwnerPid);
                if (owner is not null && IsSameProcess(owner, entry.OwnerStartTimeUtcTicks))
                {
                    // A concurrently running application still owns this server. Keep the
                    // entry, even if the server has already exited, so another instance cannot
                    // erase the owner's record while it is starting or shutting down.
                    retained.Add(Serialize(entry));
                    continue;
                }

                var server = ReadProcess(entry.ServerPid);
                if (server is null)
                {
                    // The server has already exited; there is nothing left to clean up.
                    continue;
                }

                if (!MatchesRegisteredIdentity(server, entry))
                {
                    // The PID is now used by another process, or this is an old entry without
                    // identity metadata. Never kill an unverified process.
                    continue;
                }

                if (TryKill(server))
                {
                    killed++;
                }
                else
                {
                    // Access/transient failures should be retried by the next startup.
                    retained.Add(Serialize(entry));
                }
            }

            // Rewrite the registry under the same exclusive handle. In particular, do not delete
            // the file: that would erase entries registered by another live application instance.
            WriteLines(file, retained);
        }
        catch
        {
            // best-effort
        }

        return killed;
    }

    private static FileStream OpenRegistryFile()
    {
        var path = GetRegistryPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? string.Empty);
        return new FileStream(
            path,
            FileMode.OpenOrCreate,
            FileAccess.ReadWrite,
            FileShare.None,
            bufferSize: 4096,
            options: FileOptions.SequentialScan);
    }

    private static List<string> ReadLines(FileStream file)
    {
        file.Position = 0;
        using var reader = new StreamReader(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: true);
        return reader.ReadToEnd()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .ToList();
    }

    private static void WriteLines(FileStream file, IEnumerable<string> lines)
    {
        file.Position = 0;
        file.SetLength(0);
        using var writer = new StreamWriter(
            file,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            bufferSize: 4096,
            leaveOpen: true);
        foreach (var line in lines)
        {
            writer.WriteLine(line);
        }

        writer.Flush();
        file.Flush(flushToDisk: true);
    }

    private static string Serialize(RegistryEntry entry)
    {
        var executable = string.IsNullOrWhiteSpace(entry.ExecutablePath)
            ? string.Empty
            : Convert.ToBase64String(Encoding.UTF8.GetBytes(entry.ExecutablePath));
        return string.Join(
            '|',
            entry.ServerPid.ToString(CultureInfo.InvariantCulture),
            entry.OwnerPid.ToString(CultureInfo.InvariantCulture),
            entry.ServerStartTimeUtcTicks?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            entry.OwnerStartTimeUtcTicks?.ToString(CultureInfo.InvariantCulture) ?? string.Empty,
            executable);
    }

    private static bool TryParse(string line, out RegistryEntry entry)
    {
        entry = null!;
        var parts = line.Split('|');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var serverPid)
            || !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var ownerPid)
            || serverPid <= 0
            || ownerPid <= 0)
        {
            return false;
        }

        long? serverStart = null;
        long? ownerStart = null;
        if (parts.Length > 2
            && long.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedServerStart))
        {
            serverStart = parsedServerStart;
        }

        if (parts.Length > 3
            && long.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedOwnerStart))
        {
            ownerStart = parsedOwnerStart;
        }

        string? executable = null;
        if (parts.Length > 4 && !string.IsNullOrWhiteSpace(parts[4]))
        {
            try
            {
                executable = NormalizePath(Encoding.UTF8.GetString(Convert.FromBase64String(parts[4])));
            }
            catch
            {
                return false;
            }
        }

        entry = new RegistryEntry(serverPid, ownerPid, serverStart, ownerStart, executable);
        return true;
    }

    private static ProcessSnapshot? ReadProcess(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return null;
            }

            return new ProcessSnapshot(
                process.Id,
                process.StartTime.ToUniversalTime().Ticks,
                TryGetExecutablePath(process));
        }
        catch
        {
            return null;
        }
    }

    private static ProcessSnapshot? ReadProcess(int pid)
    {
        if (pid <= 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return ReadProcess(process);
        }
        catch
        {
            return null;
        }
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return NormalizePath(process.MainModule?.FileName);
        }
        catch
        {
            return null;
        }
    }

    private static bool IsSameProcess(ProcessSnapshot process, long? expectedStartTimeUtcTicks)
        => expectedStartTimeUtcTicks is null || process.StartTimeUtcTicks == expectedStartTimeUtcTicks;

    private static bool MatchesRegisteredIdentity(ProcessSnapshot process, RegistryEntry entry)
    {
        if (entry.ServerStartTimeUtcTicks is null && string.IsNullOrWhiteSpace(entry.ExecutablePath))
        {
            // Two-field records were written before identity tracking existed. They are not safe
            // to kill because the operating system may have reused the PID.
            return false;
        }

        if (entry.ServerStartTimeUtcTicks is long expectedStart
            && process.StartTimeUtcTicks != expectedStart)
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(entry.ExecutablePath)
            || string.IsNullOrWhiteSpace(process.ExecutablePath)
            || string.Equals(entry.ExecutablePath, process.ExecutablePath, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryKill(ProcessSnapshot process)
    {
        try
        {
            using var live = Process.GetProcessById(process.ProcessId);
            if (live.HasExited)
            {
                return false;
            }

            // Re-read identity immediately before killing to reduce the PID-reuse race window.
            var current = ReadProcess(live);
            if (current is null
                || current.StartTimeUtcTicks != process.StartTimeUtcTicks
                || (!string.IsNullOrWhiteSpace(process.ExecutablePath)
                    && !string.IsNullOrWhiteSpace(current.ExecutablePath)
                    && !string.Equals(process.ExecutablePath, current.ExecutablePath, StringComparison.OrdinalIgnoreCase)))
            {
                return false;
            }

            live.Kill(entireProcessTree: true);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? NormalizePath(string? path)
        => string.IsNullOrWhiteSpace(path)
            ? null
            : Path.GetFullPath(path);

    private sealed record RegistryEntry(
        int ServerPid,
        int OwnerPid,
        long? ServerStartTimeUtcTicks,
        long? OwnerStartTimeUtcTicks,
        string? ExecutablePath);

    private sealed record ProcessSnapshot(
        int ProcessId,
        long StartTimeUtcTicks,
        string? ExecutablePath);
}
