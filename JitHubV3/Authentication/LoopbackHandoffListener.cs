using System.Net;
using System.Net.Sockets;
using System.Text;

namespace JitHubV3.Authentication;

internal sealed class LoopbackHandoffListener : IAsyncDisposable
{
    private readonly string _callbackPath;
    private TcpListener? _listener;

    public LoopbackHandoffListener(string callbackPath)
    {
        if (string.IsNullOrWhiteSpace(callbackPath))
        {
            throw new ArgumentException("Callback path is required.", nameof(callbackPath));
        }

        _callbackPath = callbackPath.StartsWith("/", StringComparison.Ordinal) ? callbackPath : "/" + callbackPath;
    }

    public Uri Start()
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("Listener already started.");
        }

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();

        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}{_callbackPath}");
    }

    public async Task<string> WaitForHandoffCodeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (_listener is null)
        {
            throw new InvalidOperationException("Listener not started.");
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(timeoutCts.Token, cancellationToken);

        try
        {
            using var client = await _listener.AcceptTcpClientAsync(linkedCts.Token);
            await using var stream = client.GetStream();

            var request = await ReadHeadersAsync(stream, linkedCts.Token);
            var (path, query) = ParseRequestTarget(request);

            var handoffCode = string.IsNullOrWhiteSpace(query) ? null : GetQueryParameter(query, "handoffCode");

            // Return a small page so the browser doesn't show a connection reset.
            var html = "<!doctype html><html><head><meta charset=\"utf-8\"/><title>Signed in</title></head>" +
                       "<body><h3>Signed in</h3><p>You can return to the app.</p></body></html>";

            var bodyBytes = Encoding.UTF8.GetBytes(html);
            var response = "HTTP/1.1 200 OK\r\n" +
                           "Content-Type: text/html; charset=utf-8\r\n" +
                           $"Content-Length: {bodyBytes.Length}\r\n" +
                           "Connection: close\r\n\r\n";

            var headerBytes = Encoding.ASCII.GetBytes(response);
            await stream.WriteAsync(headerBytes, linkedCts.Token);
            await stream.WriteAsync(bodyBytes, linkedCts.Token);
            await stream.FlushAsync(linkedCts.Token);

            if (!string.Equals(path, _callbackPath, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unexpected callback path: '{path}'.");
            }

            if (string.IsNullOrWhiteSpace(handoffCode))
            {
                throw new InvalidOperationException("Missing handoffCode in loopback callback.");
            }

            return handoffCode;
        }
        catch (OperationCanceledException)
        {
            if (timeoutCts.IsCancellationRequested)
            {
                throw new TimeoutException("Timed out waiting for authentication callback.");
            }

            throw;
        }
    }

    public ValueTask DisposeAsync()
    {
        try
        {
            _listener?.Stop();
        }
        catch
        {
            // ignored
        }

        _listener = null;
        return ValueTask.CompletedTask;
    }

    private static async Task<string> ReadHeadersAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        // Read until end of headers (CRLF CRLF). Limit size to avoid abuse.
        var buffer = new byte[4096];
        var sb = new StringBuilder();

        while (sb.Length < 32_768)
        {
            var read = await stream.ReadAsync(buffer, cancellationToken);
            if (read <= 0)
            {
                break;
            }

            sb.Append(Encoding.ASCII.GetString(buffer, 0, read));

            if (sb.ToString().Contains("\r\n\r\n", StringComparison.Ordinal))
            {
                break;
            }
        }

        return sb.ToString();
    }

    private static (string path, string? query) ParseRequestTarget(string requestHeaders)
    {
        // First line: GET /path?query HTTP/1.1
        var firstLineEnd = requestHeaders.IndexOf("\r\n", StringComparison.Ordinal);
        var firstLine = firstLineEnd >= 0 ? requestHeaders[..firstLineEnd] : requestHeaders;

        var parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            throw new InvalidOperationException("Invalid HTTP request received.");
        }

        var target = parts[1];
        var qIndex = target.IndexOf('?', StringComparison.Ordinal);
        if (qIndex < 0)
        {
            return (target, null);
        }

        return (target[..qIndex], target[(qIndex + 1)..]);
    }

    private static string? GetQueryParameter(string query, string key)
    {
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            if (kv.Length == 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(kv[0]);
            if (!string.Equals(name, key, StringComparison.Ordinal))
            {
                continue;
            }

            return kv.Length == 2 ? Uri.UnescapeDataString(kv[1]) : string.Empty;
        }

        return null;
    }
}
