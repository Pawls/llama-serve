// The switchboard: one OpenAI-compatible port that starts whatever engine serves the model a request names,
// waits until it answers, and proxies the request. Compiled into the tray by install-tray.ps1
// (C# 5: no string interpolation, no ?. operator). Design and rules: dev-notes/archive/switchboard.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

// One model the front door serves, and the engine that answers it.
sealed class SwitchTarget
{
    public readonly string Engine;    // "router", "strata" or "ninfer"
    public readonly string ModelId;
    public readonly string BaseUrl;   // where the engine listens, e.g. http://127.0.0.1:8411
    public readonly string UpstreamId;   // the id the engine knows it by; differs from ModelId when two engines share a name

    public SwitchTarget(string engine, string modelId, string baseUrl, string upstreamId = null)
    {
        Engine = engine;
        ModelId = modelId;
        BaseUrl = baseUrl;
        UpstreamId = upstreamId ?? modelId;
    }

    // Requests with the same key share the engine; a different key waits for them to end. Every router preset
    // is its own key, because the router unloads the running preset to load another.
    public string Key { get { return Engine + ":" + ModelId; } }
}

// What the switchboard needs from the tray: the engine state and the start/stop logic.
interface ISwitchHost
{
    IList<SwitchTarget> Targets { get; }
    Task<string> EnsureAsync(SwitchTarget target);   // null once the target answers; otherwise why not
    Task<SwitchTarget> CurrentAsync();               // the engine that is up now, or null
    Task<string> FreeAsync();                        // null once the GPUs are free; otherwise why not
    Task<Dictionary<string, object>> StatusAsync();
}

sealed class Switchboard
{
    public const int Port = 8409;
    const int MaxBodyBytes = 256 * 1024 * 1024;   // a request with base64 images can be large

    public TimeSpan KeepAliveInterval = TimeSpan.FromSeconds(10);
    public TimeSpan BusyWait = TimeSpan.FromMinutes(15);
    // A streaming request whose switch or wait is still going after this long is answered with headers and keepalives.
    public TimeSpan StreamHeadersAfter = TimeSpan.FromSeconds(2);

    // Whether a request is running or queued. The tray moves the listener only while this is false.
    public bool Busy
    {
        get
        {
            lock (gate) return activeCount > 0 || waiting.Values.Any(count => count > 0);
        }
    }

    readonly ISwitchHost host;
    readonly string token;
    readonly Action<string> log;
    readonly HttpClient upstream;
    readonly SemaphoreSlim switchLock = new SemaphoreSlim(1, 1);
    readonly object gate = new object();
    readonly Dictionary<string, int> waiting = new Dictionary<string, int>();
    string activeKey;
    int activeCount;
    HttpListener listener;

    public Switchboard(ISwitchHost host, string token, Action<string> log)
    {
        this.host = host;
        this.token = token;
        this.log = log;
        // .NET Framework allows 2 connections per host unless raised; two streams to one engine would serialize.
        ServicePointManager.DefaultConnectionLimit = Math.Max(ServicePointManager.DefaultConnectionLimit, 64);
        upstream = new HttpClient(new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false })
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    // The prefixes for a bind: this PC only, or every interface (which needs the URL reservation that
    // allow-switchboard.ps1 makes).
    public static string[] Prefixes(bool shared)
    {
        if (shared) return new[] { "http://+:" + Port + "/" };
        return new[] { "http://127.0.0.1:" + Port + "/", "http://localhost:" + Port + "/" };
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
            var ignored = HandleSafelyAsync(context);
        }
    }

    async Task HandleSafelyAsync(HttpListenerContext context)
    {
        Exception failure = null;
        try { await HandleAsync(context); }
        catch (Exception ex) { failure = ex; }   // C# 5 cannot await inside a catch

        try
        {
            if (failure != null)
            {
                log(context.Request.HttpMethod + " " + context.Request.RawUrl + " failed: " + failure.Message);
                await WriteErrorAsync(context.Response, 500, "server_error", failure.Message);
            }
        }
        catch (Exception) { }   // the client is gone, or the headers were already sent
        finally
        {
            try { context.Response.Close(); }
            catch (Exception) { }
        }
    }

    // ---- Auth ----

    internal static bool TokenMatches(string expected, string authorization, string apiKey)
    {
        if (string.IsNullOrEmpty(expected)) return false;
        string supplied = null;
        if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            supplied = authorization.Substring(7).Trim();
        else if (apiKey != null)
            supplied = apiKey.Trim();
        if (supplied == null) return false;
        return FixedTimeEquals(Encoding.UTF8.GetBytes(expected), Encoding.UTF8.GetBytes(supplied));
    }

    static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        int difference = a.Length ^ b.Length;
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) difference |= a[i] ^ b[i];
        return difference == 0;
    }

    // ---- Requests ----

    async Task HandleAsync(HttpListenerContext context)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        if (!TokenMatches(token, request.Headers["Authorization"], request.Headers["x-api-key"]))
        {
            await WriteErrorAsync(response, 401, "authentication_error",
                "Missing or wrong token. Send the contents of logs\\switchboard-token as a Bearer token.");
            return;
        }

        string path = request.Url.AbsolutePath;
        bool isGet = request.HttpMethod == "GET";
        if (isGet && path == "/v1/models")
        {
            await WriteJsonAsync(response, 200, ModelList());
            return;
        }
        if (isGet && path == "/switchboard/status")
        {
            Dictionary<string, object> status = await host.StatusAsync();
            lock (gate) { status["in_flight"] = activeCount; status["in_flight_model"] = activeKey; }
            await WriteJsonAsync(response, 200, status);
            return;
        }
        if (request.HttpMethod == "POST" && path == "/switchboard/free")
        {
            await FreeAsync(response);
            return;
        }

        byte[] body = await ReadBodyAsync(request);
        if (body == null)
        {
            await WriteErrorAsync(response, 413, "invalid_request_error", "The request body is too large.");
            return;
        }
        RequestInfo info = ReadRequest(body);
        SwitchTarget target;
        if (info.Model != null)
        {
            target = host.Targets.FirstOrDefault(t => t.ModelId == info.Model)
                ?? host.Targets.FirstOrDefault(t => string.Equals(t.ModelId, info.Model, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                await WriteErrorAsync(response, 404, "model_not_found",
                    "No model named '" + info.Model + "'. Known: " + string.Join(", ", host.Targets.Select(t => t.ModelId)) + ".");
                return;
            }
        }
        else
        {
            target = await host.CurrentAsync();
            if (target == null)
            {
                await WriteErrorAsync(response, 400, "invalid_request_error",
                    "Name a model in the request body. No engine is running to default to.");
                return;
            }
        }
        await ServeAsync(context, target, body, info);
    }

    async Task FreeAsync(HttpListenerResponse response)
    {
        lock (gate)
        {
            if (activeCount > 0)
            {
                // A reply is running; freeing the GPUs would kill it.
                var busy = new Dictionary<string, object> { { "error", Error("busy", "A reply is still running on " + activeKey + ".") } };
                response.Headers["Retry-After"] = "30";
                WriteJsonNow(response, 409, busy);
                return;
            }
        }
        await switchLock.WaitAsync();
        try
        {
            string error = await host.FreeAsync();
            if (error != null) await WriteErrorAsync(response, 503, "switch_failed", error);
            else await WriteJsonAsync(response, 200, new Dictionary<string, object> { { "freed", true } });
        }
        finally { switchLock.Release(); }
    }

    // The list every OpenAI client reads; it needs no engine to be running.
    Dictionary<string, object> ModelList()
    {
        var data = host.Targets.Select(t => (object)new Dictionary<string, object>
        {
            { "id", t.ModelId }, { "object", "model" }, { "owned_by", "llama-serve" }, { "engine", t.Engine },
        }).ToArray();
        return new Dictionary<string, object> { { "object", "list" }, { "data", data } };
    }

    sealed class RequestInfo
    {
        public string Model;
        public bool Stream;
        public Dictionary<string, object> Root;   // the parsed body, kept for a rewrite of "model"
    }

    static JavaScriptSerializer BodySerializer()
    {
        return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 1000 };
    }

    // The body the engine receives: the client's, with "model" in the id the engine knows. Only a request whose name
    // differs (another case, or a front-door prefix that tells two engines' equal names apart) is re-serialized.
    static byte[] BodyFor(SwitchTarget target, RequestInfo info, byte[] body)
    {
        if (info.Root == null || info.Model == target.UpstreamId) return body;
        info.Root["model"] = target.UpstreamId;
        return Encoding.UTF8.GetBytes(BodySerializer().Serialize(info.Root));
    }

    static RequestInfo ReadRequest(byte[] body)
    {
        var info = new RequestInfo();
        if (body.Length == 0) return info;
        try
        {
            var root = BodySerializer().DeserializeObject(Encoding.UTF8.GetString(body)) as Dictionary<string, object>;
            if (root == null) return info;
            info.Root = root;
            info.Model = root.ContainsKey("model") ? root["model"] as string : null;
            info.Stream = root.ContainsKey("stream") && root["stream"] is bool && (bool)root["stream"];
        }
        catch (Exception) { }   // not JSON: it goes to the engine that is up, which will say so
        return info;
    }

    // Null when the body is over the limit.
    static async Task<byte[]> ReadBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return new byte[0];
        if (request.ContentLength64 > MaxBodyBytes) return null;
        using (var buffer = new MemoryStream())
        {
            await request.InputStream.CopyToAsync(buffer);
            return buffer.Length > MaxBodyBytes ? null : buffer.ToArray();
        }
    }

    // ---- Switching and forwarding ----

    async Task ServeAsync(HttpListenerContext context, SwitchTarget target, byte[] body, RequestInfo info)
    {
        HttpListenerResponse response = context.Response;
        // A streaming request that is still waiting after a moment (a switch, or a queue behind another model) is
        // answered with headers and kept warm, so its read timeout does not fire. Everything else waits in silence.
        Task<string> prepare = PrepareAsync(target);
        bool early = info.Stream && await Task.WhenAny(prepare, Task.Delay(StreamHeadersAfter)) != prepare;
        if (early)
        {
            try
            {
                response.StatusCode = 200;
                response.ContentType = "text/event-stream";
                response.Headers["Cache-Control"] = "no-cache";
                response.SendChunked = true;
                await WriteChunkAsync(response, ": waiting for " + target.ModelId + "\n\n");
                while (true)
                {
                    Task finished = await Task.WhenAny(prepare, Task.Delay(KeepAliveInterval));
                    if (finished == prepare) break;
                    await WriteChunkAsync(response, ": waiting for " + target.ModelId + "\n\n");
                }
            }
            catch (Exception)
            {
                log("client left while waiting for " + target.ModelId);
                // The client left. Its slot is released once the switch it queued has ended.
                var ignored = prepare.ContinueWith(task => { if (task.Status == TaskStatus.RanToCompletion && task.Result == null) Release(); });
                throw;
            }
        }

        string error = await prepare;
        if (error != null)
        {
            log("could not serve " + target.ModelId + ": " + error);
            if (early)
            {
                await WriteChunkAsync(response, "data: " + new JavaScriptSerializer().Serialize(
                    new Dictionary<string, object> { { "error", Error("switch_failed", error) } }) + "\n\ndata: [DONE]\n\n");
            }
            else
            {
                response.Headers["Retry-After"] = "30";
                await WriteErrorAsync(response, 503, "switch_failed", error);
            }
            return;
        }
        try
        {
            await ForwardAsync(context, target, BodyFor(target, info, body), early);
        }
        finally
        {
            Release();
        }
    }

    // Waits for the in-flight requests of other models, then switches the engine. Null: the slot is held and the
    // target answers, so the caller must Release. Otherwise why not, with no slot held.
    async Task<string> PrepareAsync(SwitchTarget target)
    {
        if (!await AcquireAsync(target.Key))
            return "Another model has been answering requests for " + BusyWait.TotalMinutes.ToString("0") + " minutes; try again later.";
        try
        {
            await switchLock.WaitAsync();
            string error;
            try { error = await host.EnsureAsync(target); }
            finally { switchLock.Release(); }
            if (error != null) Release();
            return error;
        }
        catch (Exception)
        {
            Release();
            throw;
        }
    }

    // Requests for the model in use run together; another model waits until they have ended. While one waits, new
    // requests for the model in use queue behind it, so a busy client cannot starve it.
    async Task<bool> AcquireAsync(string key)
    {
        DateTime deadline = DateTime.UtcNow + BusyWait;
        lock (gate) { waiting[key] = (waiting.ContainsKey(key) ? waiting[key] : 0) + 1; }
        try
        {
            while (true)
            {
                lock (gate)
                {
                    bool otherWaits = waiting.Any(w => w.Key != key && w.Value > 0);
                    if (activeCount == 0 || (activeKey == key && !otherWaits))
                    {
                        activeKey = key;
                        activeCount++;
                        return true;
                    }
                }
                if (DateTime.UtcNow > deadline) return false;
                await Task.Delay(100);
            }
        }
        finally
        {
            lock (gate) { waiting[key]--; }
        }
    }

    void Release()
    {
        lock (gate)
        {
            activeCount--;
            if (activeCount == 0) activeKey = null;
        }
    }

    async Task ForwardAsync(HttpListenerContext context, SwitchTarget target, byte[] body, bool headersSent)
    {
        HttpListenerRequest request = context.Request;
        HttpListenerResponse response = context.Response;
        var outgoing = new HttpRequestMessage(new HttpMethod(request.HttpMethod), target.BaseUrl + request.RawUrl);
        if (body.Length > 0 || request.HttpMethod == "POST")
        {
            outgoing.Content = new ByteArrayContent(body);
            if (request.ContentType != null) outgoing.Content.Headers.TryAddWithoutValidation("Content-Type", request.ContentType);
        }
        if (request.Headers["Accept"] != null) outgoing.Headers.TryAddWithoutValidation("Accept", request.Headers["Accept"]);

        // Disposing the upstream response when a write fails (the client left) stops the engine's generation.
        using (HttpResponseMessage reply = await upstream.SendAsync(outgoing, HttpCompletionOption.ResponseHeadersRead))
        {
            if (headersSent && !reply.IsSuccessStatusCode)
            {
                string text = await reply.Content.ReadAsStringAsync();
                await WriteChunkAsync(response, "data: " + new JavaScriptSerializer().Serialize(
                    new Dictionary<string, object> { { "error", Error("upstream_error", text) } }) + "\n\ndata: [DONE]\n\n");
                return;
            }
            if (!headersSent)
            {
                response.StatusCode = (int)reply.StatusCode;
                if (reply.Content.Headers.ContentType != null) response.ContentType = reply.Content.Headers.ContentType.ToString();
                long? length = reply.Content.Headers.ContentLength;
                if (length.HasValue) response.ContentLength64 = length.Value;
                else response.SendChunked = true;
            }
            using (Stream source = await reply.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[16 * 1024];
                int read;
                while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    await response.OutputStream.WriteAsync(buffer, 0, read);
                    await response.OutputStream.FlushAsync();
                }
            }
        }
    }

    // ---- Responses ----

    static Dictionary<string, object> Error(string type, string message)
    {
        return new Dictionary<string, object> { { "message", message }, { "type", type } };
    }

    static Task WriteErrorAsync(HttpListenerResponse response, int status, string type, string message)
    {
        return WriteJsonAsync(response, status, new Dictionary<string, object> { { "error", Error(type, message) } });
    }

    static Task WriteJsonAsync(HttpListenerResponse response, int status, object payload)
    {
        WriteJsonNow(response, status, payload);
        return Task.FromResult(0);
    }

    static void WriteJsonNow(HttpListenerResponse response, int status, object payload)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(payload));
        response.StatusCode = status;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        response.OutputStream.Write(bytes, 0, bytes.Length);
    }

    static async Task WriteChunkAsync(HttpListenerResponse response, string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        await response.OutputStream.FlushAsync();
    }
}
