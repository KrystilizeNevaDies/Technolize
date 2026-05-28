using Raylib_cs;
namespace Technolize.Utils;

public static class Packing
{
    public static Color PackUIntToColor(uint value)
    {
        return new (
            (byte)(value & 0xFF),
            (byte)(value >> 8 & 0xFF),
            (byte)(value >> 16 & 0xFF),
            (byte)(value >> 24 & 0xFF)
        );
    }

    public static Color PackIntToColor(int value)
    {
        return PackUIntToColor(checked((uint)value));
    }

    public static uint UnpackColorToUInt(Color color)
    {
        return (uint)(color.R |
            color.G << 8 |
            color.B << 16 |
            color.A << 24);
    }

    public static int UnpackColorToInt(Color color)
    {
        return checked((int)UnpackColorToUInt(color));
    }
}
