// The tray's half of the model finder (ModelFinder.cs): where its files are, how the catalog is rebuilt, and the
// live state the page marks. Usage: README.md § Using the model finder.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class TrayApp
{
    static readonly TimeSpan CatalogBuildTimeout = TimeSpan.FromSeconds(30);
    static readonly string ModelFinderUrl = "http://127.0.0.1:" + ModelFinder.Port + "/";

    ModelFinder modelFinder;

    // Starts or restarts the page's listener to match ShareOnNetwork. It answers short read-only requests, so a
    // rebind needs no wait for replies in flight. The one ModelFinder lives as long as the tray, so a catalog rebuild
    // running during a rebind stays the only one. Returns "network", "local", or null when nothing could be bound.
    string ApplyModelFinder()
    {
        if (modelFinder == null)
            modelFinder = new ModelFinder(Path.Combine(root, "web"), Path.Combine(root, "logs", "model-catalog.json"),
                ModelCatalogSources, BuildModelCatalog, () => OnUi(() => Task.FromResult<object>(EngineStatus())));
        modelFinder.Stop();
        return RequestLoop.BindListener(modelFinder.Start, ModelFinder.Prefixes, shareOnNetwork,
            "The model finder (port " + ModelFinder.Port + ")", Notify);
    }
    // Every file model-catalog.ps1 reads, and the Strata folder itself, whose time moves when a config or run
    // script is added or deleted; the catalog is rebuilt when one is newer than it.
    IEnumerable<string> ModelCatalogSources()
    {
        var files = new List<string>();
        foreach (string name in new[] { "presets.ini", "model-catalog.json", "start-ninfer.ps1", "model-catalog.ps1", "model-sources.ps1" })
            files.Add(Path.Combine(root, name));
        if (Directory.Exists(StrataSize.StrataDir))
        {
            files.Add(StrataSize.StrataDir);
            files.AddRange(Directory.GetFiles(StrataSize.StrataDir, "strata-*.json"));
            files.AddRange(Directory.GetFiles(StrataSize.StrataDir, "run-*.bat"));
        }
        return files;
    }

    // Runs model-catalog.ps1. Null once it wrote the catalog; otherwise its last error line.
    string BuildModelCatalog()
    {
        using (Process process = Process.Start(HiddenScript.StartInfo(root, "model-catalog.ps1", "", true)))
        {
            Task<string> errors = process.StandardError.ReadToEndAsync();
            process.StandardOutput.ReadToEndAsync();   // drained, so a full pipe cannot stall the script
            if (!process.WaitForExit((int)CatalogBuildTimeout.TotalMilliseconds))
            {
                try { process.Kill(); } catch (InvalidOperationException) { }
                return "model-catalog.ps1 did not finish in " + CatalogBuildTimeout.TotalSeconds + " s.";
            }
            if (process.ExitCode == 0) return null;
            return HiddenScript.LastLine(errors.Result) ?? "model-catalog.ps1 exited with code " + process.ExitCode + ".";
        }
    }
}
