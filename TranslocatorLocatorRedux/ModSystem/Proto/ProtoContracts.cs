using ProtoBuf;

namespace TranslocatorLocatorRedux.ModSystem.Proto
{
    public enum ScanResponseStatus
    {
        Ok = 0,
        RejectedTooFar = 1,
        RejectedBusy = 2,
        RejectedInvalid = 3
    }
    internal class ProtoContracts
    {
    }

    [ProtoContract]
    public sealed class ConfigRequestPacket
    {
        // empty
    }

    [ProtoContract]
    public sealed class ConfigSyncPacket
    {
        [ProtoMember(1)]
        public string Data { get; set; } = "";

        public ConfigSyncPacket() { }
        public ConfigSyncPacket(string data) { Data = data; }
    }

    [ProtoContract]
    public sealed class ScanRequestPacket
    {
        [ProtoMember(1)] public int X { get; set; }
        [ProtoMember(2)] public int Y { get; set; }
        [ProtoMember(3)] public int Z { get; set; }
        [ProtoMember(4)] public int FaceIndex { get; set; }

        [ProtoMember(5)] public int HotbarSlotNumber { get; set; }
        [ProtoMember(6)] public string ItemCode { get; set; } = "";
    }

    [ProtoContract]
    public sealed class ScanResultPacket
    {
        [ProtoMember(1)] public int Count { get; set; }
        [ProtoMember(2)] public int Range { get; set; }
        [ProtoMember(3)] public int FaceIndex { get; set; }
        [ProtoMember(4)] public int ToolMode { get; set; }
        [ProtoMember(5)] public string ItemCode { get; set; } = "";
        [ProtoMember(6)] public ScanResponseStatus Status { get; set; } = ScanResponseStatus.Ok;
    }
}
