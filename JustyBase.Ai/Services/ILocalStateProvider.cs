using JustyBase.Ai.Models;
using JustyBase.Ai.Ports;

namespace JustyBase.Ai.Services;

public interface ILocalStateProvider
{
    void SetActiveSqlContextProvider(Func<(string ConnectionName, string DatabaseName)?> provider);
    void SetSqlEditorContextProvider(Func<(string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)?> provider);
    (string FullText, string SelectedText, int SelectionStart, int SelectionLength, int CaretOffset)? GetSqlEditorContextSnapshot();
    string BuildDatabaseContextSection();
    bool TryGetActiveDatabaseAccess(out IChatDatabaseAccess? access, out string connectionName, out string databaseName, out string errorMessage);
    string BuildAttachmentMetadataSection(List<ChatAttachment>? attachments);
}
