using PowerIsland.Models;
using PowerIsland.Services;

namespace PowerIsland.Models;

/// <summary>「添加外设」里可选的一个型号。</summary>
public sealed record CatalogEntry(
    string Id,
    string Brand,
    string Name,
    string Meta,
    DeviceKind Kind,
    ushort Vid,
    ushort PidWireless,
    ushort PidWired)
{
    public string ArtKey => DeviceArt.KeyFor(Vid, Kind);

    public string KindLabel => Kind switch
    {
        DeviceKind.Mouse => "鼠标",
        DeviceKind.Keyboard => "键盘",
        DeviceKind.Gamepad => "手柄",
        DeviceKind.Headset => "耳机",
        _ => "外设"
    };

    public string SearchText => $"{Brand} {Name} {Meta} {KindLabel}".ToLowerInvariant();
}

public static class DeviceCatalog
{
    public static IReadOnlyList<CatalogEntry> All { get; } = Build();

    private static List<CatalogEntry> Build()
    {
        var list = new List<CatalogEntry>();

        foreach (KnownDevice device in KnownDevices.Distinct())
        {
            list.Add(new CatalogEntry(
                $"{device.Vid:X4}:{device.PidWireless:X4}",
                "VGN / VXE / ATK",
                device.Name,
                device.Meta,
                device.Kind,
                device.Vid,
                device.PidWireless,
                device.PidWired));
        }

        foreach (RazerDevice device in RazerReader.Devices)
        {
            list.Add(new CatalogEntry(
                $"{RazerReader.RazerVid:X4}:{device.PidWireless:X4}",
                "Razer",
                device.Name,
                device.Meta,
                device.Kind,
                RazerReader.RazerVid,
                device.PidWireless,
                device.PidWired));
        }

        return list;
    }

    public static CatalogEntry? Find(string id) => All.FirstOrDefault(e => e.Id == id);
}
