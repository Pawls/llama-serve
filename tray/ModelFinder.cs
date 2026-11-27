// The model finder page on port 8407 (plans/model-finder.md § S1): serves web\model-finder.html and its script, the
// catalog model-catalog.ps1 builds (rebuilt here when a source file is newer than it) and the tray's live state. It is
// read-only and needs no token: it shows nothing a client of the router could not list.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class ModelFinder
{
    public const int Port = 8407;

    // Request path -> file in the web folder and its type. Nothing else in that folder is served.
    static readonly Dictionary<string, string[]> WebFiles = new Dictionary<string, string[]>
    {
        { "/", new[] { "model-finder.html", "text/html; charset=utf-8" } },
        { "/model-finder.js", new[] { "model-finder.js", "text/javascript; charset=utf-8" } },
    };

    readonly string webDir, catalogPath;
    readonly Func<IEnumerable<string>> sources;
    readonly Func<string> rebuild;
    readonly Func<Task<object>> status;
    readonly SemaphoreSlim rebuildGate = new SemaphoreSlim(1, 1);
    readonly RequestLoop loop;

    // rebuild writes the catalog and returns null, or returns why it could not; it runs off the listener's thread.
    // status gives the object status.json serializes; the tray reads it on its UI thread.
    public ModelFinder(string webDir, string catalogPath, Func<IEnumerable<string>> sources, Func<string> rebuild,
        Func<Task<object>> status)
    {
        this.webDir = webDir;
        this.catalogPath = catalogPath;
        this.sources = sources;
        this.rebuild = rebuild;
        this.status = status;
        loop = new RequestLoop(HandleAsync);
    }

    public static string[] Prefixes()
    {
        return RequestLoop.Prefixes(Port, false);
    }

    // Throws HttpListenerException when a prefix cannot be bound.
    public void Start(IEnumerable<string> prefixes) { loop.Start(prefixes); }

    public void Stop() { loop.Stop(); }

    async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerResponse response = context.Response;
        try
        {
            string path = context.Request.Url.AbsolutePath;
            string[] file;
            if (context.Request.HttpMethod != "GET")
                Write(response, 405, "text/plain; charset=utf-8", "Only GET.");
            else if (WebFiles.TryGetValue(path, out file))
                Write(response, 200, file[1], File.ReadAllBytes(Path.Combine(webDir, file[0])));
            else if (path == "/catalog.json")
                await ServeCatalogAsync(response);
            else if (path == "/status.json")
            {
                response.Headers["Cache-Control"] = "no-store";
                Write(response, 200, "application/json; charset=utf-8", new JavaScriptSerializer().Serialize(await status()));
            }
            else
                Write(response, 404, "text/plain; charset=utf-8", "Not found.");
        }
        catch (Exception ex)
        {
            try { Write(response, 500, "text/plain; charset=utf-8", ex.Message); } catch (Exception) { }
        }
        finally
        {
            try { response.Close(); } catch (Exception) { }
        }
    }

    async Task ServeCatalogAsync(HttpListenerResponse response)
    {
        string error = await RebuildIfStaleAsync();
        // A failed rebuild still serves the last good catalog: the page then shows what it showed before.
        if (File.Exists(catalogPath))
        {
            response.Headers["Cache-Control"] = "no-store";
            Write(response, 200, "application/json; charset=utf-8", File.ReadAllBytes(catalogPath));
        }
        else
            Write(response, 500, "text/plain; charset=utf-8", "The catalog could not be built: " + error);
    }

    bool Stale()
    {
        if (!File.Exists(catalogPath)) return true;
        DateTime built = File.GetLastWriteTimeUtc(catalogPath);
        return sources().Where(File.Exists).Any(source => File.GetLastWriteTimeUtc(source) > built);
    }

    // Requests that arrive during a rebuild wait for it rather than starting their own.
    async Task<string> RebuildIfStaleAsync()
    {
        if (!Stale()) return null;
        await rebuildGate.WaitAsync();
        try
        {
            if (!Stale()) return null;
            return await Task.Run(rebuild);
        }
        finally
        {
            rebuildGate.Release();
        }
    }

    static void Write(HttpListenerResponse response, int code, string type, string text)
    {
        Write(response, code, type, Encoding.UTF8.GetBytes(text));
    }

    static void Write(HttpListenerResponse response, int code, string type, byte[] body)
    {
        response.StatusCode = code;
        response.ContentType = type;
        response.ContentLength64 = body.Length;
        response.OutputStream.Write(body, 0, body.Length);
    }
}
