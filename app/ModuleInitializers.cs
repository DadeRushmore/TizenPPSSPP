using System;
using System.Collections.Generic;

// Only for audited, hash-verified modules with local DT_INIT_ARRAY entries.
// The mapping owner must relocate, flush caches and apply W^X before Run.
internal sealed class ModuleInitializers
{
    bool started;
    internal void Run(IReadOnlyList<int> entries,Action<int> invoke)
    {
        if(started) throw new InvalidOperationException("Module initialization already attempted");
        started=true; // also prevent retries after partial failure
        foreach(int entry in entries) invoke(entry);
    }
}
