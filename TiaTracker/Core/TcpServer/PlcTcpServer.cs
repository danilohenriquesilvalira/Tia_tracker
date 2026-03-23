using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace TiaTracker.Core.TcpServer
{
    public class PlcTcpServer
    {
        public int  Port      { get; private set; }
        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public event Action<string, Dictionary<string, TagValue>> DataReceived;
        public event Action<string>                               LogMessage;

        private TcpListener              _listener;
        private CancellationTokenSource  _cts;

        public async Task StartAsync(int port)
        {
            if (IsRunning) return;
            Port      = port;
            _cts      = new CancellationTokenSource();
            _listener = new TcpListener(IPAddress.Any, port);
            _listener.Start();
            Log($"Servidor iniciado na porta {port}");

            try
            {
                while (!_cts.Token.IsCancellationRequested)
                {
                    var client = await _listener.AcceptTcpClientAsync();
                    _ = HandleClientAsync(client, _cts.Token);
                }
            }
            catch (ObjectDisposedException) { }
            catch (Exception ex) { Log($"Erro: {ex.Message}"); }
        }

        public void Stop()
        {
            _cts?.Cancel();
            _listener?.Stop();
            _listener = null;
            _cts      = null;
            Log("Servidor parado.");
        }

        private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
        {
            var ep = client.Client.RemoteEndPoint?.ToString() ?? "?";
            Log($"PLC ligado: {ep}");
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    var sb  = new StringBuilder();
                    var buf = new byte[4096];

                    while (!ct.IsCancellationRequested)
                    {
                        int n;
                        try { n = await stream.ReadAsync(buf, 0, buf.Length, ct); }
                        catch { break; }
                        if (n == 0) break;

                        sb.Append(Encoding.UTF8.GetString(buf, 0, n));

                        int endIdx;
                        while ((endIdx = sb.ToString().IndexOf("END\n", StringComparison.OrdinalIgnoreCase)) >= 0)
                        {
                            var frame = sb.ToString(0, endIdx);
                            sb.Remove(0, endIdx + 4);
                            ParseFrame(ep, frame);
                        }
                    }
                }
            }
            catch (Exception ex) { Log($"Erro cliente {ep}: {ex.Message}"); }
            Log($"PLC desligado: {ep}");
        }

        private void ParseFrame(string endpoint, string frame)
        {
            var tags = new Dictionary<string, TagValue>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in frame.Split('\n'))
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                var name = line.Substring(0, eq).Trim();
                var val  = line.Substring(eq + 1).Trim();
                tags[name] = new TagValue { Name = name, Value = val, LastUpdate = DateTime.Now };
            }
            if (tags.Count > 0)
                DataReceived?.Invoke(endpoint, tags);
        }

        private void Log(string msg) =>
            LogMessage?.Invoke($"[{DateTime.Now:HH:mm:ss}] {msg}");
    }
}
