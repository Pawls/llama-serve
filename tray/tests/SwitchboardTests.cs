// Tests for Switchboard.cs, driven over HTTP against a fake host and fake engines on loopback ports.
// Built and run by test-switchboard.ps1; never touches the real router, Strata or ninfer.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

sealed class FakeEngine
{
    public readonly HttpListener Listener = new HttpListener();
    public readonly string BaseUrl;
    public readonly List<string> Bodies = new List<string>();
    public readonly List<string> AuthHeaders = new List<string>();
    public Func<HttpListenerContext, Task> Handler;

    public FakeEngine()
    {
        int port = Tests.FreePort();
        BaseUrl = "http://127.0.0.1:" + port;
        Listener.Prefixes.Add(BaseUrl + "/");
        Listener.Start();
        Loop();
        Handler = Tests.JsonReply("{\"ok\":true}");
    }

    async void Loop()
    {
        while (Listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await Listener.GetContextAsync(); }
            catch (Exception) { return; }
            Serve(ctx);
        }
    }

    async void Serve(HttpListenerContext ctx)
    {
        try
        {
            lock (Bodies)
            {
                using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
                    Bodies.Add(reader.ReadToEnd());
                AuthHeaders.Add(ctx.Request.Headers["Authorization"]);
            }
            await Handler(ctx);
        }
        catch (Exception) { }
        finally { try { ctx.Response.Close(); } catch (Exception) { } }
    }
}

sealed class FakeHost : ISwitchHost
{
    public readonly List<SwitchTarget> TargetList = new List<SwitchTarget>();
    public readonly List<string> EnsureLog = new List<string>();
    public readonly List<DateTime> EnsureTimes = new List<DateTime>();
    public Func<SwitchTarget, Task<string>> Ensure = t => Task.FromResult<string>(null);
    public SwitchTarget Current;
    public int FreeCalls;
    public string FreeError;

    public IList<SwitchTarget> Targets { get { return TargetList; } }
    public Task<SwitchTarget> CurrentAsync() { return Task.FromResult(Current); }
    public Task<Dictionary<string, object>> StatusAsync()
    {
        return Task.FromResult(new Dictionary<string, object> { { "engine", "fake" } });
    }
    public Task<string> FreeAsync() { FreeCalls++; return Task.FromResult(FreeError); }

    public Task<string> EnsureAsync(SwitchTarget t)
    {
        lock (EnsureLog) { EnsureLog.Add(t.ModelId); EnsureTimes.Add(DateTime.UtcNow); }
        return Ensure(t);
    }
}

static class Tests
{
    const string Token = "test-token-123";
    static int failures;
    static int passed;

    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    public static Func<HttpListenerContext, Task> JsonReply(string body, int status = 200)
    {
        return async ctx =>
        {
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        };
    }

    sealed class Rig : IDisposable
    {
        public FakeHost Host = new FakeHost();
        public FakeEngine A = new FakeEngine();
        public FakeEngine B = new FakeEngine();
        public Switchboard Board;
        public string Url;
        public HttpClient Http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };

        public Rig()
        {
            Host.TargetList.Add(new SwitchTarget("router", "model-a", A.BaseUrl));
            Host.TargetList.Add(new SwitchTarget("strata", "model-b", B.BaseUrl));
            int port = FreePort();
            Url = "http://127.0.0.1:" + port;
            Board = new Switchboard(Host, Token, line => Console.WriteLine("       [board] " + line));
            Board.KeepAliveInterval = TimeSpan.FromMilliseconds(150);
            Board.BusyWait = TimeSpan.FromSeconds(10);
            Board.StreamHeadersAfter = TimeSpan.FromMilliseconds(100);
            Board.Start(new[] { Url + "/" });
        }

        public HttpRequestMessage Post(string model, bool stream = false, string path = "/v1/chat/completions")
        {
            string body = "{\"model\":\"" + model + "\",\"stream\":" + (stream ? "true" : "false")
                + ",\"messages\":[{\"role\":\"user\",\"content\":\"hi\"}]}";
            var request = new HttpRequestMessage(HttpMethod.Post, Url + path)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json"),
            };
            request.Headers.Add("Authorization", "Bearer " + Token);
            return request;
        }

        public HttpRequestMessage Get(string path, string authorization = "Bearer " + Token)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, Url + path);
            if (authorization != null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
            return request;
        }

        public void Dispose()
        {
            Board.Stop();
            A.Listener.Close();
            B.Listener.Close();
            Http.Dispose();
        }
    }

    static void Check(string name, bool ok, string detail = null)
    {
        if (ok) { passed++; Console.WriteLine("  ok   " + name); return; }
        failures++;
        Console.WriteLine("  FAIL " + name + (detail == null ? "" : " :: " + detail));
    }

    static async Task Run(string name, Func<Rig, Task> body)
    {
        Console.WriteLine(name);
        try
        {
            using (var rig = new Rig()) await body(rig);
        }
        catch (Exception ex)
        {
            failures++;
            Console.WriteLine("  FAIL " + name + " threw " + ex.GetType().Name + ": " + ex.Message);
        }
    }

    static List<string> ModelIds(string json)
    {
        var root = (Dictionary<string, object>)new JavaScriptSerializer().DeserializeObject(json);
        return ((object[])root["data"]).Select(e => (string)((Dictionary<string, object>)e)["id"]).ToList();
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
        Check("token: bearer matches", Switchboard.TokenMatches("abc", "Bearer abc", null));
        Check("token: bearer is case-insensitive about the scheme", Switchboard.TokenMatches("abc", "bearer abc", null));
        Check("token: x-api-key matches", Switchboard.TokenMatches("abc", null, "abc"));
        Check("token: wrong value", !Switchboard.TokenMatches("abc", "Bearer abd", null));
        Check("token: prefix of the token", !Switchboard.TokenMatches("abc", "Bearer ab", null));
        Check("token: nothing sent", !Switchboard.TokenMatches("abc", null, null));
        Check("token: empty expected never matches", !Switchboard.TokenMatches("", "Bearer ", null));

        await Run("auth", async rig =>
        {
            var none = await rig.Http.SendAsync(rig.Get("/v1/models", null));
            Check("no token is 401", none.StatusCode == HttpStatusCode.Unauthorized);
            var wrong = await rig.Http.SendAsync(rig.Get("/v1/models", "Bearer nope"));
            Check("wrong token is 401", wrong.StatusCode == HttpStatusCode.Unauthorized);
            var post = rig.Post("model-a");
            post.Headers.Remove("Authorization");
            var posted = await rig.Http.SendAsync(post);
            Check("a POST without a token is 401", posted.StatusCode == HttpStatusCode.Unauthorized);
            Check("a refused POST never reaches an engine or the host", rig.A.Bodies.Count == 0 && rig.Host.EnsureLog.Count == 0);
            var keyed = rig.Get("/v1/models", null);
            keyed.Headers.Add("x-api-key", Token);
            var ok = await rig.Http.SendAsync(keyed);
            Check("x-api-key works", ok.StatusCode == HttpStatusCode.OK);
        });

        await Run("models and unknown ids", async rig =>
        {
            var response = await rig.Http.SendAsync(rig.Get("/v1/models"));
            string text = await response.Content.ReadAsStringAsync();
            List<string> ids = ModelIds(text);
            Check("lists every host target", ids.SequenceEqual(new[] { "model-a", "model-b" }), text);
            var unknown = await rig.Http.SendAsync(rig.Post("nope"));
            string unknownText = await unknown.Content.ReadAsStringAsync();
            Check("unknown model is 404", unknown.StatusCode == HttpStatusCode.NotFound);
            Check("the 404 names the known ids", unknownText.Contains("model-a") && unknownText.Contains("model-b"), unknownText);
            Check("an unknown model starts nothing", rig.Host.EnsureLog.Count == 0);
        });

        await Run("a body with no model", async rig =>
        {
            var request = rig.Post("x");
            request.Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json");
            var none = await rig.Http.SendAsync(request);
            Check("no model and no engine up is 400", none.StatusCode == HttpStatusCode.BadRequest);
            rig.Host.Current = rig.Host.TargetList[1];
            var request2 = rig.Post("x");
            request2.Content = new StringContent("{\"messages\":[]}", Encoding.UTF8, "application/json");
            var used = await rig.Http.SendAsync(request2);
            Check("no model uses the engine that is up", used.StatusCode == HttpStatusCode.OK && rig.B.Bodies.Count == 1);
            Check("and starts no other engine", rig.Host.EnsureLog.All(id => id == "model-b"));
        });

        await Run("routing", async rig =>
        {
            rig.A.Handler = JsonReply("{\"from\":\"a\"}");
            rig.B.Handler = JsonReply("{\"error\":\"bad\"}", 400);
            var a = await rig.Http.SendAsync(rig.Post("model-a"));
            string aText = await a.Content.ReadAsStringAsync();
            Check("model-a is answered by engine A", a.StatusCode == HttpStatusCode.OK && aText.Contains("\"from\":\"a\""), aText);
            Check("with a JSON content type", a.Content.Headers.ContentType != null && a.Content.Headers.ContentType.MediaType == "application/json");
            Check("and the body arrives unchanged", rig.A.Bodies.Count == 1 && rig.A.Bodies[0].Contains("\"model\":\"model-a\""));
            Check("the token is not forwarded to the engine", string.IsNullOrEmpty(rig.A.AuthHeaders[0]));
            var b = await rig.Http.SendAsync(rig.Post("model-b"));
            string bText = await b.Content.ReadAsStringAsync();
            Check("model-b goes to engine B and its 400 passes through", b.StatusCode == HttpStatusCode.BadRequest && bText.Contains("bad"), bText);
            Check("each request asked the host to ensure its target", rig.Host.EnsureLog.SequenceEqual(new[] { "model-a", "model-b" }));
        });

        await Run("switch failure", async rig =>
        {
            rig.Host.Ensure = t => Task.FromResult("Strata did not start");
            var response = await rig.Http.SendAsync(rig.Post("model-b"));
            string text = await response.Content.ReadAsStringAsync();
            Check("a failed switch is 503 with the host's reason", response.StatusCode == HttpStatusCode.ServiceUnavailable && text.Contains("Strata did not start"), text);
            Check("and the engine is untouched", rig.B.Bodies.Count == 0);
        });

        await Run("streaming passes through chunk by chunk", async rig =>
        {
            rig.A.Handler = async ctx =>
            {
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.SendChunked = true;
                for (int i = 0; i < 3; i++)
                {
                    byte[] chunk = Encoding.UTF8.GetBytes("data: " + i + "\n\n");
                    await ctx.Response.OutputStream.WriteAsync(chunk, 0, chunk.Length);
                    await ctx.Response.OutputStream.FlushAsync();
                    await Task.Delay(300);
                }
            };
            var arrivals = new List<double>();
            var clock = Stopwatch.StartNew();
            using (var response = await rig.Http.SendAsync(rig.Post("model-a", true), HttpCompletionOption.ResponseHeadersRead))
            using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
            {
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                    if (line.StartsWith("data:")) arrivals.Add(clock.Elapsed.TotalMilliseconds);
            }
            Check("three chunks arrived", arrivals.Count == 3);
            Check("the first chunk arrived before the last was sent", arrivals.Count == 3 && arrivals[2] - arrivals[0] > 400,
                string.Join(",", arrivals.Select(x => x.ToString("0"))));
        });

        await Run("keepalive while a stream waits for a switch", async rig =>
        {
            rig.Host.Ensure = async t => { await Task.Delay(800); return null; };
            rig.B.Handler = async ctx =>
            {
                byte[] chunk = Encoding.UTF8.GetBytes("data: hello\n\ndata: [DONE]\n\n");
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.SendChunked = true;
                await ctx.Response.OutputStream.WriteAsync(chunk, 0, chunk.Length);
            };
            var clock = Stopwatch.StartNew();
            var lines = new List<KeyValuePair<double, string>>();
            using (var response = await rig.Http.SendAsync(rig.Post("model-b", true), HttpCompletionOption.ResponseHeadersRead))
            using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
            {
                Check("headers arrive before the switch ends", clock.ElapsedMilliseconds < 600, clock.ElapsedMilliseconds + " ms");
                Check("as an event stream", response.Content.Headers.ContentType.MediaType == "text/event-stream");
                string line;
                while ((line = await reader.ReadLineAsync()) != null) lines.Add(new KeyValuePair<double, string>(clock.Elapsed.TotalMilliseconds, line));
            }
            Check("a comment line arrives while waiting", lines.Any(l => l.Value.StartsWith(":") && l.Key < 700));
            Check("then the engine's stream", lines.Any(l => l.Value == "data: hello"));
        });

        await Run("a failed switch under a stream ends in an error event", async rig =>
        {
            rig.Host.Ensure = async t => { await Task.Delay(300); return "no VRAM"; };
            var response = await rig.Http.SendAsync(rig.Post("model-b", true));
            string text = await response.Content.ReadAsStringAsync();
            Check("an error event names the reason", text.Contains("data: {\"error\"") && text.Contains("no VRAM"), text);
            Check("and the engine was not called", rig.B.Bodies.Count == 0);
        });

        await Run("busy gate", async rig =>
        {
            DateTime aDone = DateTime.MinValue;
            rig.A.Handler = async ctx =>
            {
                await Task.Delay(800);
                await JsonReply("{\"from\":\"a\"}")(ctx);
                aDone = DateTime.UtcNow;
            };
            Task<HttpResponseMessage> first = rig.Http.SendAsync(rig.Post("model-a"));
            await Task.Delay(200);
            Task<HttpResponseMessage> second = rig.Http.SendAsync(rig.Post("model-b"));
            await Task.WhenAll(first, second);
            Check("both answered", first.Result.StatusCode == HttpStatusCode.OK && second.Result.StatusCode == HttpStatusCode.OK);
            DateTime ensureB = rig.Host.EnsureTimes[1];
            Check("model-b's switch waited for model-a's reply to end", rig.Host.EnsureLog[1] == "model-b" && ensureB >= aDone.AddMilliseconds(-50),
                "ensure B " + ensureB.ToString("HH:mm:ss.fff") + " vs A done " + aDone.ToString("HH:mm:ss.fff"));
        });

        await Run("requests for one model overlap", async rig =>
        {
            rig.A.Handler = async ctx => { await Task.Delay(700); await JsonReply("{}")(ctx); };
            var clock = Stopwatch.StartNew();
            await Task.WhenAll(rig.Http.SendAsync(rig.Post("model-a")), rig.Http.SendAsync(rig.Post("model-a")));
            Check("two requests took about one", clock.ElapsedMilliseconds < 1300, clock.ElapsedMilliseconds + " ms");
        });

        await Run("a client that leaves while waiting frees its slot", async rig =>
        {
            // The reply runs long enough that the keepalive writes to the departed client fail while it waits.
            int calls = 0;
            rig.A.Handler = async ctx =>
            {
                if (Interlocked.Increment(ref calls) == 1) await Task.Delay(2000);
                await JsonReply("{}")(ctx);
            };
            Task<HttpResponseMessage> busy = rig.Http.SendAsync(rig.Post("model-a"));
            await Task.Delay(150);
            using (var leaver = new HttpClient { Timeout = TimeSpan.FromMilliseconds(400) })
            {
                try { await leaver.SendAsync(rig.Post("model-b", true), HttpCompletionOption.ResponseHeadersRead).ContinueWith(t => { var ignored = t.Exception; }); }
                catch (Exception) { }
            }
            await busy;
            await Task.Delay(1500);
            var clock = Stopwatch.StartNew();
            var next = await rig.Http.SendAsync(rig.Post("model-a"));
            Check("the next request is not stuck behind the one that left", next.StatusCode == HttpStatusCode.OK && clock.ElapsedMilliseconds < 1500,
                clock.ElapsedMilliseconds + " ms");
        });

        await Run("the model name is rewritten for the engine", async rig =>
        {
            rig.Host.TargetList.Add(new SwitchTarget("ninfer", "ninfer/model-a", rig.B.BaseUrl, "model-a"));
            await rig.Http.SendAsync(rig.Post("MODEL-A"));
            Check("a name that differs in case reaches the engine in its canonical form",
                rig.A.Bodies.Count == 1 && rig.A.Bodies[0].Contains("\"model\":\"model-a\""), rig.A.Bodies.Count == 1 ? rig.A.Bodies[0] : "no request");
            await rig.Http.SendAsync(rig.Post("ninfer/model-a"));
            Check("a prefixed front-door id reaches its engine under the engine's own id",
                rig.B.Bodies.Count == 1 && rig.B.Bodies[0].Contains("\"model\":\"model-a\"") && !rig.B.Bodies[0].Contains("ninfer/"),
                rig.B.Bodies.Count == 1 ? rig.B.Bodies[0] : "no request");
            Check("the other fields survive the rewrite", rig.B.Bodies.Count == 1 && rig.B.Bodies[0].Contains("\"content\":\"hi\""));
            Check("an exact id still wins over a case-insensitive match", rig.Host.EnsureLog.SequenceEqual(new[] { "model-a", "ninfer/model-a" }));
        });

        await Run("a stream queued behind another model gets keepalives", async rig =>
        {
            rig.A.Handler = async ctx => { await Task.Delay(1500); await JsonReply("{}")(ctx); };
            rig.B.Handler = async ctx =>
            {
                byte[] chunk = Encoding.UTF8.GetBytes("data: hello\n\n");
                ctx.Response.ContentType = "text/event-stream";
                ctx.Response.SendChunked = true;
                await ctx.Response.OutputStream.WriteAsync(chunk, 0, chunk.Length);
            };
            Task<HttpResponseMessage> busy = rig.Http.SendAsync(rig.Post("model-a"));
            await Task.Delay(200);
            var clock = Stopwatch.StartNew();
            var comments = new List<double>();
            using (var response = await rig.Http.SendAsync(rig.Post("model-b", true), HttpCompletionOption.ResponseHeadersRead))
            using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync()))
            {
                Check("headers arrive while the other model's reply is still running", clock.ElapsedMilliseconds < 900, clock.ElapsedMilliseconds + " ms");
                string line;
                while ((line = await reader.ReadLineAsync()) != null)
                    if (line.StartsWith(":")) comments.Add(clock.Elapsed.TotalMilliseconds);
            }
            Check("with comment lines while it waits", comments.Count > 0 && comments[0] < 900);
            await busy;
        });

        await Run("busy reports replies in flight", async rig =>
        {
            rig.A.Handler = async ctx => { await Task.Delay(700); await JsonReply("{}")(ctx); };
            Check("idle at first", !rig.Board.Busy);
            Task<HttpResponseMessage> pending = rig.Http.SendAsync(rig.Post("model-a"));
            await Task.Delay(250);
            Check("busy while a reply runs", rig.Board.Busy);
            await pending;
            await Task.Delay(100);
            Check("idle again once it ends", !rig.Board.Busy);
        });

        await Run("status and free", async rig =>
        {
            var status = await rig.Http.SendAsync(rig.Get("/switchboard/status"));
            string text = await status.Content.ReadAsStringAsync();
            Check("status returns the host's state", status.StatusCode == HttpStatusCode.OK && text.Contains("\"engine\""), text);
            var free = rig.Post("x", false, "/switchboard/free");
            var freed = await rig.Http.SendAsync(free);
            Check("free reaches the host", freed.StatusCode == HttpStatusCode.OK && rig.Host.FreeCalls == 1);
            rig.Host.FreeError = "ninfer did not stop";
            var failed = await rig.Http.SendAsync(rig.Post("x", false, "/switchboard/free"));
            string failedText = await failed.Content.ReadAsStringAsync();
            Check("a failed free is 503 with the reason", failed.StatusCode == HttpStatusCode.ServiceUnavailable && failedText.Contains("ninfer did not stop"), failedText);
        });

        // /switchboard/status reports strata.Model, so a stopped Strata must not keep the last size's id.
        await Run("Strata's model is forgotten once it stops answering", async rig =>
        {
            rig.B.Handler = JsonReply("{\"model\":\"swift-1.5-iq3_s\",\"loaded\":true}");
            var strata = new StrataClient(rig.B.BaseUrl);
            bool? loaded = await strata.GetLoadedAsync();
            Check("a loaded Strata reports its model", loaded == true && strata.Model == "swift-1.5-iq3_s", strata.Model);
            rig.B.Listener.Close();
            loaded = await strata.GetLoadedAsync();
            Check("a stopped Strata reports no model", loaded == null && strata.Model == null, strata.Model);
        });
    }
}
