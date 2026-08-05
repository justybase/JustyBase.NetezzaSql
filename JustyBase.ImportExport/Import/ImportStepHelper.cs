namespace JustyBase.ImportExport.Import;

/// <summary>One deferred step of a multi-sheet import (host UI decides when to run <see cref="Func"/>).</summary>
public sealed class ImportStepHelper
{
    public Func<Task>? Func { get; set; }

    public IImportJob? ImportJob { get; set; }
}
