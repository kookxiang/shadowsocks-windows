using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using Shadowsocks.Controller.Strategy;
using Shadowsocks.Encryption;
using Shadowsocks.Model;

namespace Shadowsocks.Controller
{
    internal class UDPRelay : Listener.Service
    {
        private readonly LRUCache<IPEndPoint, UDPHandler> _cache;
        private readonly ShadowsocksController _controller;
        public long inbound = 0;

        public long outbound = 0;

        public UDPRelay(ShadowsocksController controller)
        {
            _controller = controller;
            _cache = new LRUCache<IPEndPoint, UDPHandler>(512); // todo: choose a smart number
        }

        public bool Handle(byte[] firstPacket, int length, Socket socket, object state)
        {
            if (socket.ProtocolType != ProtocolType.Udp)
            {
                return false;
            }
            if (length < 4)
            {
                return false;
            }
            var udpState = (Listener.UDPState) state;
            var remoteEndPoint = (IPEndPoint) udpState.remoteEndPoint;
            var handler = _cache.get(remoteEndPoint);
            if (handler == null)
            {
                handler = new UDPHandler(socket, _controller.GetAServer(IStrategyCallerType.UDP, remoteEndPoint),
                    remoteEndPoint);
                _cache.add(remoteEndPoint, handler);
            }
            handler.Send(firstPacket, length);
            handler.Receive();
            return true;
        }

        public class UDPHandler
        {
            private readonly byte[] _buffer = new byte[1500];
            private readonly Socket _local;

            private readonly IPEndPoint _localEndPoint;
            private readonly Socket _remote;
            private readonly IPEndPoint _remoteEndPoint;

            private readonly Server _server;

            public UDPHandler(Socket local, Server server, IPEndPoint localEndPoint)
            {
                _local = local;
                _server = server;
                _localEndPoint = localEndPoint;

                // TODO async resolving
                IPAddress ipAddress;
                var parsed = IPAddress.TryParse(server.server, out ipAddress);
                if (!parsed)
                {
                    var ipHostInfo = Dns.GetHostEntry(server.server);
                    ipAddress = ipHostInfo.AddressList[0];
                }
                _remoteEndPoint = new IPEndPoint(ipAddress, server.server_port);
                _remote = new Socket(_remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            }

            public void Send(byte[] data, int length)
            {
                var encryptor = EncryptorFactory.GetEncryptor(_server.method, _server.password, _server.auth, true);
                var dataIn = new byte[length - 3 + IVEncryptor.ONETIMEAUTH_BYTES];
                Array.Copy(data, 3, dataIn, 0, length - 3);
                var dataOut = new byte[length - 3 + 16 + IVEncryptor.ONETIMEAUTH_BYTES];
                int outlen;
                encryptor.Encrypt(dataIn, length - 3, dataOut, out outlen);
                Logging.Debug(_localEndPoint, _remoteEndPoint, outlen, "UDP Relay");
                _remote.SendTo(dataOut, outlen, SocketFlags.None, _remoteEndPoint);
            }

            public void Receive()
            {
                EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                Logging.Debug($"++++++Receive Server Port, size:" + _buffer.Length);
                _remote.BeginReceiveFrom(_buffer, 0, _buffer.Length, 0, ref remoteEndPoint, RecvFromCallback, null);
            }

            public void RecvFromCallback(IAsyncResult ar)
            {
                try
                {
                    EndPoint remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                    var bytesRead = _remote.EndReceiveFrom(ar, ref remoteEndPoint);

                    var dataOut = new byte[bytesRead];
                    int outlen;

                    var encryptor = EncryptorFactory.GetEncryptor(_server.method, _server.password, _server.auth, true);
                    encryptor.Decrypt(_buffer, bytesRead, dataOut, out outlen);

                    var sendBuf = new byte[outlen + 3];
                    Array.Copy(dataOut, 0, sendBuf, 3, outlen);

                    Logging.Debug(_localEndPoint, _remoteEndPoint, outlen, "UDP Relay");
                    _local.SendTo(sendBuf, outlen + 3, 0, _localEndPoint);
                    Receive();
                }
                catch (ObjectDisposedException)
                {
                    // TODO: handle the ObjectDisposedException
                }
                catch (Exception)
                {
                    // TODO: need more think about handle other Exceptions, or should remove this catch().
                }
                finally
                {
                }
            }

            public void Close()
            {
                try
                {
                    _remote.Close();
                }
                catch (ObjectDisposedException)
                {
                    // TODO: handle the ObjectDisposedException
                }
                catch (Exception)
                {
                    // TODO: need more think about handle other Exceptions, or should remove this catch().
                }
                finally
                {
                }
            }
        }
    }

    // cc by-sa 3.0 http://stackoverflow.com/a/3719378/1124054
    internal class LRUCache<K, V> where V : UDPRelay.UDPHandler
    {
        private readonly Dictionary<K, LinkedListNode<LRUCacheItem<K, V>>> cacheMap =
            new Dictionary<K, LinkedListNode<LRUCacheItem<K, V>>>();

        private readonly int capacity;
        private readonly LinkedList<LRUCacheItem<K, V>> lruList = new LinkedList<LRUCacheItem<K, V>>();

        public LRUCache(int capacity)
        {
            this.capacity = capacity;
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public V get(K key)
        {
            LinkedListNode<LRUCacheItem<K, V>> node;
            if (cacheMap.TryGetValue(key, out node))
            {
                var value = node.Value.value;
                lruList.Remove(node);
                lruList.AddLast(node);
                return value;
            }
            return default(V);
        }

        [MethodImpl(MethodImplOptions.Synchronized)]
        public void add(K key, V val)
        {
            if (cacheMap.Count >= capacity)
            {
                RemoveFirst();
            }

            var cacheItem = new LRUCacheItem<K, V>(key, val);
            var node = new LinkedListNode<LRUCacheItem<K, V>>(cacheItem);
            lruList.AddLast(node);
            cacheMap.Add(key, node);
        }

        private void RemoveFirst()
        {
            // Remove from LRUPriority
            var node = lruList.First;
            lruList.RemoveFirst();

            // Remove from cache
            cacheMap.Remove(node.Value.key);
            node.Value.value.Close();
        }
    }

    internal class LRUCacheItem<K, V>
    {
        public K key;
        public V value;

        public LRUCacheItem(K k, V v)
        {
            key = k;
            value = v;
        }
    }
}