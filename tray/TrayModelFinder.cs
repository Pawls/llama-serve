// The tray's half of the model finder (ModelFinder.cs): where its files are, how the catalog is rebuilt, and the
// live state the page marks. Plan: plans/model-finder.md § S1.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

sealed partial class TrayApp
{
    static readonly TimeSpan CatalogBuildTimeout = TimeSpan.FromSeconds(30);
    static readonly string ModelFinderUrl = "http://127.0.0.1:" + ModelFinder.Port + "/";

    ModelFinder modelFinder;

    void StartModelFinder()
    {
        var finder = new ModelFinder(Path.Combine(root, "web"), Path.Combine(root, "logs", "model-catalog.json"),
            ModelCatalogSources, BuildModelCatalog, () => OnUi(() => Task.FromResult<object>(EngineStatus())));
        try
        {
            finder.Start(ModelFinder.Prefixes());
            modelFinder = finder;
        }
        catch (HttpListenerException ex)
        {
            Notify("The model finder could not listen on port " + ModelFinder.Port + ": " + ex.Message, ToolTipIcon.Warning);
        }
    }

    // Every file model-catalog.ps1 reads; the catalog is rebuilt when one is newer than it.
    IEnumerable<string> ModelCatalogSources()
    {
        var files = new List<string>();
        foreach (string name in new[] { "presets.ini", "model-catalog.json", "start-ninfer.ps1", "model-catalog.ps1", "model-sources.ps1" })
            files.Add(Path.Combine(root, name));
        if (Directory.Exists(StrataSize.StrataDir)) files.AddRange(Directory.GetFiles(StrataSize.StrataDir, "strata-*.json"));
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
