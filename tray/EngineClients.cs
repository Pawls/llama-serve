// The HTTP status query every engine client polls with, and Strata's client. Kept apart from LlamaTray.cs so
// test-switchboard.ps1 can build them without WinForms.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

static class StatusQuery
{
    static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(5);

    // The body of a 2xx answer to GET `path`; null when the server does not answer in time or answers an error.
    public static async Task<string> GetBodyAsync(HttpClient http, string path)
    {
        try
        {
            using (var cancel = new CancellationTokenSource(QueryTimeout))
            using (var response = await http.GetAsync(path, cancel.Token))
            {
                if (!response.IsSuccessStatusCode) return null;
                return await response.Content.ReadAsStringAsync();
            }
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
    }
}

sealed class StrataClient
{
    public const string BaseUrl = "http://127.0.0.1:8411";

    static readonly TimeSpan UnloadTimeout = TimeSpan.FromSeconds(30);

    readonly HttpClient http;
    readonly JavaScriptSerializer json = new JavaScriptSerializer();

    public StrataClient(string baseUrl = BaseUrl)
    {
        http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = Timeout.InfiniteTimeSpan };
    }

    // /health's "model" (the configured one, also while unloaded) from the last answer, e.g.
    // "qwen3.8-flash-next-iq3_xxs"; null while Strata does not answer.
    public string Model { get; private set; }

    // /health's "loaded", or null when the server is not answering (stopped, or still loading at its start) or
    // answers a body of another shape. A body without "loaded" means unloaded.
    public async Task<bool?> GetLoadedAsync()
    {
        Dictionary<string, object> root = ParseObject(await StatusQuery.GetBodyAsync(http, "/health"));
        bool? loaded = null;
        if (root != null) loaded = root.ContainsKey("loaded") ? root["loaded"] as bool? : false;
        Model = loaded.HasValue && root.ContainsKey("model") ? root["model"] as string : null;
        return loaded;
    }

    // The JSON object in `body`; null for no body or any other JSON.
    Dictionary<string, object> ParseObject(string body)
    {
        if (body == null) return null;
        try { return json.DeserializeObject(body) as Dictionary<string, object>; }
        catch (ArgumentException) { return null; }
    }

    // Null when the model is now unloaded; otherwise why not.
    public Task<string> UnloadAsync() { return PostControlAsync("/unload", UnloadTimeout); }

    // Null when the model is now loaded; otherwise why not. POST /load answers once the engine is up (1.5-2 min,
    // plus before_load), so it has no timeout: giving up would leave the load running unseen.
    public Task<string> LoadAsync() { return PostControlAsync("/load", Timeout.InfiniteTimeSpan); }

    // Its control endpoints take JSON only and refuse a foreign Origin, which this client never sends. /unload
    // answers 409 while a request runs; a refused /load answers 503 with the reason in error.message.
    async Task<string> PostControlAsync(string path, TimeSpan timeout)
    {
        try
        {
            using (var cancel = new CancellationTokenSource(timeout))
            using (var content = new StringContent("{}", Encoding.UTF8, "application/json"))
            using (var response = await http.PostAsync(path, content, cancel.Token))
            {
                if (response.IsSuccessStatusCode) return null;
                string reason = ErrorMessage(await response.Content.ReadAsStringAsync());
                if (reason != null) return reason;
                return (int)response.StatusCode == 409 ? "Strata is answering a request." : "Strata answered HTTP " + (int)response.StatusCode + ".";
            }
        }
        catch (HttpRequestException ex) { return ex.Message; }
        catch (TaskCanceledException) { return "Strata did not answer in time."; }
    }

    // {"error": {"message": ...}}, or null for any other body.
    string ErrorMessage(string body)
    {
        try
        {
            var error = ((Dictionary<string, object>)json.DeserializeObject(body))["error"] as Dictionary<string, object>;
            return error == null ? null : error["message"] as string;
        }
        catch (Exception) { return null; }
    }
}
