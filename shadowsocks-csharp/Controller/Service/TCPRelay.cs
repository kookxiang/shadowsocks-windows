using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Timers;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    internal class TCPRelay : Listener.Service
    {
        private readonly ShadowsocksController _controller;
        private DateTime _lastSweepTime;

        public TCPRelay(ShadowsocksController controller)
        {
            _controller = controller;
            Handlers = new HashSet<TCPHandler>();
            _lastSweepTime = DateTime.Now;
        }

        public ISet<TCPHandler> Handlers { get; set; }

        public bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if (socket.ProtocolType != ProtocolType.Tcp)
            {
                return false;
            }
            if (length < 2 || firstPacket[0] != 5)
            {
                return false;
            }
            socket.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);
            var handler = new TCPHandler(this);
            handler.connection = socket;
            handler.controller = _controller;
            handler.relay = this;

            handler.Start(firstPacket, length);
            IList<TCPHandler> handlersToClose = new List<TCPHandler>();
            lock (Handlers)
            {
                Handlers.Add(handler);
                var now = DateTime.Now;
                if (now - _lastSweepTime > TimeSpan.FromSeconds(1))
                {
                    _lastSweepTime = now;
                    foreach (var handler1 in Handlers)
                    {
                        if (now - handler1.lastActivity > TimeSpan.FromSeconds(900))
                        {
                            handlersToClose.Add(handler1);
                        }
                    }
                }
            }
            foreach (var handler1 in handlersToClose)
            {
                Logging.Debug("Closing timed out TCP connection.");
                handler1.Close();
            }
            return true;
        }

        public void UpdateInboundCounter(Server server, long n)
        {
            _controller.UpdateInboundCounter(server, n);
        }

        public void UpdateOutboundCounter(Server server, long n)
        {
            _controller.UpdateOutboundCounter(server, n);
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            _controller.UpdateLatency(server, latency);
        }
    }

    internal class TCPHandler
    {
        private const int maxRetry = 4;
        // Size of receive buffer.
        public const int RecvSize = 8192;

        public const int RecvReserveSize = IVEncryptor.ONETIMEAUTH_BYTES + IVEncryptor.AUTH_BYTES;
        // reserve for one-time auth

        public const int BufferSize = RecvSize + RecvReserveSize + 32;
        // connection receive buffer
        private readonly byte[] connetionRecvBuffer = new byte[BufferSize];
        // connection send buffer
        private readonly byte[] connetionSendBuffer = new byte[BufferSize];
        private readonly object decryptionLock = new object();

        private readonly object encryptionLock = new object();

        // remote receive buffer
        private readonly byte[] remoteRecvBuffer = new byte[BufferSize];
        // remote send buffer
        private readonly byte[] remoteSendBuffer = new byte[BufferSize];
        private readonly TCPRelay tcprelay; // TODO: tcprelay ?= relay
        private int _bytesToSend;
        private byte[] _firstPacket;
        private int _firstPacketLength;

        private DateTime _startConnectTime;
        private DateTime _startReceivingTime;
        private DateTime _startSendingTime;
        private bool closed;

        private byte command;
        private bool connected;
        public Socket connection;
        // Received data string.

        private bool connectionShutdown;
        public ShadowsocksController controller;
        // public Encryptor encryptor;
        public IEncryptor encryptor;

        public DateTime lastActivity;
        public TCPRelay relay;
        // Client  socket.
        public Socket remote;
        private bool remoteShutdown;
        private int retryCount;
        public Server server;

        private int totalRead;
        private int totalWrite;

        public TCPHandler(TCPRelay tcprelay)
        {
            this.tcprelay = tcprelay;
        }

        public void CreateRemote()
        {
            var server = controller.GetAServer(IStrategyCallerType.TCP, (IPEndPoint) connection.RemoteEndPoint);
            if (server == null || server.server == "")
            {
                throw new ArgumentException("No server configured");
            }
            encryptor = EncryptorFactory.GetEncryptor(server.method, server.password, server.auth, false);
            this.server = server;
        }

        public void Start(byte[] firstPacket, int length)
        {
            _firstPacket = firstPacket;
            _firstPacketLength = length;
            HandshakeReceive();
            lastActivity = DateTime.Now;
        }

        private void CheckClose()
        {
            if (connectionShutdown && remoteShutdown)
            {
                Close();
            }
        }

        public void Close()
        {
            lock (relay.Handlers)
            {
                relay.Handlers.Remove(this);
            }
            lock (this)
            {
                if (closed)
                {
                    return;
                }
                closed = true;
            }
            if (connection != null)
            {
                try
                {
                    connection.Shutdown(SocketShutdown.Both);
                    connection.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
            if (remote != null)
            {
                try
                {
                    remote.Shutdown(SocketShutdown.Both);
                    remote.Close();
                }
                catch (Exception e)
                {
                    Logging.LogUsefulException(e);
                }
            }
            lock (encryptionLock)
            {
                lock (decryptionLock)
                {
                    if (encryptor != null)
                    {
                        encryptor.Dispose();
                    }
                }
            }
        }

        private void HandshakeReceive()
        {
            if (closed)
            {
                return;
            }
            try
            {
                var bytesRead = _firstPacketLength;

                if (bytesRead > 1)
                {
                    byte[] response = {5, 0};
                    if (_firstPacket[0] != 5)
                    {
                        // reject socks 4
                        response = new byte[] {0, 91};
                        Logging.Error("socks 5 protocol error");
                    }
                    connection.BeginSend(response, 0, response.Length, 0, HandshakeSendCallback, null);
                }
                else
                {
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandshakeSendCallback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                connection.EndSend(ar);

                // +-----+-----+-------+------+----------+----------+
                // | VER | CMD |  RSV  | ATYP | DST.ADDR | DST.PORT |
                // +-----+-----+-------+------+----------+----------+
                // |  1  |  1  | X'00' |  1   | Variable |    2     |
                // +-----+-----+-------+------+----------+----------+
                // Skip first 3 bytes
                // TODO validate
                connection.BeginReceive(connetionRecvBuffer, 0, 3, 0, handshakeReceive2Callback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void handshakeReceive2Callback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                var bytesRead = connection.EndReceive(ar);

                if (bytesRead >= 3)
                {
                    command = connetionRecvBuffer[1];
                    if (command == 1)
                    {
                        byte[] response = {5, 0, 0, 1, 0, 0, 0, 0, 0, 0};
                        connection.BeginSend(response, 0, response.Length, 0, ResponseCallback, null);
                    }
                    else if (command == 3)
                    {
                        HandleUDPAssociate();
                    }
                }
                else
                {
                    Logging.Debug("failed to recv data in Shadowsocks.Controller.TCPHandler.handshakeReceive2Callback()");
                    Close();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void HandleUDPAssociate()
        {
            var endPoint = (IPEndPoint) connection.LocalEndPoint;
            var address = endPoint.Address.GetAddressBytes();
            var port = endPoint.Port;
            var response = new byte[4 + address.Length + 2];
            response[0] = 5;
            if (endPoint.AddressFamily == AddressFamily.InterNetwork)
            {
                response[3] = 1;
            }
            else if (endPoint.AddressFamily == AddressFamily.InterNetworkV6)
            {
                response[3] = 4;
            }
            address.CopyTo(response, 4);
            response[response.Length - 1] = (byte) (port & 0xFF);
            response[response.Length - 2] = (byte) ((port >> 8) & 0xFF);
            connection.BeginSend(response, 0, response.Length, 0, ReadAll, true);
        }

        private void ReadAll(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                if (ar.AsyncState != null)
                {
                    connection.EndSend(ar);
                    Logging.Debug(remote, RecvSize, "TCP Relay");
                    connection.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0, ReadAll, null);
                }
                else
                {
                    var bytesRead = connection.EndReceive(ar);
                    if (bytesRead > 0)
                    {
                        Logging.Debug(remote, RecvSize, "TCP Relay");
                        connection.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0, ReadAll, null);
                    }
                    else
                    {
                        Close();
                    }
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void ResponseCallback(IAsyncResult ar)
        {
            try
            {
                connection.EndSend(ar);

                StartConnect();
            }

            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void StartConnect()
        {
            try
            {
                CreateRemote();

                // TODO async resolving
                IPAddress ipAddress;
                var parsed = IPAddress.TryParse(server.server, out ipAddress);
                if (!parsed)
                {
                    var ipHostInfo = Dns.GetHostEntry(server.server);
                    ipAddress = ipHostInfo.AddressList[0];
                }
                var remoteEP = new IPEndPoint(ipAddress, server.server_port);

                remote = new Socket(ipAddress.AddressFamily,
                    SocketType.Stream, ProtocolType.Tcp);
                remote.SetSocketOption(SocketOptionLevel.Tcp, SocketOptionName.NoDelay, true);

                _startConnectTime = DateTime.Now;
                var connectTimer = new ServerTimer(3000);
                connectTimer.AutoReset = false;
                connectTimer.Elapsed += connectTimer_Elapsed;
                connectTimer.Enabled = true;
                connectTimer.Server = server;

                connected = false;
                // Connect to the remote endpoint.
                remote.BeginConnect(remoteEP, ConnectCallback, connectTimer);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void connectTimer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (connected)
            {
                return;
            }
            var server = ((ServerTimer) sender).Server;
            var strategy = controller.GetCurrentStrategy();
            if (strategy != null)
            {
                strategy.SetFailure(server);
            }
            Logging.Info($"{server.FriendlyName()} timed out");
            remote.Close();
            RetryConnect();
        }

        private void RetryConnect()
        {
            if (retryCount < maxRetry)
            {
                Logging.Debug($"Connection failed, retry ({retryCount})");
                StartConnect();
                retryCount++;
            }
            else
            {
                Close();
            }
        }

        private void ConnectCallback(IAsyncResult ar)
        {
            Server server = null;
            if (closed)
            {
                return;
            }
            try
            {
                var timer = (ServerTimer) ar.AsyncState;
                server = timer.Server;
                timer.Elapsed -= connectTimer_Elapsed;
                timer.Enabled = false;
                timer.Dispose();

                // Complete the connection.
                remote.EndConnect(ar);

                connected = true;

                Logging.Debug($"Socket connected to {remote.RemoteEndPoint}");

                var latency = DateTime.Now - _startConnectTime;
                var strategy = controller.GetCurrentStrategy();
                strategy?.UpdateLatency(server, latency);
                tcprelay.UpdateLatency(server, latency);

                StartPipe();
            }
            catch (ArgumentException)
            {
            }
            catch (Exception e)
            {
                if (server != null)
                {
                    var strategy = controller.GetCurrentStrategy();
                    if (strategy != null)
                    {
                        strategy.SetFailure(server);
                    }
                }
                Logging.LogUsefulException(e);
                RetryConnect();
            }
        }

        private void StartPipe()
        {
            if (closed)
            {
                return;
            }
            try
            {
                _startReceivingTime = DateTime.Now;
                remote.BeginReceive(remoteRecvBuffer, 0, RecvSize, 0, PipeRemoteReceiveCallback, null);
                connection.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0, PipeConnectionReceiveCallback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeRemoteReceiveCallback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                var bytesRead = remote.EndReceive(ar);
                totalRead += bytesRead;
                tcprelay.UpdateInboundCounter(server, bytesRead);

                if (bytesRead > 0)
                {
                    lastActivity = DateTime.Now;
                    int bytesToSend;
                    lock (decryptionLock)
                    {
                        if (closed)
                        {
                            return;
                        }
                        encryptor.Decrypt(remoteRecvBuffer, bytesRead, remoteSendBuffer, out bytesToSend);
                    }
                    Logging.Debug(remote, bytesToSend, "TCP Relay", "@PipeRemoteReceiveCallback() (download)");
                    connection.BeginSend(remoteSendBuffer, 0, bytesToSend, 0, PipeConnectionSendCallback, null);

                    var strategy = controller.GetCurrentStrategy();
                    if (strategy != null)
                    {
                        strategy.UpdateLastRead(server);
                    }
                }
                else
                {
                    connection.Shutdown(SocketShutdown.Send);
                    connectionShutdown = true;
                    CheckClose();

                    //if (totalRead == 0)
                    //{
                    //    // closed before anything received, reports as failure
                    //    // disable this feature
                    //    controller.GetCurrentStrategy().SetFailure(this.server);
                    //}
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeConnectionReceiveCallback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                var bytesRead = connection.EndReceive(ar);
                totalWrite += bytesRead;

                if (bytesRead > 0)
                {
                    int bytesToSend;
                    lock (encryptionLock)
                    {
                        if (closed)
                        {
                            return;
                        }
                        encryptor.Encrypt(connetionRecvBuffer, bytesRead, connetionSendBuffer, out bytesToSend);
                    }
                    Logging.Debug(remote, bytesToSend, "TCP Relay", "@PipeConnectionReceiveCallback() (upload)");
                    tcprelay.UpdateOutboundCounter(server, bytesToSend);
                    _startSendingTime = DateTime.Now;
                    _bytesToSend = bytesToSend;
                    remote.BeginSend(connetionSendBuffer, 0, bytesToSend, 0, PipeRemoteSendCallback, null);

                    var strategy = controller.GetCurrentStrategy();
                    strategy?.UpdateLastWrite(server);
                }
                else
                {
                    remote.Shutdown(SocketShutdown.Send);
                    remoteShutdown = true;
                    CheckClose();
                }
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeRemoteSendCallback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                remote.EndSend(ar);
                connection.BeginReceive(connetionRecvBuffer, 0, RecvSize, 0, PipeConnectionReceiveCallback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        private void PipeConnectionSendCallback(IAsyncResult ar)
        {
            if (closed)
            {
                return;
            }
            try
            {
                connection.EndSend(ar);
                remote.BeginReceive(remoteRecvBuffer, 0, RecvSize, 0, PipeRemoteReceiveCallback, null);
            }
            catch (Exception e)
            {
                Logging.LogUsefulException(e);
                Close();
            }
        }

        // inner class
        private class ServerTimer : Timer
        {
            public Server Server;

            public ServerTimer(int p) : base(p)
            {
            }
        }
    }
}