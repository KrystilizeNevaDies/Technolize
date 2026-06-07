using System.Numerics;
using Technolize.Rendering;
using Technolize.Runtime;
using Technolize.World;
using Technolize.World.Interaction;

namespace Technolize.Test.World;

/// <summary>
/// Tests that <see cref="DevInteractions"/> responds to input through the backend-agnostic
/// <see cref="IGameInput"/> abstraction, using a scripted fake input. This pins the behaviour any
/// input adapter must reproduce.
/// </summary>
[TestFixture]
public class DevInteractionsInputTest
{
    private sealed class FakeGameInput : IGameInput
    {
        public List<GameKey> PressedThisFrame { get; } = [];
        public HashSet<GameKey> Held { get; } = [];
        public HashSet<GameMouseButton> MouseHeld { get; } = [];

        public IReadOnlyList<GameKey> GetKeysPressedThisFrame() => PressedThisFrame;
        public bool IsKeyDown(GameKey key) => Held.Contains(key);
        public bool IsMouseButtonDown(GameMouseButton button) => MouseHeld.Contains(button);
    }

    private sealed class FakeWorldRenderer : IWorldRenderer
    {
        public bool ShowScheduledRegionOverlay { get; set; }
        public WorldLighting Lighting { get; set; } = WorldLighting.Default;
        public void UpdateCamera() { }
        public void Draw() { }
        public (Vector2 start, Vector2 end) GetVisibleWorldBounds() => (Vector2.Zero, Vector2.One);
        public Vector2 GetMouseWorldPosition() => Vector2.Zero;
        public void Dispose() { }
    }

    private static DevInteractions CreateInteractions(FakeGameInput input)
    {
        IWorldRenderer renderer = new FakeWorldRenderer();
        return new DevInteractions(new WorldCommandQueue(), renderer, input);
    }

    [Test]
    public void NumberKeys_SelectHotbarSlot()
    {
        FakeGameInput input = new();
        DevInteractions interactions = CreateInteractions(input);

        input.PressedThisFrame.Add(GameKey.Num5);
        interactions.Tick();

        Assert.That(interactions.SelectedHotbarIndex, Is.EqualTo(4), "Num5 selects hotbar slot index 4");
    }

    [Test]
    public void UpDownKeys_AdjustBrushSize()
    {
        FakeGameInput input = new();
        DevInteractions interactions = CreateInteractions(input);
        int initial = interactions.BrushSize;

        input.PressedThisFrame.Add(GameKey.Up);
        interactions.Tick();
        Assert.That(interactions.BrushSize, Is.EqualTo(initial + 1));

        input.PressedThisFrame.Clear();
        input.PressedThisFrame.Add(GameKey.Down);
        interactions.Tick();
        Assert.That(interactions.BrushSize, Is.EqualTo(initial));
    }

    [Test]
    public void BKey_CyclesBrushShape()
    {
        FakeGameInput input = new();
        DevInteractions interactions = CreateInteractions(input);
        BrushShape before = interactions.SelectedBrush;

        input.PressedThisFrame.Add(GameKey.B);
        interactions.Tick();

        Assert.That(interactions.SelectedBrush, Is.Not.EqualTo(before), "B cycles to the next brush shape");
    }

    [Test]
    public void PageKeys_AdjustSunRayCount()
    {
        FakeGameInput input = new();
        DevInteractions interactions = CreateInteractions(input);
        int initial = interactions.GetSunRayCount();

        input.PressedThisFrame.Add(GameKey.PageUp);
        interactions.Tick();
        Assert.That(interactions.GetSunRayCount(), Is.EqualTo(initial + 1));

        input.PressedThisFrame.Clear();
        input.PressedThisFrame.Add(GameKey.PageDown);
        interactions.Tick();
        Assert.That(interactions.GetSunRayCount(), Is.EqualTo(initial));
    }

    [Test]
    public void HeldArrowKey_RotatesSun()
    {
        FakeGameInput input = new();
        DevInteractions interactions = CreateInteractions(input);
        Vector2 before = interactions.GetSunDirection();

        input.Held.Add(GameKey.Right);
        interactions.Tick();

        Assert.That(interactions.GetSunDirection(), Is.Not.EqualTo(before), "Holding Right rotates the sun direction");
    }
}
