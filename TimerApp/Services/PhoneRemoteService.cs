using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Windows.Networking;
using Windows.Networking.Connectivity;

namespace TimerApp.Services;

/// <summary>Мгновенный снимок состояния таймера для страницы на телефоне.</summary>
public sealed class RemoteStateSnapshot
{
    public string State { get; init; } = "Idle";
    public string DisplayTime { get; init; } = "00:00";
    public string StatusText { get; init; } = string.Empty;
    public string EndTimeText { get; init; } = string.Empty;
    public string TimerName { get; init; } = string.Empty;
    public double Progress { get; init; } = 1.0;
    public bool ShowStart { get; init; }
    public bool IsRunning { get; init; }
    public bool IsPaused { get; init; }
    public bool IsFinished { get; init; }
    public bool IntervalActive { get; init; }
    public string PhaseName { get; init; } = string.Empty;
    public string RoundText { get; init; } = string.Empty;
}

/// <summary>
/// Управление таймером с телефона по домашней Wi-Fi сети.
///
/// Приложение само поднимает крошечный HTTP-сервер в локальной сети:
/// телефон открывает страницу (по QR-коду из окна приложения) и видит
/// остаток времени и кнопки «Старт / Пауза / Продолжить / +1 мин /
/// Следующий этап / Сброс», а также быстрый запуск строкой «15м чай».
/// Никакого облака и установки на телефон не нужно.
///
/// Доступ защищает личный ключ в ссылке: генерируется при каждом запуске
/// приложения и без него сервер отвечает 403. Слушается только время,
/// пока приложение открыто и доступ включён тумблером.
/// </summary>
public sealed class PhoneRemoteService : IDisposable
{
    private const int BasePort = 8777;
    private const int MaxHeaderBytes = 32 * 1024;

    private readonly string _token = NewToken();
    private TcpListener? _listener;
    private CancellationTokenSource? _cts;
    private volatile RemoteStateSnapshot _state = new();
    private bool _disposed;

    /// <summary>Команда с телефона: start, pause, resume, reset, add, skip.</summary>
    public event Action<string>? CommandReceived;

    /// <summary>Быстрый запуск с телефона: текст вида «15м чай».</summary>
    public event Action<string>? QuickStartReceived;

    public bool IsRunning { get; private set; }

    public int Port { get; private set; }

    /// <summary>Ссылка с личным ключом для QR-кода (null — сервер выключен).</summary>
    public string? AccessUrl { get; private set; }

    /// <summary>Обновляет снимок состояния (вызывается из UI-потока).</summary>
    public void UpdateState(RemoteStateSnapshot snapshot) => _state = snapshot;

    /// <summary>Запускает сервер на первом свободном порту. False — не вышло.</summary>
    public bool Start()
    {
        if (IsRunning)
            return true;

        _cts = new CancellationTokenSource();

        for (int port = BasePort; port < BasePort + 10 && _listener is null; port++)
        {
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                Port = port;
            }
            catch (SocketException)
            {
                _listener = null;
            }
        }

        if (_listener is null)
            return false;

        IsRunning = true;
        string ip = GetLocalIPv4() ?? "127.0.0.1";
        AccessUrl = $"http://{ip}:{Port}/?k={_token}";
        _ = AcceptLoopAsync(_cts.Token);
        return true;
    }

    public void Stop()
    {
        try
        {
            _cts?.Cancel();
            _listener?.Stop();
        }
        catch (Exception)
        {
            // Уже остановлен.
        }

        _listener = null;
        IsRunning = false;
        AccessUrl = null;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _listener is not null)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(ct);
            }
            catch (Exception)
            {
                break; // остановка или закрытие listener
            }

            _ = HandleClientAsync(client);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                var stream = client.GetStream();
                var request = await ReadRequestAsync(stream);
                if (request is null)
                    return;

                var (status, contentType, body) = Route(request.Value);
                byte[] payload = Encoding.UTF8.GetBytes(body);
                string head =
                    $"HTTP/1.1 {status}\r\n" +
                    $"Content-Type: {contentType}\r\n" +
                    $"Content-Length: {payload.Length}\r\n" +
                    "Cache-Control: no-store\r\n" +
                    "Connection: close\r\n\r\n";
                byte[] headBytes = Encoding.ASCII.GetBytes(head);
                await stream.WriteAsync(headBytes, 0, headBytes.Length);
                await stream.WriteAsync(payload, 0, payload.Length);
            }
            catch (Exception)
            {
                // Телефон отключился посреди запроса — ничего страшного.
            }
        }
    }

    private readonly struct HttpRequest
    {
        public HttpRequest(string method, string path, string query, string body)
        {
            Method = method;
            Path = path;
            Query = query;
            Body = body;
        }

        public string Method { get; }
        public string Path { get; }
        public string Query { get; }
        public string Body { get; }
    }

    private static async Task<HttpRequest?> ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int headerEnd = -1;

        while (buffer.Length < MaxHeaderBytes)
        {
            int read = await stream.ReadAsync(chunk, 0, chunk.Length);
            if (read <= 0)
                return null;

            buffer.Write(chunk, 0, read);
            headerEnd = IndexOfHeaderEnd(buffer.GetBuffer(), (int)buffer.Length);
            if (headerEnd >= 0)
                break;
        }

        if (headerEnd < 0)
            return null;

        byte[] all = buffer.GetBuffer();
        int headerLength = headerEnd + 4;
        string headerText = Encoding.ASCII.GetString(all, 0, headerLength);
        string[] lines = headerText.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        string[] first = lines[0].Split(' ');
        if (first.Length < 2)
            return null;

        string method = first[0].ToUpperInvariant();
        string target = first[1];
        string path = target;
        string query = string.Empty;
        int q = target.IndexOf('?');
        if (q >= 0)
        {
            path = target[..q];
            query = target[(q + 1)..];
        }

        int contentLength = 0;
        foreach (string line in lines)
        {
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
                int.TryParse(line["Content-Length:".Length..].Trim(), out contentLength);
        }

        // Тело могло частично приехать вместе с заголовками.
        var bodyBytes = new MemoryStream();
        int leftover = (int)buffer.Length - headerLength;
        if (leftover > 0)
            bodyBytes.Write(all, headerLength, leftover);

        while (bodyBytes.Length < contentLength)
        {
            int read = await stream.ReadAsync(chunk, 0, chunk.Length);
            if (read <= 0)
                break;
            bodyBytes.Write(chunk, 0, read);
        }

        string body = Encoding.UTF8.GetString(
            bodyBytes.GetBuffer(), 0, (int)Math.Min(bodyBytes.Length, Math.Max(contentLength, 0)));
        return new HttpRequest(method, path, query, body);
    }

    private static int IndexOfHeaderEnd(byte[] data, int length)
    {
        for (int i = 0; i + 3 < length; i++)
        {
            if (data[i] == (byte)'\r' && data[i + 1] == (byte)'\n' &&
                data[i + 2] == (byte)'\r' && data[i + 3] == (byte)'\n')
                return i;
        }

        return -1;
    }

    private (string Status, string ContentType, string Body) Route(HttpRequest request)
    {
        var query = ParseQuery(request.Query);
        if (!query.TryGetValue("k", out string? key) ||
            !string.Equals(key, _token, StringComparison.Ordinal))
        {
            return ("403 Forbidden", "text/plain; charset=utf-8",
                "Нет доступа: откройте ссылку или QR-код из приложения «Таймер» на компьютере.");
        }

        switch (request.Path)
        {
            case "/":
                return ("200 OK", "text/html; charset=utf-8", PageHtml);

            case "/api/state":
                return ("200 OK", "application/json; charset=utf-8", StateJson());

            case "/api/cmd":
                if (query.TryGetValue("c", out string? command) &&
                    command is "start" or "pause" or "resume" or "reset" or "add" or "skip")
                {
                    CommandReceived?.Invoke(command);
                    return ("200 OK", "application/json; charset=utf-8", "{\"ok\":true}");
                }

                return ("400 Bad Request", "application/json; charset=utf-8", "{\"ok\":false}");

            case "/api/quick":
                string text = (request.Body ?? string.Empty).Trim();
                if (text.Length > 0)
                {
                    QuickStartReceived?.Invoke(text);
                    return ("200 OK", "application/json; charset=utf-8", "{\"ok\":true}");
                }

                return ("400 Bad Request", "application/json; charset=utf-8", "{\"ok\":false}");

            default:
                return ("404 Not Found", "text/plain; charset=utf-8", "Не найдено.");
        }
    }

    private string StateJson()
    {
        var s = _state;
        var sb = new StringBuilder();
        sb.Append('{');
        sb.Append($"\"state\":\"{JsonEscape(s.State)}\",");
        sb.Append($"\"displayTime\":\"{JsonEscape(s.DisplayTime)}\",");
        sb.Append($"\"statusText\":\"{JsonEscape(s.StatusText)}\",");
        sb.Append($"\"endTimeText\":\"{JsonEscape(s.EndTimeText)}\",");
        sb.Append($"\"timerName\":\"{JsonEscape(s.TimerName)}\",");
        sb.Append($"\"progress\":{s.Progress.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture)},");
        sb.Append($"\"showStart\":{(s.ShowStart ? "true" : "false")},");
        sb.Append($"\"isRunning\":{(s.IsRunning ? "true" : "false")},");
        sb.Append($"\"isPaused\":{(s.IsPaused ? "true" : "false")},");
        sb.Append($"\"isFinished\":{(s.IsFinished ? "true" : "false")},");
        sb.Append($"\"intervalActive\":{(s.IntervalActive ? "true" : "false")},");
        sb.Append($"\"phaseName\":\"{JsonEscape(s.PhaseName)}\",");
        sb.Append($"\"roundText\":\"{JsonEscape(s.RoundText)}\"");
        sb.Append('}');
        return sb.ToString();
    }

    private static string JsonEscape(string? value) => (value ?? string.Empty)
        .Replace("\\", "\\\\")
        .Replace("\"", "\\\"")
        .Replace("\r", " ")
        .Replace("\n", " ");

    private static System.Collections.Generic.Dictionary<string, string> ParseQuery(string query)
    {
        var result = new System.Collections.Generic.Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);
        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
            }
            else
            {
                result[Uri.UnescapeDataString(pair[..eq])] =
                    Uri.UnescapeDataString(pair[(eq + 1)..].Replace("+", " "));
            }
        }

        return result;
    }

    private static string GetLocalIPv4()
    {
        try
        {
            string? fallback = null;
            foreach (var host in NetworkInformation.GetHostNames())
            {
                if (host.Type != HostNameType.Ipv4 || host.IPInformation?.NetworkAdapter is null)
                    continue;

                string ip = host.CanonicalName;
                fallback ??= ip;

                if (ip.StartsWith("192.168.", StringComparison.Ordinal) ||
                    ip.StartsWith("10.", StringComparison.Ordinal) ||
                    IsPrivate172(ip))
                    return ip;
            }

            return fallback;
        }
        catch (Exception)
        {
            return null;
        }

        static bool IsPrivate172(string ip)
        {
            if (!ip.StartsWith("172.", StringComparison.Ordinal))
                return false;

            string[] parts = ip.Split('.');
            return parts.Length == 4 &&
                   int.TryParse(parts[1], out int second) &&
                   second is >= 16 and <= 31;
        }
    }

    private static string NewToken()
    {
        Span<byte> bytes = stackalloc byte[6];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private const string PageHtml = """
<!DOCTYPE html>
<html lang="ru">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Таймер</title>
<style>
  body { margin:0; font-family: system-ui, sans-serif; background:#0d1117; color:#e6edf3;
         display:flex; flex-direction:column; align-items:center; padding:24px 16px; gap:10px; }
  #name { font-size:20px; font-weight:600; color:#4CC2FF; min-height:24px; text-align:center; }
  #time { font-size:23vw; font-weight:700; line-height:1; font-variant-numeric: tabular-nums; }
  @media (min-width:600px){ #time{ font-size:120px; } }
  #status { font-size:17px; opacity:.85; text-align:center; }
  #end { font-size:14px; opacity:.6; text-align:center; min-height:18px; }
  #phase { font-size:16px; color:#4CC2FF; min-height:20px; text-align:center; }
  #barwrap { width:100%; max-width:520px; height:8px; background:#26303c; border-radius:4px; overflow:hidden; }
  #bar { height:100%; background:#4CC2FF; width:100%; transition: width .3s linear; }
  .row { display:flex; flex-wrap:wrap; gap:10px; justify-content:center; max-width:520px; }
  button { font-size:17px; padding:14px 20px; border:0; border-radius:10px; background:#26303c; color:#e6edf3; }
  button:active { background:#33465c; }
  button.main { background:#0b5cad; }
  #quick { display:flex; gap:8px; width:100%; max-width:520px; margin-top:8px; }
  #q { flex:1; font-size:16px; padding:12px; border-radius:10px; border:1px solid #33465c;
       background:#161c24; color:#e6edf3; }
  .hint { font-size:13px; opacity:.55; text-align:center; max-width:520px; }
</style>
</head>
<body>
<div id="name"></div>
<div id="time">--:--</div>
<div id="status"></div>
<div id="phase"></div>
<div id="barwrap"><div id="bar"></div></div>
<div id="end"></div>
<div class="row">
  <button id="bStart" class="main" onclick="cmd('start')">Старт</button>
  <button id="bPause" onclick="cmd('pause')">Пауза</button>
  <button id="bResume" class="main" onclick="cmd('resume')">Продолжить</button>
  <button id="bAdd" onclick="cmd('add')">+1 мин</button>
  <button id="bSkip" onclick="cmd('skip')">Следующий этап</button>
  <button id="bReset" onclick="cmd('reset')">Сброс</button>
</div>
<div id="quick">
  <input id="q" placeholder="Быстрый старт: 15м чай" autocomplete="off">
  <button class="main" onclick="quick()">Запустить</button>
</div>
<div class="hint">Страница живёт, пока «Таймер» открыт на компьютере и включён доступ по Wi-Fi.</div>
<script>
const K = new URLSearchParams(location.search).get('k') || '';
async function cmd(c){ try{ await fetch('/api/cmd?k='+encodeURIComponent(K)+'&c='+c, {method:'POST'}); tick(); }catch(e){} }
async function quick(){
  const v = document.getElementById('q').value.trim();
  if(!v) return;
  try{ await fetch('/api/quick?k='+encodeURIComponent(K), {method:'POST', body:v}); document.getElementById('q').value=''; tick(); }catch(e){}
}
function show(id, on){ document.getElementById(id).style.display = on ? '' : 'none'; }
async function tick(){
  try{
    const r = await fetch('/api/state?k='+encodeURIComponent(K));
    const s = await r.json();
    document.getElementById('time').textContent = s.displayTime;
    document.getElementById('name').textContent = s.timerName || '';
    document.getElementById('status').textContent = s.statusText;
    document.getElementById('end').textContent = s.endTimeText;
    document.getElementById('phase').textContent =
      s.intervalActive ? (s.phaseName + ' · круг ' + s.roundText) : '';
    document.getElementById('bar').style.width = Math.round((s.progress||0)*100) + '%';
    show('bStart', s.showStart);
    show('bPause', s.isRunning);
    show('bResume', s.isPaused);
    show('bAdd', s.isRunning || s.isPaused);
    show('bSkip', s.intervalActive && (s.isRunning || s.isPaused));
    show('bReset', s.isRunning || s.isPaused || s.isFinished);
  }catch(e){}
}
setInterval(tick, 500); tick();
</script>
</body>
</html>
""";

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Stop();
        _cts?.Dispose();
    }
}
