using System.Numerics;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.Runtime;
using Technolize.World.Block;
namespace Technolize.World.Interaction;

public class DevInteractions(WorldCommandQueue worldCommands, IWorldRenderer renderer) {
    private static readonly IReadOnlyList<BlockInfo> AvailableBlocks = Blocks.AllBlocks().OrderBy(b => b.Id).ToArray();
    private readonly uint[] _hotbar = AvailableBlocks.Take(9).Select(block => block.Id).ToArray();
    private static readonly BrushShape[] BrushShapes = [BrushShape.Circle, BrushShape.Square, BrushShape.Diamond];
    private const int MinBrushSize = 0;
    private const int MaxBrushSize = 100;
    private const float SunRotationStepRadians = 0.035f;
    private const int MinSunRayCount = 1;
    private const int MaxSunRayCount = 64;
    private int _selectedHotbarIndex;
    private int _selectedBrushIndex;

    public BlockInfo SelectedBlock {
        get => BlockRegistry.GetInfo(_hotbar[_selectedHotbarIndex]);
    }

    public IReadOnlyList<uint> Hotbar => _hotbar;
    public int SelectedHotbarIndex => _selectedHotbarIndex;
    public int BrushSize { get; private set; } = 10;
    public BrushShape SelectedBrush => BrushShapes[_selectedBrushIndex];

    public IReadOnlyList<BlockInfo> GetBlocks(string? filter = null)
    {
        if (string.IsNullOrWhiteSpace(filter))
        {
            return AvailableBlocks;
        }

        return AvailableBlocks
            .Where(block => block.GetTag(BlockInfo.TagDisplayName)?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true)
            .ToArray();
    }

    public void SelectHotbarSlot(int index)
    {
        if (index < 0 || index >= _hotbar.Length)
        {
            return;
        }

        _selectedHotbarIndex = index;
    }

    public void SetHotbarBlock(int index, BlockInfo block)
    {
        if (index < 0 || index >= _hotbar.Length)
        {
            return;
        }

        _hotbar[index] = block.Id;
        _selectedHotbarIndex = index;
    }

    public IReadOnlyList<BrushShape> GetBrushShapes()
    {
        return BrushShapes;
    }

    public Vector2 GetSunDirection()
    {
        return renderer.Lighting.SunDirection;
    }

    public int GetSunRayCount()
    {
        return renderer.Lighting.SunRayCount;
    }

    public void SelectBrush(BrushShape brush)
    {
        int index = Array.IndexOf(BrushShapes, brush);
        if (index >= 0)
        {
            _selectedBrushIndex = index;
        }
    }

    public void IncreaseBrushSize()
    {
        BrushSize = Math.Min(BrushSize + 1, MaxBrushSize);
    }

    public void DecreaseBrushSize()
    {
        BrushSize = Math.Max(BrushSize - 1, MinBrushSize);
    }

    public void RotateSunDirection(float angleRadians)
    {
        Vector2 currentDirection = renderer.Lighting.SunDirection;
        if (currentDirection.LengthSquared() <= 0.0001f)
        {
            currentDirection = WorldLighting.Default.SunDirection;
        }

        float sinAngle = MathF.Sin(angleRadians);
        float cosAngle = MathF.Cos(angleRadians);
        Vector2 rotatedDirection = new(
            currentDirection.X * cosAngle - currentDirection.Y * sinAngle,
            currentDirection.X * sinAngle + currentDirection.Y * cosAngle);

        renderer.Lighting = renderer.Lighting with
        {
            SunDirection = Vector2.Normalize(rotatedDirection)
        };
    }

    public void ResetSunDirection()
    {
        renderer.Lighting = renderer.Lighting with
        {
            SunDirection = WorldLighting.Default.SunDirection
        };
    }

    public void IncreaseSunRayCount()
    {
        renderer.Lighting = renderer.Lighting with
        {
            SunRayCount = Math.Min(renderer.Lighting.SunRayCount + 1, MaxSunRayCount)
        };
    }

    public void DecreaseSunRayCount()
    {
        renderer.Lighting = renderer.Lighting with
        {
            SunRayCount = Math.Max(renderer.Lighting.SunRayCount - 1, MinSunRayCount)
        };
    }

    public void Tick(bool suppressWorldInput = false) {

        int keyPressed = Raylib.GetKeyPressed();
        while (keyPressed != 0) {
            if (keyPressed >= (int)KeyboardKey.One && keyPressed <= (int)KeyboardKey.Nine) {
                SelectHotbarSlot(keyPressed - (int)KeyboardKey.One);
            }

            // brush size controls
            if (keyPressed == (int)KeyboardKey.Up) {
                IncreaseBrushSize();
            }
            if (keyPressed == (int)KeyboardKey.Down) {
                DecreaseBrushSize();
            }
            if (keyPressed == (int)KeyboardKey.B)
            {
                _selectedBrushIndex = (_selectedBrushIndex + 1) % BrushShapes.Length;
            }
            if (keyPressed == (int)KeyboardKey.Left)
            {
                RotateSunDirection(-SunRotationStepRadians);
            }
            if (keyPressed == (int)KeyboardKey.Right)
            {
                RotateSunDirection(SunRotationStepRadians);
            }
            if (keyPressed == (int)KeyboardKey.Home)
            {
                ResetSunDirection();
            }
            if (keyPressed == (int)KeyboardKey.PageUp)
            {
                IncreaseSunRayCount();
            }
            if (keyPressed == (int)KeyboardKey.PageDown)
            {
                DecreaseSunRayCount();
            }

            keyPressed = Raylib.GetKeyPressed();
        }

        if (Raylib.IsKeyDown(KeyboardKey.Left))
        {
            RotateSunDirection(-SunRotationStepRadians);
        }

        if (Raylib.IsKeyDown(KeyboardKey.Right))
        {
            RotateSunDirection(SunRotationStepRadians);
        }

        if (suppressWorldInput)
        {
            return;
        }

        if (Raylib.IsMouseButtonDown(MouseButton.Right))
        {
            // Get the click position in world coordinates from the renderer.
            Vector2 worldPos = renderer.GetMouseWorldPosition();

            // Floor the coordinates to get the integer grid cell that was clicked.
            int centerX = (int) Math.Floor(worldPos.X);
            int centerY = (int) Math.Floor(worldPos.Y);

            uint selectedBlockId = SelectedBlock;
            int brushSize = BrushSize;
            BrushShape brush = SelectedBrush;
            worldCommands.Enqueue(world => world.BatchSetBlocks(placer => {
                for (int x = centerX - brushSize; x <= centerX + brushSize; x++)
                {
                    for (int y = centerY - brushSize; y <= centerY + brushSize; y++)
                    {
                        int dx = x - centerX;
                        int dy = y - centerY;
                        if (ShouldPlaceAt(brush, brushSize, dx, dy))
                        {
                            placer.Set(new (x, y), selectedBlockId);
                        }
                    }
                }
            }));
        }
    }

    private static bool ShouldPlaceAt(BrushShape brush, int brushSize, int dx, int dy)
    {
        return brush switch
        {
            BrushShape.Circle => dx * dx + dy * dy <= brushSize * brushSize,
            BrushShape.Square => Math.Abs(dx) <= brushSize && Math.Abs(dy) <= brushSize,
            BrushShape.Diamond => Math.Abs(dx) + Math.Abs(dy) <= brushSize,
            _ => false
        };
    }
}

public enum BrushShape
{
    Circle,
    Square,
    Diamond
}
