namespace Technolize.World.Interaction;

/// <summary>
/// Backend-agnostic keyboard keys used by gameplay interactions. Only the keys the interaction layer
/// actually consumes are modelled here.
/// </summary>
public enum GameKey
{
    Num1,
    Num2,
    Num3,
    Num4,
    Num5,
    Num6,
    Num7,
    Num8,
    Num9,
    Up,
    Down,
    Left,
    Right,
    B,
    Home,
    PageUp,
    PageDown,
}

/// <summary>Backend-agnostic mouse buttons used by gameplay interactions.</summary>
public enum GameMouseButton
{
    Left,
    Right,
}

/// <summary>
/// Abstraction over the per-frame input state the gameplay interaction layer needs, so
/// <see cref="DevInteractions"/> stays independent of any specific input backend.
/// </summary>
public interface IGameInput
{
    /// <summary>
    /// Returns the keys that transitioned to pressed during this frame (the drained "key pressed"
    /// queue), mapped to <see cref="GameKey"/>. Keys not modelled by <see cref="GameKey"/> are omitted.
    /// </summary>
    IReadOnlyList<GameKey> GetKeysPressedThisFrame();

    /// <summary>Whether the given key is currently held down.</summary>
    bool IsKeyDown(GameKey key);

    /// <summary>Whether the given mouse button is currently held down.</summary>
    bool IsMouseButtonDown(GameMouseButton button);
}
