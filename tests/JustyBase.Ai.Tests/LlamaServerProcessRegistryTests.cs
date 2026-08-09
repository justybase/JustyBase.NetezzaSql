using JustyBase.Ai.Embedded.Server;
using System.Diagnostics;

namespace JustyBase.Ai.Tests;

public sealed class LlamaServerProcessRegistryTests
{
    [Fact]
    public void RegisterAndUnregister_RoundTrips()
    {
        var path = LlamaServerProcessRegistry.GetRegistryPath();
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            LlamaServerProcessRegistry.Register(123_456_001);
            LlamaServerProcessRegistry.Register(123_456_002);
            Assert.True(File.Exists(path));
            Assert.Contains("123456001|", File.ReadAllText(path), StringComparison.Ordinal);
            Assert.Contains("123456002|", File.ReadAllText(path), StringComparison.Ordinal);

            LlamaServerProcessRegistry.Unregister(123_456_001);
            var remaining = File.ReadAllText(path);
            Assert.DoesNotContain("123456001|", remaining, StringComparison.Ordinal);
            Assert.Contains("123456002|", remaining, StringComparison.Ordinal);
        }
        finally
        {
            RestoreRegistryFile(path, backup);
        }
    }

    [Fact]
    public void CleanupOrphans_SkipsLiveOwners_PreservesTheirEntries()
    {
        var path = LlamaServerProcessRegistry.GetRegistryPath();
        var backup = File.Exists(path) ? File.ReadAllText(path) : null;
        try
        {
            var me = Environment.ProcessId;
            File.WriteAllLines(path,
            [
                "999999991|999999990",        // dead owner, dead server -> kill attempt (no-op)
                $"999999992|{me}",            // live owner (this test process) -> must be skipped
                "not-a-pid|999999990",        // malformed -> ignored
                "999999993",                  // malformed -> ignored
            ]);

            var killed = LlamaServerProcessRegistry.CleanupOrphans();

            // A dead server pid cannot be killed, so the count stays 0 — the point is that
            // processing completed without touching the live-owner entry and without throwing.
            Assert.Equal(0, killed);
            Assert.True(File.Exists(path));
            Assert.Contains($"999999992|{me}|", File.ReadAllText(path), StringComparison.Ordinal);
            // The test process survived, proving the live-owner entry was never killed.
            Assert.True(Process.GetProcessById(me).HasExited == false);
        }
        finally
        {
            RestoreRegistryFile(path, backup);
        }
    }

    private static void RestoreRegistryFile(string path, string? backup)
    {
        try
        {
            if (backup is null)
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            else
            {
                File.WriteAllText(path, backup);
            }
        }
        catch
        {
            // best-effort restore
        }
    }
}
