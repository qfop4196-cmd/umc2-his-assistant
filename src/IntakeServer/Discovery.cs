using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace Umc2.IntakeServer
{
    /// <summary>
    /// Answers UDP broadcast "UMC2-INTAKE-DISCOVER" from HIS assistants on the same LAN segment so doctors' PCs
    /// find the server automatically. Replies only to private addresses and contain no secrets.
    /// </summary>
    internal sealed class DiscoveryResponder : IDisposable
    {
        public const string Probe = "UMC2-INTAKE-DISCOVER v1";
        private readonly int port;
        private readonly Func<Dictionary<string, object>> describe;
        private UdpClient client;
        private Thread thread;
        private volatile bool running;

        public DiscoveryResponder(int port, Func<Dictionary<string, object>> describe)
        {
            this.port = port;
            this.describe = describe;
        }

        public void Start()
        {
            try
            {
                client = new UdpClient();
                client.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
                client.Client.Bind(new IPEndPoint(IPAddress.Any, port));
                running = true;
                thread = new Thread(Loop) { IsBackground = true, Name = "discovery" };
                thread.Start();
                Logs.Info("Tự dò tìm máy chủ (UDP " + port + ") đang bật");
            }
            catch (SocketException ex)
            {
                Logs.Warn("Không bật được tự dò tìm UDP " + port + ": " + ex.Message);
            }
        }

        private void Loop()
        {
            while (running)
            {
                try
                {
                    var remote = new IPEndPoint(IPAddress.Any, 0);
                    var data = client.Receive(ref remote);
                    if (!NetUtil.IsPrivateOrLoopback(remote.Address)) continue;
                    var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 64));
                    if (!text.StartsWith(Probe, StringComparison.Ordinal)) continue;
                    var reply = Encoding.UTF8.GetBytes(Json.Serialize(describe()));
                    client.Send(reply, reply.Length, remote);
                }
                catch (SocketException)
                {
                    if (!running) break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.Error("Discovery", ex);
                }
            }
        }

        public void Dispose()
        {
            running = false;
            if (client != null)
            {
                try { client.Close(); }
                catch (Exception) { }
            }
        }
    }
}
