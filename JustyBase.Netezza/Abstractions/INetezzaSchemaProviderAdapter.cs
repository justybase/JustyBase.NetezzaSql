using JustyBase.Netezza.Models;
using JustyBase.NetezzaSqlParser.Visitor;

namespace JustyBase.Netezza.Abstractions;

/// <summary>
/// Loads a neutral metadata snapshot into the parser's schema provider
/// so the parser can resolve object names during completion and authoring.
/// </summary>
public interface INetezzaSchemaProviderAdapter
{
    /// <summary>Applies the snapshot to the given <paramref name="provider"/>.</summary>
    /// <param name="provider">The parser schema provider to populate.</param>
    /// <param name="snapshot">The neutral metadata snapshot to load.</param>
    /// <param name="clear">When <see langword="true"/>, clears the provider before loading.</param>
    void Apply(InMemorySchemaProvider provider, NetezzaSchemaSnapshot snapshot, bool clear = true);
}
