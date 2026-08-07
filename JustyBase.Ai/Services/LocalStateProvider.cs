using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;
using System.Globalization;
using System.Text;

namespace JustyBase.Ai.Services;

public sealed class LocalStateProvider : ILocalStateProvider
{
    private readonly ISimpleLogger _logger;
    private readonly IChatDatabaseAccessProvider _databaseAccessProvider;
    private readonly IUiDispatcher _dispatcher;

    private Func<(string ConnectionName, string DatabaseName)?>? _activeSqlContextProvider;
    private Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?>? _sqlEditorContextProvider;

    public LocalStateProvider(
        ISimpleLogger logger,
        IChatDatabaseAccessProvider databaseAccessProvider,
        IUiDispatcher dispatcher)
    {
        _logger = logger;
        _databaseAccessProvider = databaseAccessProvider;
        _dispatcher = dispatcher;
    }

    public void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider)
    {
        _activeSqlContextProvider = provider;
    }

    public void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider)
    {
        _sqlEditorContextProvider = provider;
    }

    public (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetSqlEditorContextSnapshot()
    {
        if (_sqlEditorContextProvider is null)
        {
            return null;
        }

        try
        {
            if (_dispatcher.CheckAccess())
            {
                return _sqlEditorContextProvider.Invoke();
            }

            (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? captured = null;
            Exception? captureError = null;
            using var done = new ManualResetEventSlim(false);
            _ = _dispatcher.InvokeAsync(() =>
            {
                try { captured = _sqlEditorContextProvider.Invoke(); }
                catch (Exception ex) { captureError = ex; }
                finally { done.Set(); }
            });
            if (!done.Wait(TimeSpan.FromSeconds(5))) // ManualResetEventSlim
            {
                return null;
            }
            if (captureError is not null) throw captureError;
            return captured;
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            return null;
        }
    }

    public async Task<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> GetSqlEditorContextSnapshotAsync()
    {
        if (_sqlEditorContextProvider is null)
        {
            return null;
        }

        try
        {
            return await _dispatcher.InvokeAsync(_sqlEditorContextProvider);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            return null;
        }
    }

    public string BuildDatabaseContextSection()
    {
        if (!TryGetActiveDatabaseAccess(out var access, out var connectionName, out var databaseName, out _) || access is null)
        {
            return LocalDatabaseContextFormatter.BuildNoActiveConnectionContext();
        }

        try
        {
            var schemas = access.GetSchemas(databaseName, "").Take(20).ToList();
            return LocalDatabaseContextFormatter.BuildDatabaseContext(connectionName, databaseName, schemas);
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            return LocalDatabaseContextFormatter.BuildFallbackContext(connectionName, databaseName);
        }
    }

    public bool TryGetActiveDatabaseAccess(
        out IChatDatabaseAccess? access,
        out string connectionName,
        out string databaseName,
        out string errorMessage)
    {
        access = null;
        connectionName = string.Empty;
        databaseName = string.Empty;
        errorMessage = string.Empty;

        try
        {
            if (_activeSqlContextProvider is null)
            {
                errorMessage = "No active SQL context provider is configured.";
                return false;
            }

            (string ConnectionName, string DatabaseName)? context;
            if (_dispatcher.CheckAccess())
            {
                context = _activeSqlContextProvider.Invoke();
            }
            else
            {
                // Avoid InvokeAsync(...).GetResult() deadlock: marshal via Post + ManualResetEventSlim.
                (string ConnectionName, string DatabaseName)? captured = null;
                Exception? captureError = null;
                using var done = new ManualResetEventSlim(false);
                _ = _dispatcher.InvokeAsync(() =>
                {
                    try { captured = _activeSqlContextProvider.Invoke(); }
                    catch (Exception ex) { captureError = ex; }
                    finally { done.Set(); }
                });
                if (!done.Wait(TimeSpan.FromSeconds(5))) // ManualResetEventSlim
                {
                    errorMessage = "Timed out waiting for active SQL context on UI thread.";
                    return false;
                }
                if (captureError is not null) throw captureError;
                context = captured;
            }

            if (context is null || string.IsNullOrWhiteSpace(context.Value.ConnectionName))
            {
                errorMessage = "No active SQL document/connection is available.";
                return false;
            }

            connectionName = context.Value.ConnectionName;
            access = _databaseAccessProvider.GetDatabaseAccess(connectionName);
            if (access is null)
            {
                errorMessage = $"Could not initialize database service for connection '{connectionName}'.";
                return false;
            }

            databaseName = string.IsNullOrWhiteSpace(context.Value.DatabaseName)
                ? access.Database
                : context.Value.DatabaseName;
            return true;
        }
        catch (Exception ex)
        {
            _logger.TrackError(ex, isCrash: false);
            errorMessage = $"Failed to resolve active database context: {ex.Message}";
            return false;
        }
    }

    public string BuildAttachmentMetadataSection(List<ChatAttachment>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.AppendLine("[ATTACHED_REFERENCES]");
        sb.AppendLine("The user attached the following references (metadata only — file contents are not sent):");
        sb.AppendLine();

        foreach (var attachment in attachments)
        {
            if (attachment is null || string.IsNullOrWhiteSpace(attachment.Path))
            {
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(attachment.EffectiveDisplayName)
                ? attachment.Path
                : attachment.EffectiveDisplayName;

            if (File.Exists(attachment.Path))
            {
                var fileInfo = new FileInfo(attachment.Path);
                var ext = Path.GetExtension(attachment.Path).ToLowerInvariant();
                sb.AppendLine(CultureInfo.InvariantCulture, $"- File: {displayName}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Path: {attachment.Path}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Type: {ext}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Size: {FormatFileSize(fileInfo.Length)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Lines: {CountLines(attachment.Path)}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Last modified: {fileInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                if (attachment.StartLine.HasValue || attachment.EndLine.HasValue)
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"  Selected range: lines {attachment.StartLine ?? 1}-{attachment.EndLine ?? fileInfo.Length}");
                }
                sb.AppendLine();
            }
            else if (Directory.Exists(attachment.Path))
            {
                var dirInfo = new DirectoryInfo(attachment.Path);
                var fileCount = dirInfo.EnumerateFiles("*", SearchOption.AllDirectories).Count();
                sb.AppendLine(CultureInfo.InvariantCulture, $"- Directory: {displayName}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Path: {attachment.Path}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Files: {fileCount}");
                sb.AppendLine(CultureInfo.InvariantCulture, $"  Last modified: {dirInfo.LastWriteTime:yyyy-MM-dd HH:mm:ss}");
                sb.AppendLine();
            }
        }

        sb.AppendLine("[/ATTACHED_REFERENCES]");
        return sb.ToString().TrimEnd();
    }

    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
            _ => $"{bytes / (1024.0 * 1024.0):F1} MB"
        };
    }

    private static int CountLines(string path)
    {
        try
        {
            int count = 0;
            using var reader = new StreamReader(path);
            while (reader.ReadLine() is not null)
            {
                count++;
            }
            return count;
        }
        catch
        {
            return 0;
        }
    }
}
