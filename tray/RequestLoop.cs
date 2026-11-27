// One HttpListener and its accept loop, shared by the tray's two listeners (Switchboard.cs, ModelFinder.cs).
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;

sealed class RequestLoop
{
    readonly Func<HttpListenerContext, Task> handle;
    HttpListener listener;

    // handle owns the request from here: it must answer and close it, and never throw.
    public RequestLoop(Func<HttpListenerContext, Task> handle)
    {
        this.handle = handle;
    }

    // The prefixes for a bind: this PC only, or every interface (which needs a URL reservation for the port, made
    // once as administrator by allow-switchboard.ps1).
    public static string[] Prefixes(int port, bool shared)
    {
        if (shared) return new[] { "http://+:" + port + "/" };
        return new[] { "http://127.0.0.1:" + port + "/", "http://localhost:" + port + "/" };
    }

    // Throws HttpListenerException when a prefix cannot be bound (access denied without the URL reservation).
    public void Start(IEnumerable<string> prefixes)
    {
        var fresh = new HttpListener();
        foreach (string prefix in prefixes) fresh.Prefixes.Add(prefix);
        fresh.Start();
        listener = fresh;
        AcceptLoop(fresh);
    }

    public void Stop()
    {
        HttpListener old = listener;
        listener = null;
        if (old != null) old.Close();
    }

    public bool Running { get { return listener != null; } }

    async void AcceptLoop(HttpListener source)
    {
        while (source.IsListening)
        {
            HttpListenerContext context;
            try { context = await source.GetContextAsync(); }
            catch (Exception) { return; }   // the listener was stopped
            var ignored = handle(context);
        }
    }
}
