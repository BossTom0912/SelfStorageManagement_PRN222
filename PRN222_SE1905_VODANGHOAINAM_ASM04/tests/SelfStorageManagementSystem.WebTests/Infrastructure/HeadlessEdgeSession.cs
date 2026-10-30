using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SelfStorageManagementSystem.WebTests.Infrastructure;

/// <summary>
/// Headless Microsoft Edge browser session utilizing Chrome DevTools Protocol (CDP) over WebSocket.
/// Enables automated real browser testing against in-memory WebApplicationFactory test servers.
/// </summary>
public class HeadlessEdgeSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly string _tempDir;
    private readonly ClientWebSocket _ws;
    private int _nextId = 1;

    private HeadlessEdgeSession(Process process, string tempDir, ClientWebSocket ws)
    {
        _process = process;
        _tempDir = tempDir;
        _ws = ws;
    }

    public static string ResolveEdgeBrowserPath()
    {
        var paths = new[]
        {
            @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
            @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
        };

        foreach (var p in paths)
        {
            if (File.Exists(p)) return p;
        }

        return @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe";
    }

    public static async Task<HeadlessEdgeSession> LaunchAsync(string? edgePath = null, bool disableJs = false)
    {
        var resolvedPath = edgePath ?? ResolveEdgeBrowserPath();
        if (!File.Exists(resolvedPath))
        {
            throw new FileNotFoundException($"Microsoft Edge executable was not found at '{resolvedPath}'.", resolvedPath);
        }

        var port = Random.Shared.Next(26000, 33000);
        var tempDir = Path.Combine(Path.GetTempPath(), "edge_cdp_t13_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        var jsFlag = disableJs ? "--disable-javascript " : "";
        var args = $"--headless --remote-debugging-port={port} --user-data-dir=\"{tempDir}\" --allow-file-access-from-files --disable-extensions --disable-component-extensions-with-background-pages --disable-background-networking --no-first-run --no-default-browser-check {jsFlag}about:blank";

        var psi = new ProcessStartInfo
        {
            FileName = resolvedPath,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = Process.Start(psi) ?? throw new InvalidOperationException("Failed to launch Microsoft Edge.");

        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        string? wsUrl = null;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(200);
            try
            {
                var json = await httpClient.GetStringAsync($"http://127.0.0.1:{port}/json");
                using var doc = JsonDocument.Parse(json);
                foreach (var target in doc.RootElement.EnumerateArray())
                {
                    var targetType = target.TryGetProperty("type", out var tProp) ? tProp.GetString() : null;
                    if (targetType == "page" && target.TryGetProperty("webSocketDebuggerUrl", out var prop))
                    {
                        wsUrl = prop.GetString();
                        if (!string.IsNullOrEmpty(wsUrl)) break;
                    }
                }
                if (!string.IsNullOrEmpty(wsUrl)) break;
            }
            catch
            {
                // Edge still initializing
            }
        }

        if (string.IsNullOrEmpty(wsUrl))
        {
            try { process.Kill(); } catch { }
            try { Directory.Delete(tempDir, true); } catch { }
            throw new InvalidOperationException("Failed to obtain CDP WebSocket debugger URL from Microsoft Edge.");
        }

        var ws = new ClientWebSocket();
        using (var connectCts = new CancellationTokenSource(TimeSpan.FromSeconds(5)))
        {
            await ws.ConnectAsync(new Uri(wsUrl), connectCts.Token);
        }

        var session = new HeadlessEdgeSession(process, tempDir, ws);
        await session.SendCommandAsync("Page.enable");
        await session.SendCommandAsync("Runtime.enable");
        return session;
    }

    public TimeSpan DefaultCommandTimeout { get; set; } = TimeSpan.FromSeconds(15);

    public async Task<JsonElement> SendCommandAsync(
        string method,
        object? @params = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var effectiveTimeout = timeout ?? DefaultCommandTimeout;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(effectiveTimeout);

        var payload = JsonSerializer.Serialize(new
        {
            id,
            method,
            @params = @params ?? new { }
        });

        var timestamp = DateTimeOffset.UtcNow;
        Console.WriteLine($"[CDP {timestamp:HH:mm:ss.fff}] -> {method} (id={id})");
        var sw = Stopwatch.StartNew();

        try
        {
            if (_ws.State != WebSocketState.Open)
            {
                throw new InvalidOperationException($"CDP WebSocket is not open (state: {_ws.State}) when sending '{method}' (id={id}).");
            }

            var bytes = Encoding.UTF8.GetBytes(payload);
            await _ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cts.Token);

            var buffer = new byte[65536];
            while (true)
            {
                using var ms = new MemoryStream();
                WebSocketReceiveResult res;
                do
                {
                    res = await _ws.ReceiveAsync(new ArraySegment<byte>(buffer), cts.Token);
                    if (res.MessageType == WebSocketMessageType.Close)
                    {
                        throw new InvalidOperationException($"CDP WebSocket closed by browser while waiting for response to '{method}' (id={id}). CloseStatus: {res.CloseStatus}, Description: {res.CloseStatusDescription}");
                    }
                    ms.Write(buffer, 0, res.Count);
                } while (!res.EndOfMessage);

                var respText = Encoding.UTF8.GetString(ms.ToArray());
                using var doc = JsonDocument.Parse(respText);
                if (doc.RootElement.TryGetProperty("id", out var idProp) && idProp.GetInt32() == id)
                {
                    Console.WriteLine($"[CDP {DateTimeOffset.UtcNow:HH:mm:ss.fff}] <- {method} (id={id}) completed in {sw.ElapsedMilliseconds}ms");
                    if (doc.RootElement.TryGetProperty("result", out var resultProp))
                    {
                        return resultProp.Clone();
                    }
                    if (doc.RootElement.TryGetProperty("error", out var errProp))
                    {
                        throw new InvalidOperationException($"CDP Error executing '{method}' (id={id}): {errProp}");
                    }
                    return doc.RootElement.Clone();
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            var msg = $"CDP command '{method}' (id={id}) timed out after {effectiveTimeout.TotalSeconds}s at {DateTimeOffset.UtcNow:o}. WebSocket state: {_ws.State}.";
            Console.WriteLine($"[CDP ERROR] {msg}");
            throw new TimeoutException(msg);
        }
    }

    public async Task NavigateAsync(string url, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync("Page.navigate", new { url }, timeout, cancellationToken);
        await Task.Delay(1500, cancellationToken);
    }

    public async Task ReloadAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        await SendCommandAsync("Page.reload", null, timeout, cancellationToken);
        await Task.Delay(1200, cancellationToken);
    }

    private static void ThrowIfException(JsonElement result, string expression)
    {
        if (result.TryGetProperty("exceptionDetails", out var exDetails))
        {
            var desc = exDetails.TryGetProperty("exception", out var exObj) && exObj.TryGetProperty("description", out var descProp)
                ? descProp.GetString()
                : exDetails.TryGetProperty("text", out var textProp)
                    ? textProp.GetString()
                    : "Unknown JavaScript Exception";
            throw new InvalidOperationException($"JavaScript error evaluating '{expression}': {desc}");
        }

        if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("subtype", out var st) && st.GetString() == "error")
        {
            var desc = resObj.TryGetProperty("description", out var descProp) ? descProp.GetString() : "JavaScript Error";
            throw new InvalidOperationException($"JavaScript error evaluating '{expression}': {desc}");
        }
    }

    public async Task ExecuteScriptAsync(string expression, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, timeout, cancellationToken);

        ThrowIfException(result, expression);
    }

    public async Task<string> EvaluateStringRequiredAsync(string expression, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, timeout, cancellationToken);

        ThrowIfException(result, expression);

        if (result.TryGetProperty("result", out var resObj))
        {
            if (resObj.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
            {
                var str = val.GetString();
                if (str != null)
                {
                    return str;
                }
            }
            else if (resObj.TryGetProperty("type", out var typeProp) && typeProp.GetString() == "undefined")
            {
                throw new InvalidOperationException($"Expression '{expression}' returned undefined instead of string.");
            }
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return a valid string.");
    }

    public async Task<string?> EvaluateStringOrNullAsync(string expression, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, timeout, cancellationToken);

        ThrowIfException(result, expression);

        if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("value", out var val) && val.ValueKind == JsonValueKind.String)
        {
            return val.GetString();
        }

        return null;
    }

    public async Task<bool> EvaluateBoolAsync(string expression, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, timeout, cancellationToken);

        ThrowIfException(result, expression);

        if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("value", out var val) && (val.ValueKind == JsonValueKind.True || val.ValueKind == JsonValueKind.False))
        {
            return val.GetBoolean();
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return a valid boolean.");
    }

    public async Task<int> EvaluateIntAsync(string expression, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var result = await SendCommandAsync("Runtime.evaluate", new
        {
            expression,
            returnByValue = true
        }, timeout, cancellationToken);

        ThrowIfException(result, expression);

        if (result.TryGetProperty("result", out var resObj) && resObj.TryGetProperty("value", out var val) && val.TryGetInt32(out var intVal))
        {
            return intVal;
        }

        throw new InvalidOperationException($"Expression '{expression}' did not return a valid integer.");
    }


    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_ws.State == WebSocketState.Open || _ws.State == WebSocketState.CloseReceived)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Closing", closeCts.Token);
            }
        }
        catch { }
        finally
        {
            try { _ws.Dispose(); } catch { }
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(true);
                _process.WaitForExit(2000);
            }
        }
        catch { }
        finally
        {
            try { _process.Dispose(); } catch { }
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch { }
    }
}
