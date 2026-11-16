using System.Text.Json;
using System.Collections.Concurrent;
namespace DareuLocal.Core;
public sealed record ProductDefinition
{
    public int Pid
    {
        get; init;
    }
    public string Name { get; init; } = "DAREU"; public string Model { get; init; } = ""; public int Type { get; init; } = -1; public int FirmwareType { get; init; } = -1;
    public bool HasBattery
    {
        get; init;
    }
    public bool HasLighting
    {
        get; init;
    }
    public bool HasTravel
    {
        get; init;
    }
    public bool HasMatrix
    {
        get; init;
    }
    public bool CanConfigure
    {
        get; init;
    }
    public int ButtonCount
    {
        get; init;
    }
    public int KeyCount
    {
        get; init;
    }
    public int DpiMin { get; init; } = 100; public int DpiMax { get; init; } = 30000; public int DpiStep { get; init; } = 50; public int StageCount { get; init; } = 5; public int SensorModel
    {
        get; init;
    }
    public string[] NativeModules { get; init; } = Array.Empty<string>(); public string Image { get; init; } = ""; public int[][] PollingUsb { get; init; } = Array.Empty<int[]>(); public int[][] PollingWireless { get; init; } = Array.Empty<int[]>(); public int[] SensorSupports { get; init; } = Array.Empty<int>();
    public string Family => Pid is 0x1264 or 0x1265 or 0x1330 ? "AE6V2" : $"{Model}|{Name}";
    public string Adapter => CanConfigure ? (Type == 1 ? "TGKeyboard" : "JMMouse") : "ReadOnly";
}
public sealed record ReceiverPair
{
    public int PID
    {
        get; init;
    }
    public int CID
    {
        get; init;
    }
    public int MID
    {
        get; init;
    }
    public int Type
    {
        get; init;
    }
}
public sealed record ReceiverRoute
{
    public int PID
    {
        get; init;
    }
    public ReceiverPair[] PairInfo { get; init; } = Array.Empty<ReceiverPair>();
}
public sealed record ReceiverBinding(int Pid, byte Target);
public static class DeviceCatalog
{
    static T Load<T>(string name)
    {
        using var stream = typeof(DeviceCatalog).Assembly.GetManifestResourceStream(name) ?? throw new IOException("设备资料未打包");
        return JsonSerializer.Deserialize<T>(stream) ?? throw new IOException("设备资料无效");
    }
    public static IReadOnlyList<ProductDefinition> All { get; } = Load<ProductDefinition[]>("DareuLocal.Catalog");
    static readonly ReceiverRoute[] Routes = Load<ReceiverRoute[]>("DareuLocal.Receivers");
    public static ProductDefinition? Find(int pid) => All.FirstOrDefault(x => x.Pid == pid);
    // Retain the user's explicit 1330 adaptation for their AE6 wired IDs.
    public static ProductDefinition? Effective(int pid) => Find(pid is 0x1264 or 0x1265 ? 0x1330 : pid);
    public static int? MatchReceiver(int pid, int cid, int mid) => Routes.FirstOrDefault(x => x.PID == pid)?.PairInfo.FirstOrDefault(x => x.CID == cid && x.MID == mid)?.PID;
    public static readonly ConcurrentDictionary<string, ReceiverBinding[]> ReceiverCache = new();
    public static void Remember(string path, IEnumerable<ReceiverBinding> bindings)
    {
        var valid = bindings.Where(x => Find(x.Pid)?.Type is 0 or 1).ToArray();
        if (valid.Length > 0)
            ReceiverCache[path] = valid;
    }
}
public static class DeviceIdentityProbe
{
    public static void ResolveReceivers()
    {
        foreach (var e in Hardware.Endpoints().Where(x => x.Wireless).DistinctBy(x => x.Device.DevicePath))
            try
            {
                if (e.PhysicalProtocol == 2)
                {
                    using var d = new JmMouse(e);
                    d.ResolveReceiverIdentity();
                }
                else if (e.PhysicalProtocol is 0 or 6 or 7 && e.Device.GetMaxFeatureReportLength() == 65)
                {
                    using var d = new TgKeyboard(e);
                    d.ResolveReceiverIdentities();
                }
            }
            catch (IOException) { }
            catch (TimeoutException) { }
    }
    public static PowerReading ReadPower(DeviceEndpoint e)
    {
        if (e.Product?.HasBattery == false)
            return new(null, -1);
        if (e.PhysicalProtocol == 2)
        {
            using var d = new JmMouse(e);
            return d.ReadPower();
        }
        if (e.PhysicalProtocol is 0 or 1 or 6 or 7 && e.Device.GetMaxFeatureReportLength() == 65)
        {
            using var d = new TgKeyboard(e);
            return d.ReadPower();
        }
        throw new IOException("此协议的电量读取尚未适配");
    }
}
