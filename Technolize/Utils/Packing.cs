using Raylib_cs;
namespace Technolize.Utils;

public static class Packing
{
    public static Color PackIntToColor(int value)
    {
        return new Color(
            (byte)(value & 0xFF),
            (byte)(value >> 8 & 0xFF),
            (byte)(value >> 16 & 0xFF)
        );
    }

    public static int UnpackColorToInt(Color color)
    {
        return color.R | color.G << 8 | color.B << 16;
    }
}
