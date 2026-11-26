// The switchboard: one OpenAI-compatible port that starts whatever engine serves the model a request names.
// Compiled into the tray by install-tray.ps1 (C# 5: no string interpolation, no ?. operator).
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

// One model the front door serves, and the engine that answers it.
sealed class SwitchTarget
{
    public readonly string Engine;    // "router", "strata" or "ninfer"
    public readonly string ModelId;
    public readonly string BaseUrl;   // where the engine listens, e.g. http://127.0.0.1:8411

    public SwitchTarget(string engine, string modelId, string baseUrl)
    {
        Engine = engine;
        ModelId = modelId;
        BaseUrl = baseUrl;
    }

    public string Key { get { return Engine + ":" + ModelId; } }
}

// What the switchboard needs from the tray: the engine state and the start/stop logic.
interface ISwitchHost
{
    IList<SwitchTarget> Targets { get; }
    Task<bool> IsReadyAsync(SwitchTarget target);
    Task<string> EnsureAsync(SwitchTarget target);   // null once the target answers; otherwise why not
    Task<SwitchTarget> CurrentAsync();               // the engine that is up now, or null
    Task<string> FreeAsync();                        // null once the GPUs are free; otherwise why not
    Task<Dictionary<string, object>> StatusAsync();
}

sealed class Switchboard
{
    public TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    public TimeSpan BusyWait = TimeSpan.FromMinutes(15);

    public Switchboard(ISwitchHost host, string token, Action<string> log) { throw new NotImplementedException(); }

    public void Start(IEnumerable<string> prefixes) { throw new NotImplementedException(); }

    public void Stop() { throw new NotImplementedException(); }

    internal static bool TokenMatches(string expected, string authorization, string apiKey)
    {
        throw new NotImplementedException();
    }
}
