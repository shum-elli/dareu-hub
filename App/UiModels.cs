using DareuLocal.Core;
namespace DareuLocal.App;
public sealed record KeyChoice(byte Id, string Label, Assignment Default, Assignment FnDefault, int X, int Y, int Width, int Height, bool RotarySegment = false)
{
    public override string ToString() => Label;
}
public sealed record Choice(int Id, string Label)
{
    public override string ToString() => Label;
}
public sealed record KeyBackup(byte Key, byte Layer, Assignment Value);
public sealed record DeviceBackup(int Version, string Device, int Pid, int Profile, MouseState? Mouse, SensorState? Sensor, byte[][]? Buttons, byte[][]? ButtonKeys, List<KeyBackup>? Keys, LightingState[]? Lights, int Sleep, Dictionary<int, byte[]>? Macros);
