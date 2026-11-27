// Tests for ModelFinder.cs, driven over HTTP on a free loopback port against temporary files.
// Built and run by test-model-finder.ps1; never touches the tray, the router or the GPUs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading.Tasks;

static class ModelFinderTests
{
    static int passed, failures;

    static void Check(string name, bool ok, string detail = null)
    {
        if (ok) { passed++; Console.WriteLine("  ok    " + name); return; }
        failures++;
        Console.WriteLine("  FAIL  " + name + (detail == null ? "" : "\n        " + detail));
    }

    static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    sealed class Rig : IDisposable
    {
        public readonly string Dir = Path.Combine(Path.GetTempPath(), "model-finder-tests-" + Guid.NewGuid().ToString("N"));
        public readonly string WebDir, Catalog, Source;
        public readonly HttpClient Http = new HttpClient();
        public readonly ModelFinder Finder;
        public readonly string BaseUrl;
        public int Rebuilds;
        public Func<string> RebuildBody;   // returns an error message, or null after writing the catalog

        public Rig()
        {
            WebDir = Path.Combine(Dir, "web");
            Directory.CreateDirectory(WebDir);
            File.WriteAllText(Path.Combine(WebDir, "model-finder.html"), "<!DOCTYPE html><title>finder</title>");
            File.WriteAllText(Path.Combine(WebDir, "model-finder.js"), "export const x = 1;");
            Catalog = Path.Combine(Dir, "catalog.json");
            Source = Path.Combine(Dir, "presets.ini");
            File.WriteAllText(Source, "[a]");
            RebuildBody = () => { File.WriteAllText(Catalog, "{\"built\":" + Rebuilds + "}"); return null; };
            Finder = new ModelFinder(WebDir, Catalog, () => new[] { Source, Path.Combine(Dir, "absent.json") },
                () => { Rebuilds++; return RebuildBody(); },
                () => Task.FromResult<object>(new Dictionary<string, object> { { "reserveGpu0", true } }));
            int port = FreePort();
            BaseUrl = "http://127.0.0.1:" + port;
            Finder.Start(new[] { BaseUrl + "/" });
        }

        public async Task<HttpResponseMessage> Get(string path)
        {
            return await Http.GetAsync(BaseUrl + path);
        }

        public void Dispose()
        {
            Finder.Stop();
            Http.Dispose();
            try { Directory.Delete(Dir, true); } catch (IOException) { }
        }
    }

    static async Task Run(string name, Func<Rig, Task> body)
    {
        Console.WriteLine(name);
        using (var rig = new Rig())
        {
            try { await body(rig); }
            catch (Exception ex) { Check("no exception", false, ex.ToString()); }
        }
    }

    static string Type(HttpResponseMessage response)
    {
        return response.Content.Headers.ContentType == null ? "" : response.Content.Headers.ContentType.MediaType;
    }

    static int Main()
    {
        MainAsync().GetAwaiter().GetResult();
        Console.WriteLine();
        Console.WriteLine(passed + " passed, " + failures + " failed");
        return failures == 0 ? 0 : 1;
    }

    static async Task MainAsync()
    {
        await Run("static files", async rig =>
        {
            var page = await rig.Get("/");
            Check("/ is the page", page.StatusCode == HttpStatusCode.OK && (await page.Content.ReadAsStringAsync()).Contains("<title>finder"));
            Check("/ is text/html", Type(page) == "text/html", Type(page));
            var script = await rig.Get("/model-finder.js");
            Check("the script is served", script.StatusCode == HttpStatusCode.OK && (await script.Content.ReadAsStringAsync()).Contains("export"));
            Check("the script is text/javascript", Type(script) == "text/javascript", Type(script));
            Check("an unknown path is 404", (await rig.Get("/nope")).StatusCode == HttpStatusCode.NotFound);
            Check("a file beside the catalog is 404", (await rig.Get("/presets.ini")).StatusCode == HttpStatusCode.NotFound);
            Check("a file in web that is not on the list is 404", (await rig.Get("/model-finder.html")).StatusCode == HttpStatusCode.NotFound);
            var post = await rig.Http.PostAsync(rig.BaseUrl + "/", new StringContent(""));
            Check("a POST is 405", post.StatusCode == HttpStatusCode.MethodNotAllowed, post.StatusCode.ToString());
            Check("nothing rebuilt the catalog", rig.Rebuilds == 0);
        });

        await Run("catalog: built when missing, kept while fresh, rebuilt when a source is newer", async rig =>
        {
            var first = await rig.Get("/catalog.json");
            Check("missing: built once and served", first.StatusCode == HttpStatusCode.OK && rig.Rebuilds == 1
                && (await first.Content.ReadAsStringAsync()) == "{\"built\":1}", rig.Rebuilds.ToString());
            Check("served as application/json", Type(first) == "application/json", Type(first));
            await rig.Get("/catalog.json");
            Check("fresh: not rebuilt", rig.Rebuilds == 1, rig.Rebuilds.ToString());
            File.SetLastWriteTimeUtc(rig.Source, DateTime.UtcNow.AddMinutes(5));
            var third = await rig.Get("/catalog.json");
            Check("a newer source: rebuilt", rig.Rebuilds == 2 && (await third.Content.ReadAsStringAsync()) == "{\"built\":2}", rig.Rebuilds.ToString());
        });

        await Run("catalog: a failed rebuild", async rig =>
        {
            rig.RebuildBody = () => "pwsh said no";
            var none = await rig.Get("/catalog.json");
            string text = await none.Content.ReadAsStringAsync();
            Check("with no old file: 500 with the error", none.StatusCode == HttpStatusCode.InternalServerError && text.Contains("pwsh said no"), text);
            File.WriteAllText(rig.Catalog, "{\"old\":true}");
            File.SetLastWriteTimeUtc(rig.Catalog, DateTime.UtcNow.AddMinutes(-10));
            var old = await rig.Get("/catalog.json");
            Check("with an old file: the old file", old.StatusCode == HttpStatusCode.OK && (await old.Content.ReadAsStringAsync()) == "{\"old\":true}");
        });

        await Run("catalog: concurrent requests share one rebuild", async rig =>
        {
            rig.RebuildBody = () => { System.Threading.Thread.Sleep(300); File.WriteAllText(rig.Catalog, "{}"); return null; };
            var all = await Task.WhenAll(Enumerable.Range(0, 4).Select(i => rig.Get("/catalog.json")));
            Check("every request answered 200", all.All(r => r.StatusCode == HttpStatusCode.OK));
            Check("one rebuild", rig.Rebuilds == 1, rig.Rebuilds.ToString());
        });

        await Run("status", async rig =>
        {
            var status = await rig.Get("/status.json");
            string text = await status.Content.ReadAsStringAsync();
            Check("status.json is the host's state as JSON", status.StatusCode == HttpStatusCode.OK && text == "{\"reserveGpu0\":true}", text);
            Check("status is not cached", status.Headers.CacheControl != null && status.Headers.CacheControl.NoStore);
        });
    }
}
