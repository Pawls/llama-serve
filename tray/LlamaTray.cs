// Tray front end for the llama.cpp router: shows its state, switches models, starts and stops it.
// Built by install-tray.ps1 with the .NET Framework csc.exe that ships in Windows, so this is C# 5
// (no string interpolation, no ?. operator).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Llama Router")]
[assembly: AssemblyProduct("Llama Router")]
[assembly: AssemblyDescription("Tray front end for the llama.cpp router on 127.0.0.1:8410")]

static class Program
{
    const string InstanceMutexName = @"Local\LlamaRouterTray";
    const string ActivateEventName = @"Local\LlamaRouterTray.Activate";

    [DllImport("user32.dll")]
    static extern bool SetProcessDPIAware();

    [STAThread]
    static void Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--write-icon")
        {
            TrayIcons.WriteIcoFile(args[1]);
            return;
        }

        bool firstInstance;
        using (var instanceMutex = new Mutex(true, InstanceMutexName, out firstInstance))
        using (var activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName))
        {
            if (!firstInstance)
            {
                // Launched again from the Start menu: the running instance announces itself instead.
                activate.Set();
                return;
            }

            string root = FindRepoRoot();
            if (root == null)
            {
                MessageBox.Show("start-router.ps1 was not found two folders above LlamaTray.exe.", "Llama Router",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new TrayApp(root, activate));
        }
    }

    // install-tray.ps1 builds the exe into <repo>\tray\bin.
    static string FindRepoRoot()
    {
        string root = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", ".."));
        return File.Exists(Path.Combine(root, "start-router.ps1")) ? root : null;
    }
}

enum RouterState { Stopped, Starting, Idle, Loading, Loaded, Stopping }

sealed class ModelInfo
{
    public string Id;
    public string Status;   // the router's status.value: "unloaded", "loading", "loaded", ...
}

sealed class RouterClient
{
    public const string BaseUrl = "http://127.0.0.1:8410";

    static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    // A load that replaces a running model unloads it first, before answering.
    static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(60);

    readonly HttpClient http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = Timeout.InfiniteTimeSpan };
    readonly JavaScriptSerializer json = new JavaScriptSerializer();

    // Returns null when the router is not answering.
    public async Task<List<ModelInfo>> GetModelsAsync()
    {
        string body;
        try
        {
            using (var cancel = new CancellationTokenSource(QueryTimeout))
            using (var response = await http.GetAsync("/models", cancel.Token))
            {
                if (!response.IsSuccessStatusCode) return null;
                body = await response.Content.ReadAsStringAsync();
            }
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }

        var models = new List<ModelInfo>();
        var root = (Dictionary<string, object>)json.DeserializeObject(body);
        foreach (Dictionary<string, object> entry in (object[])root["data"])
        {
            var status = (Dictionary<string, object>)entry["status"];
            models.Add(new ModelInfo { Id = (string)entry["id"], Status = (string)status["value"] });
        }
        return models;
    }

    // Both return the router's error message, or null on success. The router answers before the
    // (un)load finishes; GetModelsAsync shows the progress.
    public Task<string> LoadAsync(string model) { return PostModelAsync("/models/load", model); }
    public Task<string> UnloadAsync(string model) { return PostModelAsync("/models/unload", model); }

    async Task<string> PostModelAsync(string path, string model)
    {
        string payload = json.Serialize(new Dictionary<string, string> { { "model", model } });
        try
        {
            using (var cancel = new CancellationTokenSource(CommandTimeout))
            using (var content = new StringContent(payload, Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync(path, content, cancel.Token))
            {
                if (response.IsSuccessStatusCode) return null;
                return ErrorMessage(await response.Content.ReadAsStringAsync(), (int)response.StatusCode);
            }
        }
        catch (HttpRequestException ex) { return ex.Message; }
        catch (TaskCanceledException) { return "The router did not answer in time."; }
    }

    string ErrorMessage(string body, int statusCode)
    {
        try
        {
            var root = (Dictionary<string, object>)json.DeserializeObject(body);
            var error = (Dictionary<string, object>)root["error"];
            return (string)error["message"];
        }
        catch (Exception)
        {
            return "The router answered HTTP " + statusCode + ".";
        }
    }
}

// Strata (Qwen3.8-Flash-Next) runs as its own server beside the router; the router cannot host it.
enum StrataState { Stopped, Starting, Unloaded, Loaded }

sealed class StrataClient
{
    public const string BaseUrl = "http://127.0.0.1:8411";

    static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);
    static readonly TimeSpan UnloadTimeout = TimeSpan.FromSeconds(30);

    readonly HttpClient http = new HttpClient { BaseAddress = new Uri(BaseUrl), Timeout = Timeout.InfiniteTimeSpan };
    readonly JavaScriptSerializer json = new JavaScriptSerializer();

    // /health's "model" from the last answer, e.g. "qwen3.8-flash-next-iq3_xxs"; null before Strata answers.
    public string Model { get; private set; }

    // /health's "loaded", or null when the server is not answering (stopped, or still loading at its start).
    public async Task<bool?> GetLoadedAsync()
    {
        try
        {
            using (var cancel = new CancellationTokenSource(QueryTimeout))
            using (var response = await http.GetAsync("/health", cancel.Token))
            {
                if (!response.IsSuccessStatusCode) return null;
                var root = (Dictionary<string, object>)json.DeserializeObject(await response.Content.ReadAsStringAsync());
                Model = root.ContainsKey("model") ? root["model"] as string : null;
                return root.ContainsKey("loaded") && (bool)root["loaded"];
            }
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }

    // Null when the model is now unloaded; otherwise why not. Strata answers 409 while a request runs.
    // Its control endpoints take JSON only and refuse a foreign Origin, which this client never sends.
    public async Task<string> UnloadAsync()
    {
        try
        {
            using (var cancel = new CancellationTokenSource(UnloadTimeout))
            using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync("/unload", content, cancel.Token))
            {
                if (response.IsSuccessStatusCode) return null;
                return (int)response.StatusCode == 409 ? "Strata is answering a request." : "Strata answered HTTP " + (int)response.StatusCode + ".";
            }
        }
        catch (HttpRequestException ex) { return ex.Message; }
        catch (TaskCanceledException) { return "Strata did not answer in time."; }
    }
}

// Both servers start and stop through the repo's own scripts, so their flags stay in one place.
sealed class ScriptProcess
{
    readonly string root;
    readonly string startScript, stopScript;
    Process launcher;

    public ScriptProcess(string root, string startScript, string stopScript)
    {
        this.root = root;
        this.startScript = startScript;
        this.stopScript = stopScript;
    }

    // The start script launched by this tray, while it is alive.
    public Process Launcher { get { return launcher != null && !launcher.HasExited ? launcher : null; } }

    // `arguments` go to the start script as they are, e.g. "-Model iq3_xxs".
    public void Start(string arguments = "")
    {
        launcher = RunScriptHidden(startScript, arguments);
    }

    public Task StopAsync()
    {
        Process stopper = RunScriptHidden(stopScript, "");
        return Task.Run(() => stopper.WaitForExit());
    }

    // CreateNoWindow gives the console no window at all. pwsh -WindowStyle Hidden does not help here:
    // with Windows Terminal as the default terminal, the window it opens stays visible.
    Process RunScriptHidden(string script, string arguments)
    {
        var startInfo = new ProcessStartInfo("pwsh.exe",
            ("-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, script) + "\" " + arguments).TrimEnd())
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root,
        };
        return Process.Start(startInfo);
    }
}

// tray-settings.json holds the idle-unload choices that start-router.ps1 and start-strata.ps1 read at launch.
// Minutes of idleness before a model frees its VRAM; 0 means never.
static class TraySettings
{
    public const string RouterIdleKey = "RouterIdleMinutes";
    public const string StrataIdleKey = "StrataIdleMinutes";

    public static int GetMinutes(string root, string key, int fallback)
    {
        Dictionary<string, object> saved = Load(root);
        return saved.ContainsKey(key) ? Convert.ToInt32(saved[key]) : fallback;
    }

    public static void SetMinutes(string root, string key, int minutes)
    {
        Dictionary<string, object> saved = Load(root);
        saved[key] = minutes;
        File.WriteAllText(PathOf(root), new JavaScriptSerializer().Serialize(saved));
    }

    static Dictionary<string, object> Load(string root)
    {
        string path = PathOf(root);
        if (!File.Exists(path)) return new Dictionary<string, object>();
        return new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(path));
    }

    static string PathOf(string root)
    {
        return Path.Combine(root, "tray-settings.json");
    }
}

// One row of `memory-saver.ps1 -Mode List`.
sealed class SaverCandidate
{
    public string Id, Kind, Name, Cost;
    public int PrivateMB;
    public bool CanClose, Chosen;
}

// memory-saver.ps1 closes the background apps picked in memory-saver.choices.json to leave Strata more RAM, and
// reopens them. Its state file says whether it is on, so a switch made from a terminal shows in the menu too.
static class MemorySaver
{
    [DllImport("kernel32.dll")]
    static extern ulong GetTickCount64();

    // A state file from before this boot counts as off: Windows' startup has relaunched those apps.
    // memory-saver.ps1 applies the same rule and removes such a file on its next run.
    public static bool IsOn(string root)
    {
        var state = new FileInfo(Path.Combine(root, "logs", "memory-saver.json"));
        DateTime bootedAt = DateTime.Now - TimeSpan.FromMilliseconds(GetTickCount64());
        return state.Exists && state.LastWriteTime > bootedAt;
    }

    public static async Task<List<SaverCandidate>> ListAsync(string root)
    {
        string json = await RunAsync(root, "List");
        var rows = new JavaScriptSerializer().Deserialize<List<Dictionary<string, object>>>(json);
        return rows.Select(row => new SaverCandidate
        {
            Id = (string)row["Id"],
            Kind = (string)row["Kind"],
            Name = (string)row["Name"],
            Cost = (string)row["Cost"],
            PrivateMB = Convert.ToInt32(row["PrivateMB"]),
            CanClose = (bool)row["CanClose"],
            Chosen = (bool)row["Chosen"],
        }).ToList();
    }

    // Keeps the choices for apps that were not running, so they are not forgotten while closed.
    public static void SaveChoices(string root, IEnumerable<string> shownIds, IEnumerable<string> chosenIds)
    {
        string path = Path.Combine(root, "memory-saver.choices.json");
        var serializer = new JavaScriptSerializer();
        var kept = new List<string>();
        if (File.Exists(path))
        {
            var saved = serializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(path));
            List<string> previous;
            if (saved != null && saved.TryGetValue("Close", out previous)) kept.AddRange(previous.Except(shownIds));
        }
        kept.AddRange(chosenIds);
        File.WriteAllText(path, serializer.Serialize(new Dictionary<string, object> { { "Close", kept } }));
    }

    // Runs -Mode On, Reapply or Off. Returns the script's summary line; throws with its error when it fails.
    public static async Task<string> SwitchAsync(string root, string mode)
    {
        return LastLine(await RunAsync(root, mode));
    }

    static Task<string> RunAsync(string root, string mode)
    {
        var startInfo = new ProcessStartInfo("pwsh.exe",
            "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(root, "memory-saver.ps1") + "\" -Mode " + mode)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        return Task.Run(() =>
        {
            using (Process process = Process.Start(startInfo))
            {
                Task<string> errors = process.StandardError.ReadToEndAsync();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0) throw new InvalidOperationException(LastLine(errors.Result) ?? "memory-saver.ps1 failed.");
                return output;
            }
        });
    }

    static string LastLine(string text)
    {
        return text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(line => line.Trim()).LastOrDefault();
    }
}

// The checklist shown before the memory saver closes anything. VS Code extensions and services are listed for
// their RAM only: the script cannot free them, so their rows cannot be ticked.
sealed class MemorySaverPicker : Form
{
    static readonly string[] GroupTitles =
    {
        "known", "Apps with a tested close and reopen",
        "other", "Other apps (ended like Task Manager's End task if they do not close on request)",
        "extension", "VS Code extensions (information only: free them in VS Code)",
        "service", "Services (information only: stopping one needs admin)",
    };

    readonly ListView list;
    readonly Label checkedLabel;

    public MemorySaverPicker(List<SaverCandidate> candidates)
    {
        Text = "Memory saver";
        Font = SystemFonts.MessageBoxFont;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(1000, 560);

        var intro = new Label
        {
            Dock = DockStyle.Top,
            AutoSize = false,
            Height = Font.Height * 3,
            Padding = new Padding(8, 8, 8, 0),
            Text = "Tick the apps to close while Strata runs. Your ticks are remembered. Click the menu item again to reopen them.\n"
                + "RAM is private memory per app. Free RAM now: " + FreeRamText() + ". Hover a row for what you lose while it is closed.",
        };

        list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            CheckBoxes = true,
            FullRowSelect = true,
            ShowItemToolTips = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
        };
        list.Columns.Add("App", 300);
        list.Columns.Add("RAM", 90, HorizontalAlignment.Right);
        list.Columns.Add("While closed", 580);
        AddRows(candidates);
        list.ItemCheck += (s, e) => { if (!((SaverCandidate)list.Items[e.Index].Tag).CanClose) e.NewValue = e.CurrentValue; };
        list.ItemChecked += (s, e) => UpdateCheckedLabel();

        var closeButton = new Button { Text = "Close checked apps", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        checkedLabel = new Label { AutoSize = true, Margin = new Padding(3, 8, 24, 3) };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8),
        };
        buttons.Controls.AddRange(new Control[] { cancelButton, closeButton, checkedLabel });

        Controls.Add(list);
        Controls.Add(intro);
        Controls.Add(buttons);
        AcceptButton = closeButton;
        CancelButton = cancelButton;
        UpdateCheckedLabel();
    }

    public List<string> ShownIds()
    {
        return list.Items.Cast<ListViewItem>().Select(item => ((SaverCandidate)item.Tag).Id).ToList();
    }

    public List<string> CheckedIds()
    {
        return list.CheckedItems.Cast<ListViewItem>().Select(item => ((SaverCandidate)item.Tag).Id).ToList();
    }

    void AddRows(List<SaverCandidate> candidates)
    {
        for (int i = 0; i < GroupTitles.Length; i += 2)
        {
            string kind = GroupTitles[i];
            var group = new ListViewGroup(kind, GroupTitles[i + 1]);
            list.Groups.Add(group);
            foreach (SaverCandidate candidate in candidates.Where(c => c.Kind == kind).OrderByDescending(c => c.PrivateMB))
            {
                var item = new ListViewItem(new[] { candidate.Name, candidate.PrivateMB.ToString("N0") + " MB", candidate.Cost }, group)
                {
                    Tag = candidate,
                    Checked = candidate.CanClose && candidate.Chosen,
                    ToolTipText = candidate.Cost,
                };
                if (!candidate.CanClose) item.ForeColor = SystemColors.GrayText;
                list.Items.Add(item);
            }
        }
    }

    void UpdateCheckedLabel()
    {
        int total = list.CheckedItems.Cast<ListViewItem>().Sum(item => ((SaverCandidate)item.Tag).PrivateMB);
        checkedLabel.Text = "Checked: " + (total / 1024.0).ToString("0.0") + " GB";
    }

    static string FreeRamText()
    {
        using (var counter = new PerformanceCounter("Memory", "Available MBytes"))
            return (counter.NextValue() / 1024).ToString("0.0") + " GB";
    }
}

static class RouterProcess
{
    const string LlamaCppDir = @"F:\AI\llama.cpp\";   // same filter as stop-router.ps1

    // True when a router or model process is up, even if the router is not answering HTTP yet.
    public static bool AnyServerRunning()
    {
        foreach (Process process in Process.GetProcessesByName("llama-server"))
        {
            try
            {
                if (process.MainModule.FileName.StartsWith(LlamaCppDir, StringComparison.OrdinalIgnoreCase)) return true;
            }
            catch (Exception) { }   // exited meanwhile, or not ours to inspect
            finally { process.Dispose(); }
        }
        return false;
    }
}

// The logon task that starts this tray; the menu's "Start with Windows" toggles it.
static class LogonTask
{
    const string TaskName = "llama-router";

    // Null when the task does not exist.
    public static bool? IsEnabled()
    {
        try { return (bool)Find().Enabled; }
        catch (Exception) { return null; }
    }

    public static void SetEnabled(bool enabled)
    {
        Find().Enabled = enabled;
    }

    static dynamic Find()
    {
        dynamic scheduler = Activator.CreateInstance(Type.GetTypeFromProgID("Schedule.Service"));
        scheduler.Connect();
        return scheduler.GetFolder(@"\").GetTask(TaskName);
    }
}

static class Gpus
{
    // One line like "GPU 0  3.4 / 24.0 GB   GPU 1  0.0 / 24.0 GB", or null when nvidia-smi fails.
    public static string QueryMemory()
    {
        var startInfo = new ProcessStartInfo("nvidia-smi.exe",
            "--query-gpu=index,memory.used,memory.total --format=csv,noheader,nounits")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
        };
        try
        {
            using (Process process = Process.Start(startInfo))
            {
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                var parts = new List<string>();
                foreach (string line in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] fields = line.Split(',');
                    double usedGb = double.Parse(fields[1].Trim()) / 1024;
                    double totalGb = double.Parse(fields[2].Trim()) / 1024;
                    parts.Add(string.Format("GPU {0}  {1:0.0} / {2:0.0} GB", fields[0].Trim(), usedGb, totalGb));
                }
                return parts.Count > 0 ? string.Join("   ", parts) : null;
            }
        }
        catch (Exception)
        {
            return null;
        }
    }
}

sealed class TrayApp : ApplicationContext
{
    const string AppName = "Llama Router";
    const int PollIntervalMs = 2000;
    // start-router.ps1 needs a few seconds before the router answers HTTP.
    static readonly TimeSpan StartGracePeriod = TimeSpan.FromSeconds(60);
    // Strata answers HTTP only once its model is in RAM: 1-3 minutes, longer on a cold file cache.
    static readonly TimeSpan StrataStartGracePeriod = TimeSpan.FromMinutes(10);

    static readonly MethodInfo ShowContextMenu =
        typeof(NotifyIcon).GetMethod("ShowContextMenu", BindingFlags.Instance | BindingFlags.NonPublic);

    readonly string root;
    readonly RouterClient client = new RouterClient();
    readonly ScriptProcess routerProcess;
    readonly StrataClient strata = new StrataClient();
    readonly ScriptProcess strataProcess;
    readonly List<string> presetOrder;
    readonly Dictionary<RouterState, Icon> stateIcons = new Dictionary<RouterState, Icon>();
    readonly NotifyIcon tray;
    readonly ContextMenuStrip menu;
    readonly System.Windows.Forms.Timer pollTimer;
    readonly SynchronizationContext uiContext;

    ToolStripMenuItem statusItem, gpuItem, modelsMenu, unloadItem, chatItem;
    ToolStripMenuItem startStopItem, restartItem, autostartItem;
    ToolStripMenuItem strataStatusItem, strataStartStopItem, strataStartOtherItem, strataUnloadItem, strataAppItem;
    ToolStripMenuItem memorySaverItem, memorySaverReapplyItem, routerIdleMenu, strataIdleMenu;
    bool memorySaverSwitching;

    // start-strata.ps1's -Model values and their menu labels; the first is also the script's default.
    static readonly string[,] StrataSizes =
    {
        { "iq3_xxs", "IQ3_XXS (more accurate)" },
        { "iq2_xs", "IQ2_XS (faster, more RAM free)" },
    };

    StrataState strataState = StrataState.Stopped;
    DateTime? strataStartRequestedAt;   // set while the tray waits for a Strata it started
    bool strataUnloading;               // an unload the tray sent is in flight
    string strataYieldedTo;             // the router model Strata was last unloaded for

    RouterState state = RouterState.Stopped;
    List<ModelInfo> models = new List<ModelInfo>();
    ModelInfo activeModel;          // the model that is loaded, loading or unloading
    DateTime? startRequestedAt;     // set while the tray waits for a router it started
    bool stopping;
    bool quitting;
    bool refreshing;

    // A load asked for from this menu, followed until it succeeds or fails.
    string requestedModel;
    bool requestedModelSeenLoading;
    DateTime requestedAt;

    public TrayApp(string root, EventWaitHandle activate)
    {
        this.root = root;
        routerProcess = new ScriptProcess(root, "start-router.ps1", "stop-router.ps1");
        strataProcess = new ScriptProcess(root, "start-strata.ps1", "stop-strata.ps1");
        presetOrder = ReadPresetOrder(Path.Combine(root, "presets.ini"));

        int iconSize = SystemInformation.SmallIconSize.Width;
        foreach (RouterState each in Enum.GetValues(typeof(RouterState)))
            stateIcons[each] = TrayIcons.CreateIcon(iconSize, TrayIcons.ColorFor(each));

        menu = BuildMenu();
        uiContext = SynchronizationContext.Current;   // installed by the first control, the menu

        tray = new NotifyIcon { ContextMenuStrip = menu, Icon = stateIcons[state], Text = AppName, Visible = true };
        tray.MouseUp += (sender, e) => { if (e.Button == MouseButtons.Left) ShowContextMenu.Invoke(tray, null); };

        ThreadPool.RegisterWaitForSingleObject(activate,
            (ignored, timedOut) => uiContext.Post(unused => AnnounceAlreadyRunning(), null), null, -1, false);

        pollTimer = new System.Windows.Forms.Timer { Interval = PollIntervalMs };
        pollTimer.Tick += async (sender, e) => await RefreshAsync();

        StartUp();
    }

    async void StartUp()
    {
        List<ModelInfo> current = await client.GetModelsAsync();
        // Adopt a router that is already up (started in a window, or left behind by a crashed tray).
        if (current == null && !RouterProcess.AnyServerRunning()) StartRouter();
        await RefreshAsync();
        pollTimer.Start();
    }

    // ---- Menu ----

    ContextMenuStrip BuildMenu()
    {
        var strip = new ContextMenuStrip();

        statusItem = new ToolStripMenuItem { Enabled = false };
        statusItem.Font = new Font(statusItem.Font, FontStyle.Bold);
        gpuItem = new ToolStripMenuItem("GPU memory...") { Enabled = false };

        modelsMenu = new ToolStripMenuItem("Load model");
        unloadItem = new ToolStripMenuItem("Unload model (free GPUs)", null, (s, e) => RunAction(UnloadAsync));

        chatItem = new ToolStripMenuItem("Open chat in browser", null, (s, e) => OpenPath(RouterClient.BaseUrl + "/"));
        var logItem = new ToolStripMenuItem("Open server log", null, (s, e) => OpenPath(Path.Combine(root, "logs", "server.log")));
        var presetsItem = new ToolStripMenuItem("Edit presets.ini", null, (s, e) => OpenPath(Path.Combine(root, "presets.ini")));
        var folderItem = new ToolStripMenuItem("Open llama-serve folder", null, (s, e) => OpenPath(root));

        startStopItem = new ToolStripMenuItem("Start router", null, (s, e) => RunAction(StartOrStopAsync));
        restartItem = new ToolStripMenuItem("Restart router (reload presets.ini)", null, (s, e) => RunAction(RestartAsync));

        strataStatusItem = new ToolStripMenuItem { Enabled = false };
        strataStartStopItem = new ToolStripMenuItem("Start Strata", null, (s, e) => RunAction(() => StrataStartOrStopAsync(StrataSizes[0, 0])));
        strataStartOtherItem = new ToolStripMenuItem("Start Strata", null, (s, e) => RunAction(() => StrataStartOrStopAsync(StrataSizes[1, 0])));
        strataUnloadItem = new ToolStripMenuItem("Unload Strata model (free GPUs)", null, (s, e) => RunAction(() => UnloadStrataAsync(null)));
        strataAppItem = new ToolStripMenuItem("Open Strata app", null, (s, e) => OpenPath(StrataClient.BaseUrl + "/"));
        var strataLogItem = new ToolStripMenuItem("Open Strata log", null, (s, e) => OpenPath(Path.Combine(root, "logs", "strata.log")));
        memorySaverItem = new ToolStripMenuItem("Memory saver", null, (s, e) => RunAction(ToggleMemorySaverAsync));
        memorySaverReapplyItem = new ToolStripMenuItem("Memory saver: close apps again...", null,
            (s, e) => RunAction(ReapplyMemorySaverAsync));
        routerIdleMenu = BuildIdleMenu("Router models: free GPUs when idle", TraySettings.RouterIdleKey, 0, "router");
        strataIdleMenu = BuildIdleMenu("Strata: unload when idle", TraySettings.StrataIdleKey, 15, "Strata");

        autostartItem = new ToolStripMenuItem("Start with Windows", null, (s, e) => ToggleAutostart());
        var quitItem = new ToolStripMenuItem("Quit (stops the router and Strata)", null, (s, e) => Quit());

        strip.Items.AddRange(new ToolStripItem[]
        {
            statusItem, gpuItem,
            new ToolStripSeparator(),
            modelsMenu, unloadItem,
            new ToolStripSeparator(),
            chatItem, logItem, presetsItem, folderItem,
            new ToolStripSeparator(),
            startStopItem, restartItem,
            new ToolStripSeparator(),
            strataStatusItem, strataStartStopItem, strataStartOtherItem, strataUnloadItem, strataAppItem, strataLogItem, memorySaverItem,
            memorySaverReapplyItem,
            new ToolStripSeparator(),
            routerIdleMenu, strataIdleMenu,
            new ToolStripSeparator(),
            autostartItem, quitItem,
        });
        strip.Opening += (s, e) => OnMenuOpening();
        UpdateMenu();
        return strip;
    }

    static readonly int[] IdleChoicesMinutes = { 0, 15, 30, 60, 120, 240 };

    // A submenu of idle times. The choice is saved at once and used the next time the server starts.
    ToolStripMenuItem BuildIdleMenu(string title, string key, int defaultMinutes, string serverName)
    {
        var parent = new ToolStripMenuItem(title) { Tag = new KeyValuePair<string, int>(key, defaultMinutes) };
        foreach (int minutes in IdleChoicesMinutes)
        {
            int choice = minutes;
            parent.DropDownItems.Add(new ToolStripMenuItem(IdleLabel(choice), null, (s, e) =>
            {
                TraySettings.SetMinutes(root, key, choice);
                MarkIdleChoice(parent);
                Notify("Saved. The " + serverName + " uses \"" + IdleLabel(choice) + "\" from its next start.", ToolTipIcon.Info);
            }) { Tag = choice });
        }
        MarkIdleChoice(parent);
        return parent;
    }

    static string IdleLabel(int minutes)
    {
        if (minutes == 0) return "Never";
        return minutes < 60 ? minutes + " minutes" : minutes / 60 + (minutes == 60 ? " hour" : " hours");
    }

    void MarkIdleChoice(ToolStripMenuItem parent)
    {
        var setting = (KeyValuePair<string, int>)parent.Tag;
        int current = TraySettings.GetMinutes(root, setting.Key, setting.Value);
        foreach (ToolStripMenuItem item in parent.DropDownItems)
            item.Checked = (int)item.Tag == current;
    }

    async void OnMenuOpening()
    {
        MarkIdleChoice(routerIdleMenu);
        MarkIdleChoice(strataIdleMenu);
        bool? autostart = LogonTask.IsEnabled();
        autostartItem.Visible = autostart.HasValue;
        autostartItem.Checked = autostart == true;

        gpuItem.Text = "GPU memory...";
        string gpus = await Task.Run(() => Gpus.QueryMemory());
        gpuItem.Text = gpus ?? "GPU memory unavailable (nvidia-smi failed)";
    }

    void UpdateMenu()
    {
        bool answering = state == RouterState.Idle || state == RouterState.Loading || state == RouterState.Loaded;

        statusItem.Text = StatusText();
        RebuildModelItems();
        modelsMenu.Enabled = answering;
        unloadItem.Enabled = answering && activeModel != null;
        chatItem.Enabled = answering;

        startStopItem.Text = state == RouterState.Stopped ? "Start router" : "Stop router";
        startStopItem.Enabled = state != RouterState.Stopping;
        restartItem.Enabled = answering;

        strataStatusItem.Text = StrataStatusText();
        bool strataStopped = strataState == StrataState.Stopped;
        strataStartStopItem.Text = strataStopped ? "Start Strata: Flash-Next " + StrataSizes[0, 1] : "Stop Strata";
        strataStartOtherItem.Text = "Start Strata: Flash-Next " + StrataSizes[1, 1];
        strataStartOtherItem.Visible = strataStopped;
        strataUnloadItem.Enabled = strataState == StrataState.Loaded && !strataUnloading;
        strataAppItem.Enabled = strataState == StrataState.Loaded || strataState == StrataState.Unloaded;

        bool saverOn = MemorySaver.IsOn(root);
        memorySaverItem.Checked = saverOn;
        memorySaverItem.Enabled = !memorySaverSwitching;
        if (!memorySaverSwitching)
            memorySaverItem.Text = saverOn ? "Memory saver on: reopen the closed apps" : "Memory saver: close apps to free RAM...";
        memorySaverReapplyItem.Visible = saverOn;
        memorySaverReapplyItem.Enabled = !memorySaverSwitching;
    }

    string StrataStatusText()
    {
        switch (strataState)
        {
            case StrataState.Stopped: return "Strata stopped";
            case StrataState.Starting: return "Strata starting (1-3 min)...";
            case StrataState.Unloaded: return "Strata ready, model unloaded (loads on use)";
            default: return "Strata loaded: " + (strata.Model ?? "Qwen3.8-Flash-Next");
        }
    }

    void RebuildModelItems()
    {
        List<string> ids = models.Select(m => m.Id).ToList();
        bool sameList = modelsMenu.DropDownItems.Count == ids.Count
            && modelsMenu.DropDownItems.Cast<ToolStripItem>().Select(item => (string)item.Tag).SequenceEqual(ids);
        if (!sameList)
        {
            modelsMenu.DropDownItems.Clear();
            foreach (string id in ids)
            {
                string target = id;
                var item = new ToolStripMenuItem(id, null, (s, e) => RunAction(() => LoadAsync(target))) { Tag = id };
                modelsMenu.DropDownItems.Add(item);
            }
        }

        foreach (ToolStripMenuItem item in modelsMenu.DropDownItems)
        {
            string id = (string)item.Tag;
            bool active = activeModel != null && activeModel.Id == id;
            item.Checked = active;
            item.Text = active && activeModel.Status != "loaded" ? id + "  (" + activeModel.Status + "...)" : id;
            ModelInfo info = models.First(m => m.Id == id);
            if (info.Status == "sleeping") item.Text = id + "  (asleep, wakes on use)";
        }
    }

    string StatusText()
    {
        switch (state)
        {
            case RouterState.Stopped: return "Router stopped";
            case RouterState.Starting: return "Router starting...";
            case RouterState.Stopping: return "Router stopping...";
            case RouterState.Idle: return "Ready, no model loaded";
            case RouterState.Loading: return activeModel.Id + " " + activeModel.Status + "...";
            default: return activeModel.Id + " loaded";
        }
    }

    // ---- State ----

    async Task RefreshAsync()
    {
        if (refreshing || quitting) return;
        refreshing = true;
        try
        {
            Task<List<ModelInfo>> routerQuery = client.GetModelsAsync();
            Task<bool?> strataQuery = strata.GetLoadedAsync();
            List<ModelInfo> latest = await routerQuery;
            bool? strataLoaded = await strataQuery;
            if (quitting) return;
            strataState = ClassifyStrata(strataLoaded);
            Apply(latest);
            KeepGpusExclusive();
        }
        finally
        {
            refreshing = false;
        }
    }

    void Apply(List<ModelInfo> latest)
    {
        RouterState previous = state;
        if (latest != null)
        {
            startRequestedAt = null;
            models = SortByPresetOrder(latest);
        }
        activeModel = latest == null ? null : latest.FirstOrDefault(m => m.Status != "unloaded" && m.Status != "sleeping");   // asleep = VRAM already free
        state = Classify(latest);

        FollowRequestedLoad();
        bool wasAnswering = previous == RouterState.Idle || previous == RouterState.Loading || previous == RouterState.Loaded;
        if (wasAnswering && state == RouterState.Stopped)
            Notify("The router stopped. Start it again from this menu.", ToolTipIcon.Warning);

        ShowState(state);
    }

    void ShowState(RouterState newState)
    {
        state = newState;
        // With no router model on the GPUs, the icon and tooltip follow Strata.
        bool routerHoldsGpus = state == RouterState.Loading || state == RouterState.Loaded;
        RouterState iconState = state;
        string tooltip = StatusText();
        if (!routerHoldsGpus && strataState == StrataState.Loaded)
        {
            iconState = RouterState.Loaded;
            tooltip = StrataStatusText();
        }
        else if (!routerHoldsGpus && strataState == StrataState.Starting)
        {
            iconState = RouterState.Loading;
            tooltip = StrataStatusText();
        }
        tray.Icon = stateIcons[iconState];
        tray.Text = TrimTooltip(AppName + ": " + tooltip);
        UpdateMenu();
    }

    StrataState ClassifyStrata(bool? loaded)
    {
        if (loaded.HasValue)
        {
            strataStartRequestedAt = null;
            return loaded.Value ? StrataState.Loaded : StrataState.Unloaded;
        }
        if (!strataStartRequestedAt.HasValue) return StrataState.Stopped;

        bool launcherAlive = strataProcess.Launcher != null;
        bool withinGrace = DateTime.Now - strataStartRequestedAt.Value < StrataStartGracePeriod;
        if (launcherAlive && withinGrace) return StrataState.Starting;
        strataStartRequestedAt = null;
        Notify("Strata did not start. Open the Strata log for details.", ToolTipIcon.Error);
        return StrataState.Stopped;
    }

    // The router has no hook before it loads a model, so a load Pi asks the router for while Strata holds the
    // GPUs is caught here, on the next poll. Strata's own reloads unload the router first (its before_load).
    void KeepGpusExclusive()
    {
        if (activeModel == null)
        {
            strataYieldedTo = null;
            return;
        }
        // Once per router load: a Strata busy answering refuses (409), and retrying every poll only repeats the toast.
        if (strataState != StrataState.Loaded || strataUnloading || strataYieldedTo == activeModel.Id) return;
        string routerModel = activeModel.Id;
        strataYieldedTo = routerModel;
        RunAction(() => UnloadStrataAsync(routerModel + " is loading in the router"));
    }

    RouterState Classify(List<ModelInfo> latest)
    {
        if (stopping) return RouterState.Stopping;
        if (latest == null) return IsStarting() ? RouterState.Starting : RouterState.Stopped;
        if (activeModel == null) return RouterState.Idle;
        return activeModel.Status == "loaded" ? RouterState.Loaded : RouterState.Loading;
    }

    bool IsStarting()
    {
        if (startRequestedAt.HasValue)
        {
            bool launcherAlive = routerProcess.Launcher != null;
            bool withinGrace = DateTime.Now - startRequestedAt.Value < StartGracePeriod;
            if (launcherAlive && withinGrace) return true;
            startRequestedAt = null;
            Notify("The router did not start. Open the server log for details.", ToolTipIcon.Error);
            return false;
        }
        return RouterProcess.AnyServerRunning();
    }

    void FollowRequestedLoad()
    {
        if (requestedModel == null) return;
        ModelInfo requested = models.FirstOrDefault(m => m.Id == requestedModel);
        string status = requested == null ? null : requested.Status;

        if (state == RouterState.Stopped || state == RouterState.Stopping)
        {
            requestedModel = null;
        }
        else if (status == "loaded")
        {
            int seconds = (int)(DateTime.Now - requestedAt).TotalSeconds;
            Notify(requestedModel + " is ready (" + seconds + " s).", ToolTipIcon.Info);
            requestedModel = null;
        }
        else if (status == "loading")
        {
            requestedModelSeenLoading = true;
        }
        else if (activeModel != null)
        {
            requestedModel = null;   // something else (Pi) asked for another model meanwhile
        }
        else if (requestedModelSeenLoading)
        {
            Notify(requestedModel + " failed to load. Open the server log for details.", ToolTipIcon.Error);
            requestedModel = null;
        }
    }

    List<ModelInfo> SortByPresetOrder(List<ModelInfo> latest)
    {
        return latest
            .OrderBy(m => presetOrder.Contains(m.Id) ? presetOrder.IndexOf(m.Id) : int.MaxValue)
            .ThenBy(m => m.Id, StringComparer.Ordinal)
            .ToList();
    }

    // Section order in presets.ini groups the lanes by model family, default first.
    static List<string> ReadPresetOrder(string presetsPath)
    {
        var order = new List<string>();
        if (!File.Exists(presetsPath)) return order;
        foreach (string line in File.ReadAllLines(presetsPath))
        {
            Match section = Regex.Match(line, @"^\s*\[(.+)\]\s*$");
            if (section.Success && section.Groups[1].Value != "*") order.Add(section.Groups[1].Value);
        }
        return order;
    }

    // ---- Actions ----

    async void RunAction(Func<Task> action)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            Notify(ex.Message, ToolTipIcon.Error);
        }
        await RefreshAsync();
    }

    async Task LoadAsync(string id)
    {
        if (activeModel != null && activeModel.Id == id) return;
        if (strataState == StrataState.Loaded && !await UnloadStrataAsync(id + " was asked for")) return;
        requestedModel = id;
        requestedModelSeenLoading = false;
        requestedAt = DateTime.Now;
        string error = await client.LoadAsync(id);
        if (error == null) return;
        requestedModel = null;
        Notify("Could not load " + id + ": " + error, ToolTipIcon.Error);
    }

    async Task UnloadAsync()
    {
        if (activeModel == null) return;
        string error = await client.UnloadAsync(activeModel.Id);
        if (error != null) Notify("Could not unload " + activeModel.Id + ": " + error, ToolTipIcon.Error);
    }

    Task StartOrStopAsync()
    {
        if (state != RouterState.Stopped) return StopRouterAsync();
        StartRouter();
        return Task.FromResult(0);
    }

    async Task RestartAsync()
    {
        await StopRouterAsync();
        StartRouter();
    }

    void StartRouter()
    {
        routerProcess.Start();
        startRequestedAt = DateTime.Now;
        ShowState(RouterState.Starting);
    }

    async Task StopRouterAsync()
    {
        stopping = true;
        activeModel = null;
        ShowState(RouterState.Stopping);
        try
        {
            await routerProcess.StopAsync();
        }
        finally
        {
            stopping = false;
            startRequestedAt = null;
        }
        ShowState(RouterState.Stopped);   // before the next poll, so Apply does not report a crash
    }

    // While stopped, starts `size` (one of StrataSizes); otherwise stops Strata, whichever size runs.
    async Task StrataStartOrStopAsync(string size)
    {
        if (strataState == StrataState.Stopped)
        {
            // start-strata.ps1 unloads the router's model before Strata takes the GPUs.
            strataProcess.Start("-Model " + size);
            strataStartRequestedAt = DateTime.Now;
            strataState = StrataState.Starting;
            ShowState(state);
            return;
        }
        strataStartRequestedAt = null;
        await strataProcess.StopAsync();
        strataState = StrataState.Stopped;
        ShowState(state);
    }

    // `reason` names what needs the GPUs, for the notification; null when the user asked from the menu.
    // Returns whether the model is unloaded now.
    async Task<bool> UnloadStrataAsync(string reason)
    {
        strataUnloading = true;
        UpdateMenu();
        string error;
        try
        {
            error = await strata.UnloadAsync();
        }
        finally
        {
            strataUnloading = false;
        }
        if (error != null)
        {
            Notify("Could not unload Strata's model: " + error, ToolTipIcon.Error);
            return false;
        }
        strataState = StrataState.Unloaded;
        ShowState(state);
        if (reason != null) Notify("Unloaded Strata's model: " + reason + ".", ToolTipIcon.Info);
        return true;
    }

    // Off: measure the candidates, let the user pick, close the picks. On: reopen what was closed.
    Task ToggleMemorySaverAsync()
    {
        return MemorySaver.IsOn(root)
            ? SwitchMemorySaverAsync("Off", "Memory saver: reopening apps (Docker takes about a minute)...", null)
            : SwitchMemorySaverAsync("On", "Memory saver: closing apps...", PickAppsToCloseAsync);
    }

    // While on: the same checklist, showing what runs now, then close the ticked apps that run again.
    // Off still reopens everything closed by either pass, once.
    Task ReapplyMemorySaverAsync()
    {
        return SwitchMemorySaverAsync("Reapply", "Memory saver: closing apps...", PickAppsToCloseAsync);
    }

    // False when the user cancels the checklist.
    async Task<bool> PickAppsToCloseAsync()
    {
        ShowMemorySaverProgress("Memory saver: measuring apps...");
        List<SaverCandidate> candidates = await MemorySaver.ListAsync(root);
        using (var picker = new MemorySaverPicker(candidates))
        {
            if (picker.ShowDialog() != DialogResult.OK) return false;
            MemorySaver.SaveChoices(root, picker.ShownIds(), picker.CheckedIds());
            return true;
        }
    }

    // `confirm` runs first and can cancel; null goes straight to the script.
    async Task SwitchMemorySaverAsync(string mode, string progress, Func<Task<bool>> confirm)
    {
        memorySaverSwitching = true;
        try
        {
            if (confirm != null && !await confirm()) return;
            ShowMemorySaverProgress(progress);
            string summary = await MemorySaver.SwitchAsync(root, mode);
            if (summary != null) Notify(summary, ToolTipIcon.Info);
        }
        finally
        {
            memorySaverSwitching = false;
            UpdateMenu();
        }
    }

    void ShowMemorySaverProgress(string text)
    {
        memorySaverItem.Text = text;
        UpdateMenu();
    }

    void ToggleAutostart()
    {
        try
        {
            LogonTask.SetEnabled(!autostartItem.Checked);
        }
        catch (Exception ex)
        {
            Notify("Could not change the llama-router logon task: " + ex.Message, ToolTipIcon.Error);
        }
    }

    async void Quit()
    {
        pollTimer.Stop();
        quitting = true;
        tray.Visible = false;
        try
        {
            await Task.WhenAll(routerProcess.StopAsync(), strataProcess.StopAsync());
        }
        catch (Exception) { }   // exiting either way; the stop scripts can finish the job
        tray.Dispose();
        ExitThread();
    }

    void AnnounceAlreadyRunning()
    {
        Notify("Already running in the system tray. " + StatusText() + ".", ToolTipIcon.Info);
    }

    void Notify(string text, ToolTipIcon icon)
    {
        if (tray != null && tray.Visible) tray.ShowBalloonTip(5000, AppName, text, icon);
    }

    static void OpenPath(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show("Could not open " + target + ":\n" + ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    // NotifyIcon.Text rejects more than 63 characters.
    static string TrimTooltip(string text)
    {
        return text.Length <= 63 ? text : text.Substring(0, 60) + "...";
    }
}

// A white llama head on a rounded tile whose color shows the router state.
static class TrayIcons
{
    // Llama head in profile, facing right, in a 100 x 100 box; the neck runs off the bottom edge.
    static readonly PointF[] LlamaOutline =
    {
        new PointF(28, 100), new PointF(28, 58), new PointF(27, 40), new PointF(26, 26),
        new PointF(24, 6), new PointF(28, 0), new PointF(33, 8), new PointF(35, 20),
        new PointF(40, 21), new PointF(42, 5), new PointF(46, 0), new PointF(49, 8),
        new PointF(48, 22), new PointF(56, 26), new PointF(72, 30), new PointF(80, 34),
        new PointF(82, 40), new PointF(79, 46), new PointF(66, 48), new PointF(55, 50),
        new PointF(50, 56), new PointF(50, 100),
    };

    static readonly int[] IcoSizes = { 16, 20, 24, 32, 40, 48, 64, 256 };

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public static Color ColorFor(RouterState state)
    {
        switch (state)
        {
            case RouterState.Stopped: return Color.FromArgb(0x6B, 0x72, 0x80);   // grey
            case RouterState.Idle: return Color.FromArgb(0x25, 0x63, 0xEB);      // blue
            case RouterState.Loaded: return Color.FromArgb(0x16, 0xA3, 0x4A);     // green
            default: return Color.FromArgb(0xD9, 0x77, 0x06);                     // amber: in transition
        }
    }

    public static Bitmap Render(int size, Color tile)
    {
        var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
        using (Graphics g = Graphics.FromImage(bitmap))
        using (GraphicsPath tilePath = RoundedSquare(size, size * 0.22f))
        using (var tileBrush = new SolidBrush(tile))
        using (var llama = new GraphicsPath())
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.FillPath(tileBrush, tilePath);

            // Head spans x 24..82; center it and leave a margin above the ears.
            float scale = size / 100f * 0.86f;
            float offsetX = size / 2f - 53 * scale;
            float offsetY = size * 0.1f;
            llama.AddPolygon(LlamaOutline.Select(p => new PointF(offsetX + p.X * scale, offsetY + p.Y * scale)).ToArray());
            g.SetClip(tilePath);
            g.FillPath(Brushes.White, llama);

            // An eye only reads from 32 px up; below that it is noise.
            if (size >= 32)
            {
                float eye = 6 * scale;
                g.FillEllipse(tileBrush, offsetX + 57 * scale - eye / 2, offsetY + 32 * scale - eye / 2, eye, eye);
            }
        }
        return bitmap;
    }

    public static Icon CreateIcon(int size, Color tile)
    {
        using (Bitmap bitmap = Render(size, tile))
        {
            IntPtr handle = bitmap.GetHicon();
            try
            {
                using (Icon borrowed = Icon.FromHandle(handle)) return (Icon)borrowed.Clone();
            }
            finally
            {
                DestroyIcon(handle);
            }
        }
    }

    // Writes a multi-size .ico of PNG frames (Vista+ format) in the idle color, for the exe and shortcut.
    public static void WriteIcoFile(string path)
    {
        var frames = new List<byte[]>();
        foreach (int size in IcoSizes)
        {
            using (Bitmap bitmap = Render(size, ColorFor(RouterState.Idle)))
            using (var png = new MemoryStream())
            {
                bitmap.Save(png, ImageFormat.Png);
                frames.Add(png.ToArray());
            }
        }

        using (var writer = new BinaryWriter(File.Create(path)))
        {
            writer.Write((short)0);               // reserved
            writer.Write((short)1);               // type: icon
            writer.Write((short)frames.Count);
            int offset = 6 + 16 * frames.Count;
            for (int i = 0; i < frames.Count; i++)
            {
                int size = IcoSizes[i];
                writer.Write((byte)(size >= 256 ? 0 : size));   // 0 means 256
                writer.Write((byte)(size >= 256 ? 0 : size));
                writer.Write((byte)0);            // palette size
                writer.Write((byte)0);            // reserved
                writer.Write((short)1);           // color planes
                writer.Write((short)32);          // bits per pixel
                writer.Write(frames[i].Length);
                writer.Write(offset);
                offset += frames[i].Length;
            }
            foreach (byte[] frame in frames) writer.Write(frame);
        }
    }

    static GraphicsPath RoundedSquare(int size, float radius)
    {
        float diameter = radius * 2;
        float far = size - diameter;
        var path = new GraphicsPath();
        path.AddArc(0, 0, diameter, diameter, 180, 90);
        path.AddArc(far, 0, diameter, diameter, 270, 90);
        path.AddArc(far, far, diameter, diameter, 0, 90);
        path.AddArc(0, far, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
