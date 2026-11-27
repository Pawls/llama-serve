// The model finder page on port 8407 (plans/model-finder.md § S1).
using System;
using System.Collections.Generic;

sealed class ModelFinder
{
    public const int Port = 8407;

    public ModelFinder(string webDir, string catalogPath, Func<IEnumerable<string>> sources, Func<string> rebuild,
        Func<object> status)
    {
    }

    public void Start(IEnumerable<string> prefixes) { throw new NotImplementedException(); }

    public void Stop() { }
}
