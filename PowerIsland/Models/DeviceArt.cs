using PowerIsland.Models;

namespace PowerIsland.Models;

public static class DeviceArt
{
    /// <summary>按厂商 + 设备类型挑外观图模板。</summary>
    public static string KeyFor(ushort vid, DeviceKind kind)
    {
        // 雷蛇：耳机用官网白色渲染图，鼠标用白色毒蝰实拍抠图
        if (vid == 0x1532)
        {
            return kind switch
            {
                DeviceKind.Headset => "ArtRazerHeadset",
                DeviceKind.Mouse => "ArtRazerViper",
                DeviceKind.Keyboard => "ArtKeyboard",
                _ => "ArtOther"
            };
        }

        if (kind == DeviceKind.Mouse)
        {
            // 实物抠图只对应 VGN / VXE / ATK 的 Compx 系列
            bool isCompxFamily = vid is 0x3554 or 0x373B;
            return isCompxFamily ? "ArtMouse" : "ArtMouseGeneric";
        }

        return kind switch
        {
            DeviceKind.Keyboard => "ArtKeyboard",
            DeviceKind.Gamepad => "ArtGamepad",
            DeviceKind.Headset => "ArtHeadset",
            _ => "ArtOther"
        };
    }
}
