namespace PowerIsland.Services;

/// <summary>
/// 锂电池电压到剩余容量的换算。
///
/// 鼠标固件上报的百分比是整数粗值（常常停留在同一档好几分钟），
/// 而官方驱动显示的是按电压查表算出来的连续值，两者会对不上。
/// 这里用同一条思路：拿电芯电压对照放电曲线，得出逐 1% 的数值。
///
/// 曲线按 3.7V 钴酸锂 / 聚合物电芯的典型放电特性标定，
/// 关键锚点已用 VGN F1 实测校准（3.934V ≈ 59%）。
/// </summary>
public static class BatteryCurve
{
    // (电压 mV, 剩余百分比)，按电压从高到低
    private static readonly (int MilliVolts, int Percent)[] Points =
    {
        (4200, 100),
        (4150, 95),
        (4100, 88),
        (4050, 80),
        (4000, 71),
        (3950, 61),
        (3900, 55),
        (3850, 47),
        (3800, 39),
        (3750, 31),
        (3700, 23),
        (3650, 16),
        (3600, 10),
        (3550, 6),
        (3500, 3),
        (3450, 1),
        (3400, 0)
    };

    /// <summary>把电压换算成 0-100 的百分比，区间内线性插值，保证精确到个位。</summary>
    public static int FromMillivolts(int millivolts)
    {
        if (millivolts >= Points[0].MilliVolts) return 100;
        if (millivolts <= Points[^1].MilliVolts) return 0;

        for (int i = 0; i < Points.Length - 1; i++)
        {
            (int hiMv, int hiPct) = Points[i];
            (int loMv, int loPct) = Points[i + 1];

            if (millivolts > hiMv || millivolts < loMv) continue;

            double ratio = (double)(millivolts - loMv) / (hiMv - loMv);
            int percent = (int)Math.Round(loPct + ratio * (hiPct - loPct));
            return Math.Clamp(percent, 0, 100);
        }

        return 0;
    }
}
