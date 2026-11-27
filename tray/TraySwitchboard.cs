// The tray's half of the switchboard (Switchboard.cs): which model belongs to which engine, and how to bring an
// engine up using the same start/stop methods the menu uses. Design and rules: dev-notes/archive/switchboard.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class TrayApp : ISwitchHost
{
    static readonly TimeSpan SwitchRouterWait = TimeSpan.FromMinutes(6);
    static readonly TimeSpan SwitchStrataWait = TimeSpan.FromMinutes(12);
    static readonly TimeSpan SwitchNinferWait = TimeSpan.FromMinutes(8);
    // An engine that has not appeared this long after its start was launched did not start.
    static readonly TimeSpan SwitchLaunchGrace = TimeSpan.FromSeconds(30);

    readonly object switchLogLock = new object();
    Switchboard switchboard;
    List<SwitchTarget> switchTargets;
    ToolStripMenuItem switchboardItem, switchboardTokenItem;
    bool switchboardEnabled;   // tray-settings.json's SwitchboardEnabled; only this tray writes it
    string switchboardBound;   // "network", "local", or null while it is not listening
    bool switchboardRebindPending;

    // ---- Listener ----

    string SwitchboardTokenPath() { return Path.Combine(root, "logs", "switchboard-token"); }

    // The token is made on first use and kept; the menu opens the file so it can be copied to another machine.
    string ReadOrCreateSwitchboardToken()
    {
        string path = SwitchboardTokenPath();
        if (File.Exists(path))
        {
            string saved = File.ReadAllText(path).Trim();
            if (saved.Length >= 32) return saved;
        }
        var bytes = new byte[32];
        using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
        string token = string.Concat(bytes.Select(b => b.ToString("x2")));
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.WriteAllText(path, token);
        return token;
    }

    // Starts, restarts or stops the listener to match SwitchboardEnabled and ShareOnNetwork. Returns where it
    // listens ("network" or "local"), or null when it is off or could not bind (a notice says why). A listener that is
    // answering requests is left alone until they end, since closing it aborts their replies.
    string ApplySwitchboard()
    {
        if (switchboardEnabled && switchboard != null && switchboard.Busy)
        {
            switchboardRebindPending = true;
            Notify("The front door moves once the replies it is running have finished.", ToolTipIcon.Info);
            return switchboardBound;
        }
        switchboardRebindPending = false;
        StopSwitchboard();
        if (!switchboardEnabled) return null;
        if (switchTargets == null) switchTargets = BuildSwitchTargets();
        var board = new Switchboard(this, ReadOrCreateSwitchboardToken(), WriteSwitchLog);
        try
        {
            board.Start(Switchboard.Prefixes(shareOnNetwork));
        }
        catch (HttpListenerException ex)
        {
            if (!shareOnNetwork)
            {
                Notify("The front door could not listen on port " + Switchboard.Port + ": " + ex.Message, ToolTipIcon.Error);
                return null;
            }
            // Access denied: the one-time URL reservation for all interfaces is missing.
            Notify("The front door cannot listen on the network yet (" + ex.Message + "). It is local only until you run allow-switchboard.ps1 as administrator.", ToolTipIcon.Warning);
            try { board.Start(Switchboard.Prefixes(false)); }
            catch (HttpListenerException local)
            {
                Notify("The front door could not listen on port " + Switchboard.Port + ": " + local.Message, ToolTipIcon.Error);
                return null;
            }
            switchboard = board;
            switchboardBound = "local";
            return switchboardBound;
        }
        switchboard = board;
        switchboardBound = shareOnNetwork ? "network" : "local";
        return switchboardBound;
    }

    void StopSwitchboard()
    {
        if (switchboard == null) return;
        switchboard.Stop();
        switchboard = null;
        switchboardBound = null;
    }

    // Called by every poll: a rebind postponed because replies were running happens once they have ended.
    void ApplyPendingSwitchboardRebind()
    {
        if (switchboardRebindPending && (switchboard == null || !switchboard.Busy)) ApplySwitchboard();
    }

    Task ToggleSwitchboardAsync()
    {
        bool turnOn = !switchboardEnabled;
        switchboardEnabled = turnOn;
        string bound = ApplySwitchboard();
        if (turnOn && bound == null)
        {
            switchboardEnabled = false;   // ApplySwitchboard has said why; the item must not stay ticked
            return Task.FromResult(0);
        }
        TraySettings.SetFlag(root, TraySettings.SwitchboardKey, switchboardEnabled);
        string where = bound == "network" ? " (LAN and Tailscale)."
            : shareOnNetwork ? " (this PC only until allow-switchboard.ps1 has been run)."
            : " (this PC only; tick Share router on the network for other machines).";
        Notify(turnOn
            ? "Front door on port " + Switchboard.Port + where + " A request that names a model starts the engine for it. Token: logs\\switchboard-token."
            : "Front door off.", ToolTipIcon.Info);
        return Task.FromResult(0);
    }

    void WriteSwitchLog(string line)
    {
        try
        {
            lock (switchLogLock)
                File.AppendAllText(Path.Combine(root, "logs", "switchboard.log"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n");
        }
        catch (Exception) { }   // a log that cannot be written never fails a request
    }

    // ---- ISwitchHost: everything below runs on the tray's UI thread, like the menu actions ----

    Task<T> OnUi<T>(Func<Task<T>> work)
    {
        var done = new TaskCompletionSource<T>();
        uiContext.Post(async unused =>
        {
            try { done.SetResult(await work()); }
            catch (Exception ex) { done.SetException(ex); }
        }, null);
        return done.Task;
    }

    // Every router preset, Strata size and ninfer profile with a model id, in that order.
    List<SwitchTarget> BuildSwitchTargets()
    {
        var targets = new List<SwitchTarget>();
        foreach (string id in presetOrder) AddSwitchTarget(targets, "router", id, RouterClient.BaseUrl);
        foreach (StrataSize size in StrataSizes.Where(size => size.ModelId != null)) AddSwitchTarget(targets, "strata", size.ModelId, StrataClient.BaseUrl);
        foreach (NinferModel model in NinferModels) AddSwitchTarget(targets, "ninfer", model.ModelId, NinferClient.BaseUrl);
        return targets;
    }

    // Two engines can report the same id (ninfer's qwen3.8-27b is also a router preset). The later one is listed as
    // "<engine>/<id>"; the switchboard rewrites the request's model back to the engine's own id.
    static void AddSwitchTarget(List<SwitchTarget> targets, string engine, string id, string baseUrl)
    {
        string frontDoorId = targets.Any(t => t.ModelId == id) ? engine + "/" + id : id;
        targets.Add(new SwitchTarget(engine, frontDoorId, baseUrl, id));
    }

    IList<SwitchTarget> ISwitchHost.Targets { get { return switchTargets; } }

    bool IsTargetReady(SwitchTarget target)
    {
        switch (target.Engine)
        {
            case "router":
                return activeModel != null && activeModel.Id == target.UpstreamId && activeModel.Status == "loaded"
                    && strataState != StrataState.Loaded && ninferState == NinferState.Stopped;
            case "strata":
                return strataState == StrataState.Loaded && strata.Model == target.UpstreamId;
            default:
                return ninferState == NinferState.Ready && ninfer.Model == target.UpstreamId;
        }
    }

    // The engine that holds the GPUs now, for a request that names no model.
    Task<SwitchTarget> ISwitchHost.CurrentAsync()
    {
        return OnUi(() =>
        {
            string engine = null, id = null;
            if (ninferState == NinferState.Ready) { engine = "ninfer"; id = ninfer.Model; }
            else if (strataState == StrataState.Loaded) { engine = "strata"; id = strata.Model; }
            else if (activeModel != null) { engine = "router"; id = activeModel.Id; }
            return Task.FromResult(switchTargets.FirstOrDefault(t => t.Engine == engine && t.UpstreamId == id));
        });
    }

    Task<Dictionary<string, object>> ISwitchHost.StatusAsync()
    {
        return OnUi(() => Task.FromResult(EngineStatus()));
    }

    // Each engine's state and model, as the front door's /switchboard/status and the model finder's /status.json
    // report them. Read on the UI thread.
    Dictionary<string, object> EngineStatus()
    {
        return new Dictionary<string, object>
        {
            { "router", new Dictionary<string, object> { { "state", state.ToString() }, { "model", activeModel == null ? null : activeModel.Id }, { "model_status", activeModel == null ? null : activeModel.Status } } },
            { "strata", new Dictionary<string, object> { { "state", strataState.ToString() }, { "model", strata.Model }, { "size", StrataRunningSize() } } },
            { "ninfer", new Dictionary<string, object> { { "state", ninferState.ToString() }, { "model", ninfer.Model } } },
            { "reserve_gpu0", reserveGpu0 },
        };
    }

    // The size serving now, when the tray started it; null after a start by hand, where /health's model id may
    // fit more than one size.
    string StrataRunningSize()
    {
        if (strataStarted == null || strata.Model == null || strata.Model != strataStarted.ModelId) return null;
        return strataStarted.Script;
    }

    // Unloads what holds the GPUs without stopping the servers: ninfer stops, Strata and the router lose their model.
    Task<string> ISwitchHost.FreeAsync()
    {
        return OnUi(async () =>
        {
            string waited = await WaitWhileStrataLoadsAsync();   // else its load finishes on GPUs just reported free
            if (waited != null) return waited;
            if (ninferState != NinferState.Stopped && !ninferStopping) await StopNinferAsync();
            if (strataState == StrataState.Loaded && !await UnloadStrataAsync(null)) return "Could not unload Strata's model.";
            if (activeModel != null)
            {
                string error = await client.UnloadAsync(activeModel.Id);
                if (error != null) return "Could not unload " + activeModel.Id + ": " + error;
            }
            return null;
        });
    }

    Task<string> ISwitchHost.EnsureAsync(SwitchTarget target)
    {
        return OnUi(async () =>
        {
            if (quitting) return "The tray is quitting.";
            if (IsTargetReady(target)) return null;
            Notify("A request asked for " + target.ModelId + "; switching to it.", ToolTipIcon.Info);
            WriteSwitchLog("switching to " + target.Key);
            DateTime began = DateTime.Now;
            string error;
            switch (target.Engine)
            {
                case "router": error = await EnsureRouterAsync(target.UpstreamId); break;
                case "strata": error = await EnsureStrataAsync(StrataSizes.First(s => s.ModelId == target.UpstreamId)); break;
                default: error = await EnsureNinferAsync(NinferModels.First(m => m.ModelId == target.UpstreamId)); break;
            }
            WriteSwitchLog(error == null
                ? target.Key + " ready after " + (int)(DateTime.Now - began).TotalSeconds + " s"
                : target.Key + " failed: " + error);
            return error;
        });
    }

    // Polls once a second (the tray's own poll keeps the state fields current) until `check` returns "" (done), a
    // message (failed) or the time runs out. `check` returns null to keep waiting.
    async Task<string> WaitForAsync(Func<string> check, TimeSpan limit, string timeout)
    {
        DateTime deadline = DateTime.Now + limit;
        while (DateTime.Now < deadline)
        {
            if (quitting) return "The tray is quitting.";
            string verdict = check();
            if (verdict != null) return verdict.Length == 0 ? null : verdict;
            await Task.Delay(1000);
        }
        return timeout;
    }

    Task<string> WaitWhileStrataLoadsAsync()
    {
        return WaitForAsync(() => StrataIsLoading() || strataStopping || strataLaunching ? null : "", SwitchStrataWait,
            "Strata is still loading after " + SwitchStrataWait.TotalMinutes + " minutes.");
    }

    // The router serves `id`. A router that is stopped starts (ninfer stops first); Strata's model unloads; then
    // the router loads the preset, which unloads the router's previous one.
    async Task<string> EnsureRouterAsync(string id)
    {
        if (reserveGpu0)
        {
            // start-router.ps1 wrote the presets it still serves; a missing file means no check.
            List<string> served = ReadPresetOrder(Path.Combine(root, "logs", "presets.run.ini"));
            if (served.Count > 0 && !served.Contains(id)) return "GPU 0 is reserved for ComfyUI in the tray, and " + id + " needs both cards.";
        }
        string waited = await WaitWhileStrataLoadsAsync();
        if (waited != null) return waited;
        if (ninferState != NinferState.Stopped && !ninferStopping) await StopNinferAsync();   // brings the router back if the tray stopped it for ninfer
        DateTime began = DateTime.Now;
        if (state == RouterState.Stopped && !ServerProcesses.RouterRunning()) StartRouter();
        string up = await WaitForAsync(() =>
            state == RouterState.Idle || state == RouterState.Loading || state == RouterState.Loaded ? ""
            : state == RouterState.Stopped && DateTime.Now - began > SwitchLaunchGrace && !ServerProcesses.RouterRunning()
                ? "The router did not start. See logs\\server.log." : null,
            SwitchRouterWait, "The router did not answer in time.");
        if (up != null) return up;

        string problem = await TryLoadAsync(id, null);   // the caller reports it
        if (problem != null) return problem;
        bool seenLoading = false;
        DateTime asked = DateTime.Now;
        return await WaitForAsync(() =>
        {
            ModelInfo model = models.FirstOrDefault(m => m.Id == id);
            string status = model == null ? null : model.Status;
            if (status == "loaded") return "";
            if (status == "loading") { seenLoading = true; return null; }
            if (state == RouterState.Stopped) return "The router stopped while loading " + id + ".";
            if (seenLoading || DateTime.Now - asked > SwitchLaunchGrace) return id + " failed to load. See logs\\server.log.";
            return null;
        }, SwitchRouterWait, id + " did not finish loading in " + SwitchRouterWait.TotalMinutes + " minutes.");
    }

    // Strata serves `size`: starts it, replaces another size, or loads the model if it is unloaded.
    async Task<string> EnsureStrataAsync(StrataSize size)
    {
        if (reserveGpu0) return "GPU 0 is reserved for ComfyUI in the tray, and Strata needs both cards.";
        string waited = await WaitWhileStrataLoadsAsync();
        if (waited != null) return waited;
        DateTime launched = DateTime.Now;
        if (strataState == StrataState.Stopped)
        {
            await StrataStartAsync(size);
        }
        else if (strata.Model != size.ModelId)
        {
            if (await StopStrataAsync(StrataStopFailure) != 0) return StrataStopFailure;
            await StrataStartAsync(size);
        }

        bool loadSent = false;
        DateTime loadSentAt = DateTime.Now;
        while (DateTime.Now - launched < SwitchStrataWait)
        {
            if (quitting) return "The tray is quitting.";
            if (strataState == StrataState.Loaded && strata.Model == size.ModelId) return null;
            if (strataState == StrataState.Unloaded && strata.Model == size.ModelId && !strataLoading && !strataUnloading)
            {
                if (!loadSent)
                {
                    loadSent = true;
                    await StrataLoadAsync(size);
                    loadSentAt = DateTime.Now;
                    continue;
                }
                if (DateTime.Now - loadSentAt > SwitchLaunchGrace) return "Strata did not load its model. See logs\\strata.log.";
            }
            if (strataState == StrataState.Stopped && !strataStopping && !strataLaunching && DateTime.Now - launched > SwitchLaunchGrace)
                return "Strata did not start. See logs\\strata.log.";
            await Task.Delay(1000);
        }
        return "Strata did not load " + size.ModelId + " in " + SwitchStrataWait.TotalMinutes + " minutes.";
    }

    // ninfer serves `model`: starts it (stopping Strata and the router), or replaces the profile that runs.
    async Task<string> EnsureNinferAsync(NinferModel model)
    {
        if (reserveGpu0 && model.BothGpus) return "GPU 0 is reserved for ComfyUI in the tray, and " + model.Name + " needs both cards.";
        string settled = await WaitForAsync(() => ninferStopping || ninferLaunching ? null : "", SwitchNinferWait,
            "ninfer is still busy starting or stopping.");
        if (settled != null) return settled;

        if (ninferState != NinferState.Stopped)
        {
            // Which profile runs is known only once it answers.
            string answering = await WaitForAsync(() => ninferState == NinferState.Starting ? null : "", SwitchNinferWait,
                "ninfer did not finish starting.");
            if (answering != null) return answering;
            if (ninferState == NinferState.Ready && ninfer.Model != model.ModelId)
            {
                string replaced = await ReplaceNinferAsync(model);
                if (replaced != null) return replaced;
            }
        }
        DateTime launched = DateTime.Now;
        if (ninferState == NinferState.Stopped) await NinferStartAsync(model);
        return await WaitForAsync(() =>
            ninferState == NinferState.Ready && ninfer.Model == model.ModelId ? ""
            : ninferState == NinferState.Stopped && !ninferStopping && !ninferLaunching && DateTime.Now - launched > SwitchLaunchGrace
                ? model.Name + " did not start. See logs\\ninfer.log." : null,
            SwitchNinferWait, model.Name + " did not answer in " + SwitchNinferWait.TotalMinutes + " minutes.");
    }

    // Stops the running profile and launches `model`. The router stays down: ninfer is replacing ninfer, so
    // RestoreRouterAfterNinfer must not run in between (ninferStopping holds it off until the new start is set).
    async Task<string> ReplaceNinferAsync(NinferModel model)
    {
        ninferStartRequestedAt = null;
        ninferStopping = true;
        UpdateMenu();
        try
        {
            if (await ninferProcess.StopAsync() != 0)
            {
                Notify("ninfer did not stop cleanly; it may still hold the GPUs. Check Task Manager.", ToolTipIcon.Error);
                return "ninfer did not stop cleanly.";
            }
            ninfer.ForgetModel();
            ninferProcess.Start("-Model " + model.Script);
            ninferStartRequestedAt = DateTime.Now;
            ninferState = NinferState.Starting;
            return null;
        }
        finally
        {
            ninferStopping = false;
            ShowState(state);
        }
    }
}
