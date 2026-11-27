// One HttpListener and its accept loop, shared by the tray's two listeners (Switchboard.cs, ModelFinder.cs).
using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using System.Windows.Forms;

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
    // once as administrator by allow-tray-ports.ps1).
    public static string[] Prefixes(int port, bool shared)
    {
        if (shared) return new[] { "http://+:" + port + "/" };
        return new[] { "http://127.0.0.1:" + port + "/", "http://localhost:" + port + "/" };
    }

    // Starts a listener on the prefixes for `shared`, falling back to loopback with a notice when the network bind is
    // denied (no URL reservation yet). Returns "network", "local", or null when nothing could be bound (a notice says why).
    public static string BindListener(Action<string[]> start, Func<bool, string[]> prefixes, bool shared, string what,
        Action<string, ToolTipIcon> notify)
    {
        try
        {
            start(prefixes(shared));
            return shared ? "network" : "local";
        }
        catch (HttpListenerException ex)
        {
            if (!shared)
            {
                notify(what + " could not start listening: " + ex.Message, ToolTipIcon.Error);
                return null;
            }
            const int AccessDenied = 5;   // no URL reservation for the port yet
            notify(what + " cannot listen on the network (" + ex.Message + "). It is local only"
                + (ex.ErrorCode == AccessDenied ? " until you run allow-tray-ports.ps1 as administrator." : "."), ToolTipIcon.Warning);
        }
        try
        {
            start(prefixes(false));
            return "local";
        }
        catch (HttpListenerException local)
        {
            notify(what + " could not start listening: " + local.Message, ToolTipIcon.Error);
            return null;
        }
    }

    // Throws HttpListenerException when a prefix cannot be bound (access denied without the URL reservation).
    public void Start(IEnumerable<string> prefixes)
    {
        var fresh = new HttpListener();
        foreach (string prefix in prefixes) fresh.Prefixes.Add(prefix);
        try { fresh.Start(); }
        catch (HttpListenerException)
        {
            fresh.Close();
            throw;
        }
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
