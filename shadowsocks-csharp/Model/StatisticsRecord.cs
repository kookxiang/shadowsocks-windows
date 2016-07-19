using System;
using System.Collections.Generic;
using System.Linq;

namespace Shadowsocks.Model
{
    // Simple processed records for a short period of time
    public class StatisticsRecord
    {
        public int? AverageInboundSpeed;

        // in ping-only records, these fields would be null
        public int? AverageLatency;

        public int? AverageOutboundSpeed;

        // if user disabled ping test, response would be null
        public int? AverageResponse;
        public int? MaxInboundSpeed;
        public int? MaxLatency;
        public int? MaxOutboundSpeed;
        public int? MaxResponse;
        public int? MinInboundSpeed;
        public int? MinLatency;
        public int? MinOutboundSpeed;
        public int? MinResponse;
        public float? PackageLoss;

        public StatisticsRecord()
        {
        }

        public StatisticsRecord(string identifier, ICollection<int> inboundSpeedRecords,
            ICollection<int> outboundSpeedRecords, ICollection<int> latencyRecords)
        {
            ServerIdentifier = identifier;
            var inbound = inboundSpeedRecords?.Where(s => s > 0).ToList();
            if (inbound != null && inbound.Any())
            {
                AverageInboundSpeed = (int) inbound.Average();
                MinInboundSpeed = inbound.Min();
                MaxInboundSpeed = inbound.Max();
            }
            var outbound = outboundSpeedRecords?.Where(s => s > 0).ToList();
            if (outbound != null && outbound.Any())
            {
                AverageOutboundSpeed = (int) outbound.Average();
                MinOutboundSpeed = outbound.Min();
                MaxOutboundSpeed = outbound.Max();
            }
            var latency = latencyRecords?.Where(s => s > 0).ToList();
            if (latency != null && latency.Any())
            {
                AverageLatency = (int) latency.Average();
                MinLatency = latency.Min();
                MaxLatency = latency.Max();
            }
        }

        public StatisticsRecord(string identifier, ICollection<int?> responseRecords)
        {
            ServerIdentifier = identifier;
            SetResponse(responseRecords);
        }

        public DateTime Timestamp { get; set; } = DateTime.Now;
        public string ServerIdentifier { get; set; }

        private bool EmptyLatencyData => (AverageLatency == null) && (MinLatency == null) && (MaxLatency == null);

        private bool EmptyInboundSpeedData
            => (AverageInboundSpeed == null) && (MinInboundSpeed == null) && (MaxInboundSpeed == null);

        private bool EmptyOutboundSpeedData
            => (AverageOutboundSpeed == null) && (MinOutboundSpeed == null) && (MaxOutboundSpeed == null);

        private bool EmptyResponseData
            => (AverageResponse == null) && (MinResponse == null) && (MaxResponse == null) && (PackageLoss == null);

        public bool IsEmptyData()
        {
            return EmptyInboundSpeedData && EmptyOutboundSpeedData && EmptyResponseData && EmptyLatencyData;
        }

        public void SetResponse(ICollection<int?> responseRecords)
        {
            if (responseRecords == null) return;
            var records =
                responseRecords.Where(response => response != null).Select(response => response.Value).ToList();
            if (!records.Any()) return;
            AverageResponse = (int?) records.Average();
            MinResponse = records.Min();
            MaxResponse = records.Max();
            PackageLoss = responseRecords.Count(response => response != null)/(float) responseRecords.Count;
        }
    }
}