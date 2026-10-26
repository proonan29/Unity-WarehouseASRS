using System;
using UnityEngine;

namespace WarehouseASRS.Core
{
    /// <summary>How a free slot is chosen for a box that has to be stored.</summary>
    public enum SlotPolicy { Random, Nearest, Abc }

    [Serializable]
    public class WarehouseConfig
    {
        public int stackers = 4;          // stacker cranes (aisles)
        public int bays = 30;             // columns per rack row
        public int levels = 16;           // shelf levels
        public int amrs = 10;              // transport AMRs
        public int stations = 2;          // picking stations with a 6-axis robot (1..2)
        public SlotPolicy policy = SlotPolicy.Abc;
        public bool dualCommand = true;   // chain a retrieval right after a store
        public float inboundPerHour = 100f;   // supplier boxes per hour
        public float ordersPerHour = 60f;     // outbound orders per hour
        public float initialFill = 0.4f;
        public float fullStop = 0.92f;        // stop inbound at this occupancy
        public float fullResume = 0.85f;      // resume inbound below this
        public int skuCount = 40;
        public int seed = 1;

        public int SlotsPerStacker => 2 * bays * levels;
        public int Capacity => stackers * SlotsPerStacker;
        public WarehouseConfig Clone() => (WarehouseConfig)MemberwiseClone();
        public override string ToString() => $"{stackers} stackers {bays}x{levels} ({Capacity} slots), {amrs} AMR, {stations} station(s), {policy}{(dualCommand ? "+dual" : "")}, in {inboundPerHour}/h out {ordersPerHour}/h, seed {seed}";
    }
}
