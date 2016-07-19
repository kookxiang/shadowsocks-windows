using System;
using System.Net;
using Shadowsocks.Model;

namespace Shadowsocks.Controller.Strategy
{
    internal class BalancingStrategy : IStrategy
    {
        private readonly ShadowsocksController _controller;
        private readonly Random _random;

        public BalancingStrategy(ShadowsocksController controller)
        {
            _controller = controller;
            _random = new Random();
        }

        public string Name
        {
            get { return I18N.GetString("Load Balance"); }
        }

        public string ID
        {
            get { return "com.shadowsocks.strategy.balancing"; }
        }

        public void ReloadServers()
        {
            // do nothing
        }

        public Server GetAServer(IStrategyCallerType type, IPEndPoint localIPEndPoint)
        {
            var configs = _controller.GetCurrentConfiguration().configs;
            int index;
            if (type == IStrategyCallerType.TCP)
            {
                index = _random.Next();
            }
            else
            {
                index = localIPEndPoint.GetHashCode();
            }
            return configs[index%configs.Count];
        }

        public void UpdateLatency(Server server, TimeSpan latency)
        {
            // do nothing
        }

        public void UpdateLastRead(Server server)
        {
            // do nothing
        }

        public void UpdateLastWrite(Server server)
        {
            // do nothing
        }

        public void SetFailure(Server server)
        {
            // do nothing
        }
    }
}