using System;
using System.Collections.Generic;
using UnityEngine;

namespace WarehouseASRS.Core
{
    public enum SlotState : byte { Free, ReservedIn, Stored, ReservedOut }
    public enum BoxLoc { Supplier, InboundConv, OnAmr, InQueue, OnCrane, Stored, OutQueue, StationBuf, StationWork, StationOut, Shipped }
    public enum DockKind { Inbound, Stacker, Station, Pad }
    public enum OrderState { Waiting, Fetching, Picking, Shipped }
    public enum AmrState { Idle, ToPad, Charging, ToStop, Transfer }
    public enum StationPhase { Idle, WaitOrderBox, MoveOrd, WaitSource, MoveSrc, Picking, MoveOut, Shipping }

    public class SimEvent { public float t; public string en, ko; }

    public class Sku
    {
        public int id;
        public string name, nameKo;
        public int cap;          // items in a full box
        public float pop;        // demand weight
        public int cls;          // 0 = A (fast), 1 = B, 2 = C (slow)
        public string ClassName => cls == 0 ? "A" : cls == 1 ? "B" : "C";
    }

    /// <summary>A standard box with an RFID tag. sku &lt; 0 = empty box.</summary>
    public class Box
    {
        public int id;
        public string Rfid => "RF-" + id.ToString("D6");
        public int sku = -1;
        public int qty;
        public BoxLoc loc;
        public int slot = -1;
        public int owner = -1;       // stacker / station / inbound line / AMR index, depending on loc
        public int queueIdx;         // place inside a queue container
        public bool reserved;        // promised to an order (stays so until it is back in storage)
        public bool taken;           // claimed by an AMR job
        public bool orderBox;        // the box the order is packed into
        public int orderId = -1;
        public float availT;         // physically available in the current container from this time on
        public float readyT;         // waiting for a job since
        public float tCreated;
        public Vector3[] xp; public float xt0, xdur;
        public bool Empty => sku < 0 && !orderBox;

        public void Move(Vector3[] pts, float t, float dur) { xp = pts; xt0 = t; xdur = Mathf.Max(0.2f, dur); }
        public void Stop() { xp = null; }
    }

    public class Dock
    {
        public int id; public string name; public DockKind kind; public int owner;
        public int node, entry; public Vector3 pos; public float yaw;
        public int[] spurNodes = new int[0];
        public int load;     // AMR stops pending at this dock
    }

    public class Leg
    {
        public enum Kind { Travel, Fork }
        public Kind kind;
        public float x0, y0, x1, y1, dur, dx, dy;
        public int side;                     // fork direction: -1 south, +1 north
        public Action onMid, onEnd;
        public bool midDone;
    }

    public class RetrieveCmd { public Box box; public int slot; public float tCreated; }

    public class Stacker
    {
        public const int InCap = 2, OutCap = 2;
        public int idx;
        public Crane crane;
        public Dock dock;
        public float aisleZ;
        public readonly List<Box> inQ = new List<Box>();
        public readonly List<Box> outQ = new List<Box>();
        public int inResv;       // AMR jobs promised to bring a box
        public int outResv;      // crane retrievals that will end in the out queue
        public readonly List<RetrieveCmd> retr = new List<RetrieveCmd>();
        public int free, stored, empties;
        public int[] byTime;     // local slot ids ordered by crane travel time from the I/O point
        public int stores, retrievals;
        public int Workload => inQ.Count + inResv + retr.Count + outQ.Count + (crane.Busy ? 1 : 0);
    }

    public class InboundLine
    {
        public const int Cap = 6;
        public int idx; public Dock dock;
        public readonly List<Box> q = new List<Box>();
        public int spawned;
    }

    public class Pick { public Box box; public int sku, qty; public bool delivered, done; }

    public class Order
    {
        public int id;
        public readonly List<Pick> picks = new List<Pick>();
        public Box orderBox;
        public int station = -1;
        public OrderState state;
        public float tCreated, tStart, tDone;
        public int lines, totalQty;
        public bool fetchRequested, boxRequested;
    }

    public class Stop { public Dock dock; public bool pick; public Box box; public int destIdx; public int destSlot = -1; }

    public class Amr
    {
        public int id; public string name;
        public Mover mv;
        public AmrState state;
        public readonly List<Stop> stops = new List<Stop>();
        public Box load;
        public float timer;
        public float battery = 85f;
        public bool lowBat;
        public Dock pad;
        public float busyTime;
        public int jobs, moves;
        public bool Available => stops.Count == 0 && !lowBat && battery >= 30f && (state == AmrState.Idle || state == AmrState.Charging || state == AmrState.ToPad);
        public string StateText => state.ToString();
    }

    public class Station
    {
        public const int InCap = 6, OutCap = 4;
        public const float ItemT = 3.8f, XferT = 2.0f;
        public int idx; public Dock dock;
        public readonly List<Box> inBuf = new List<Box>();
        public readonly List<Box> outBuf = new List<Box>();
        public readonly List<Order> queue = new List<Order>();
        public int inResv;
        public StationPhase phase;
        public Box ordBox, srcBox; public Pick curPick;
        public float timer, rt, blend;
        public int itemsLeft;
        public float busyTime; public int ordersDone, itemsPicked;
        public Order Active => queue.Count > 0 ? queue[0] : null;
    }
}
