using HidSharp;
namespace DareuLocal.Core;
public enum DeviceKind
{
    Mouse, Keyboard, Receiver, Unknown
}
public sealed record DeviceEndpoint(HidDevice Device, DeviceKind InitialKind, bool Wireless, int? LinkedPid = null, byte Target = 0)
{
    public string Path => Device.DevicePath + (Target == 0 ? "" : "@" + Target);
    public int Pid => Device.ProductID; public int LogicalPid => LinkedPid ?? Pid;
    public ProductDefinition? Product => DeviceCatalog.Effective(LogicalPid);
    public int PhysicalProtocol => DeviceCatalog.Find(Pid)?.FirmwareType ?? -1;
    public DeviceKind Kind => Product?.Type switch { 0 => DeviceKind.Mouse, 1 => DeviceKind.Keyboard, 2 => DeviceKind.Receiver, _ => InitialKind };
    public string FamilyKey => Product?.Type is 0 or 1 ? Product.Family : Path;
    public bool CanConfigure => Product?.CanConfigure == true && (Kind == DeviceKind.Mouse ? PhysicalProtocol == 2 : Kind == DeviceKind.Keyboard && PhysicalProtocol == 0);
    public string Name => Product?.Name ?? "DAREU 设备";
    public string Label => $"{Name} · {(Wireless ? "2.4G 接收器" : "USB 有线")}";
    public override string ToString() => Label;
}
public static class Hardware
{
    public static List<DeviceEndpoint> Endpoints()
    {
        var result = new List<DeviceEndpoint>();
        var candidates = DeviceList.Local.GetHidDevices(0x260d).Select(d => { try { return (Device: d, Rank: DeviceCatalog.Find(d.ProductID)?.FirmwareType == 2 ? (d.GetMaxOutputReportLength() == 17 && d.GetMaxInputReportLength() == 17 ? 100 : 0) : (d.GetMaxFeatureReportLength() == 65 ? 100 : d.GetMaxFeatureReportLength() > 0 ? 20 : 0)); } catch (IOException) { return (Device: d, Rank: -1); } }).Where(x => x.Rank >= 0);
        foreach (var group in candidates.GroupBy(x => x.Device.ProductID))
        {
            var d = group.OrderByDescending(x => x.Rank).First().Device;
            var product = DeviceCatalog.Find(d.ProductID);
            bool wireless = product?.Type == 2;
            var kind = product?.Type switch
            {
                0 => DeviceKind.Mouse,
                1 => DeviceKind.Keyboard,
                2 => DeviceKind.Receiver,
                _ => DeviceKind.Unknown
            };
            if (wireless && DeviceCatalog.ReceiverCache.TryGetValue(d.DevicePath, out var links))
                foreach (var link in links)
                    result.Add(new(d, kind, true, link.Pid, link.Target));
            else
                result.Add(new(d, kind, wireless));
        }
        return result.OrderBy(x => x.Wireless).ThenBy(x => x.Name).ToList();
    }
    public static List<DeviceEndpoint> PreferredEndpoints(IEnumerable<DeviceEndpoint> endpoints) => endpoints.GroupBy(x => x.FamilyKey).Select(g => ConnectionPreference.Preferred(g, x => x.Wireless)!).OrderBy(x => x.Kind).ThenBy(x => x.Name).ToList();
    public static HidDevice Mouse() => Endpoints().First(x => x.Kind == DeviceKind.Mouse).Device;
    public static HidDevice Keyboard() => Endpoints().First(x => x.Kind == DeviceKind.Keyboard).Device;
}
public sealed record Assignment(byte Function, byte[] Data)
{
    public byte[] Bytes() => new[] { Function }.Concat(Data).ToArray();
    public static Assignment Keyboard(byte key, byte modifiers = 0) => new(6, new byte[] { modifiers, key, 0, 0, 0 });
    public static Assignment Mouse(byte button) => new(1, new byte[] { button, 0, 0, 0, 0 });
    public static Assignment Disabled() => new(0, new byte[5]);
}
public sealed record MacroAction(string Kind, int Code, int Delay = 0)
{
    [System.Text.Json.Serialization.JsonIgnore] public string Display => (Kind.EndsWith("Down") ? "按下 " : "松开 ") + (Kind.StartsWith("Mouse") ? (Code switch { 1 => "鼠标左键", 2 => "鼠标右键", 3 => "鼠标中键", 4 => "鼠标后退键", 5 => "鼠标前进键", _ => "鼠标键" }) : KeyName(Code));
    [System.Text.Json.Serialization.JsonIgnore] public string Timing => $"{Delay} ms";
    static string KeyName(int key) => key switch { >= 4 and <= 29 => ((char)('A' + key - 4)).ToString(), >= 30 and <= 38 => (key - 29).ToString(), 39 => "0", >= 58 and <= 69 => $"F{key - 57}", 40 => "Enter", 41 => "Esc", 42 => "Backspace", 43 => "Tab", 44 => "Space", 224 => "左 Ctrl", 225 => "左 Shift", 226 => "左 Alt", 227 => "左 Win", 228 => "右 Ctrl", 229 => "右 Shift", 230 => "右 Alt", 231 => "右 Win", _ => $"键 {key}" };
}
public sealed record DeviceMacro(string Name, List<MacroAction> Actions);
public sealed record LightingState(int Region, int Effect, int Flag, int Speed, int Brightness, byte[][] Colors, int[] Effects);
public sealed record SensorState(int MotionSync, int Lod, int Response, int Angle, int Ripple, int Mode);

public static class ConnectionPreference
{
    public static T? Preferred<T>(IEnumerable<T> candidates, Func<T, bool> wireless) where T : class => candidates.OrderBy(wireless).FirstOrDefault();
}
public sealed record PowerReading(int? Level, int Status)
{
    public bool? IsCharging => Status switch { 0 => false, 1 or 2 => true, _ => null };
    public string StateText => IsCharging == true ? "充电中" : IsCharging == false ? (Level == 100 ? "电量已满" : "未充电") : "充电状态未知";
    public static PowerReading FromReport(int level, int status) => new(level is >= 0 and <= 100 ? level : null, status);
}
