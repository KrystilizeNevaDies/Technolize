using Silk.NET.Input;
using Technolize.World.Interaction;

namespace Technolize.Rendering.Graphics;

/// <summary>
/// Silk.NET-backed implementation of <see cref="IGameInput"/>. Subscribes to the keyboard's key-down
/// events to build a per-frame "pressed this frame" queue (drained by <see cref="GetKeysPressedThisFrame"/>),
/// and reads live key/mouse state for the held-state queries.
/// </summary>
public sealed class GameInput : IGameInput, IDisposable
{
    private readonly IKeyboard? _keyboard;
    private readonly IMouse? _mouse;
    private readonly List<GameKey> _pressedThisFrame = [];

    public GameInput(IInputContext input)
    {
        _keyboard = input.Keyboards.Count > 0 ? input.Keyboards[0] : null;
        _mouse = input.Mice.Count > 0 ? input.Mice[0] : null;

        if (_keyboard is not null)
        {
            _keyboard.KeyDown += OnKeyDown;
        }
    }

    private void OnKeyDown(IKeyboard keyboard, Key key, int scancode)
    {
        if (TryMapKey(key, out GameKey gameKey))
        {
            _pressedThisFrame.Add(gameKey);
        }
    }

    public IReadOnlyList<GameKey> GetKeysPressedThisFrame()
    {
        if (_pressedThisFrame.Count == 0)
        {
            return Array.Empty<GameKey>();
        }

        GameKey[] snapshot = _pressedThisFrame.ToArray();
        _pressedThisFrame.Clear();
        return snapshot;
    }

    public bool IsKeyDown(GameKey key)
    {
        return _keyboard is not null && _keyboard.IsKeyPressed(MapKey(key));
    }

    public bool IsMouseButtonDown(GameMouseButton button)
    {
        return _mouse is not null && _mouse.IsButtonPressed(MapButton(button));
    }

    private static bool TryMapKey(Key key, out GameKey gameKey)
    {
        switch (key)
        {
            case >= Key.Number1 and <= Key.Number9:
                gameKey = GameKey.Num1 + (key - Key.Number1);
                return true;
            case Key.Up: gameKey = GameKey.Up; return true;
            case Key.Down: gameKey = GameKey.Down; return true;
            case Key.Left: gameKey = GameKey.Left; return true;
            case Key.Right: gameKey = GameKey.Right; return true;
            case Key.B: gameKey = GameKey.B; return true;
            case Key.Home: gameKey = GameKey.Home; return true;
            case Key.PageUp: gameKey = GameKey.PageUp; return true;
            case Key.PageDown: gameKey = GameKey.PageDown; return true;
            default: gameKey = default; return false;
        }
    }

    private static Key MapKey(GameKey key) => key switch
    {
        >= GameKey.Num1 and <= GameKey.Num9 => Key.Number1 + (key - GameKey.Num1),
        GameKey.Up => Key.Up,
        GameKey.Down => Key.Down,
        GameKey.Left => Key.Left,
        GameKey.Right => Key.Right,
        GameKey.B => Key.B,
        GameKey.Home => Key.Home,
        GameKey.PageUp => Key.PageUp,
        GameKey.PageDown => Key.PageDown,
        _ => Key.Unknown,
    };

    private static MouseButton MapButton(GameMouseButton button) => button switch
    {
        GameMouseButton.Left => MouseButton.Left,
        GameMouseButton.Right => MouseButton.Right,
        _ => MouseButton.Left,
    };

    public void Dispose()
    {
        if (_keyboard is not null)
        {
            _keyboard.KeyDown -= OnKeyDown;
        }
    }
}
