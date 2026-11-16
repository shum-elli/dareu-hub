namespace DareuLocal.Core;

public sealed record DeviceFeatures(bool Performance, bool Mapping, bool Lighting, bool PerKeyRgb, bool AudioReactive, bool Macros, bool Profiles)
{
    public static DeviceFeatures For(ProductDefinition? product, bool adapterReady)
    {
        bool ready = product?.CanConfigure == true && adapterReady;
        bool mouse = product?.Type == 0;
        bool keyboard = product?.Type == 1;
        bool lighting = ready && keyboard && product!.HasLighting;
        bool matrix = lighting && product!.HasMatrix;
        return new(ready && (mouse || keyboard && product!.HasBattery),
            ready && (mouse && product!.ButtonCount > 0 || keyboard && product!.KeyCount > 0),
            lighting, matrix, matrix, ready, ready);
    }
}
