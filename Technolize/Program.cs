using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Raylib_cs;
using Technolize.Rendering;
using Technolize.Runtime;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation.Noise;
using Technolize.World.Interaction;
using Technolize.World.Ticking;

namespace Technolize;

public static class Program
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const int TargetRenderFramesPerSecond = 120;
    private const double InitialTicksPerSecond = 60.0;

    public static void Main()
    {
        Raylib.SetTraceLogLevel(TraceLogLevel.Warning);
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(ScreenWidth, ScreenHeight, "Technolize");
        Raylib.SetExitKey((KeyboardKey)0);
        Raylib.SetTargetFPS(TargetRenderFramesPerSecond);

        SaveGameStore saveGameStore = new();
        AppSettings settings = new();
        AppScreen screen = AppScreen.MainMenu;
        GameSession? gameSession = null;

        while (!Raylib.WindowShouldClose())
        {
            switch (screen)
            {
                case AppScreen.MainMenu:
                    screen = HandleMainMenu(saveGameStore, settings, ref gameSession);
                    break;
                case AppScreen.SaveMenu:
                    screen = HandleSaveMenu(saveGameStore, settings, ref gameSession);
                    break;
                case AppScreen.Settings:
                    screen = HandleSettingsMenu(settings, gameSession);
                    break;
                case AppScreen.InGame:
                    screen = HandleInGame(saveGameStore, ref gameSession);
                    break;
            }
        }

        gameSession?.Dispose();
        Raylib.CloseWindow();
    }

    private static AppScreen HandleMainMenu(SaveGameStore saveGameStore, AppSettings settings, ref GameSession? gameSession)
    {
        Rectangle playButton = new(Raylib.GetScreenWidth() / 2f - 150, 250, 300, 64);
        Rectangle unlocksButton = new(playButton.X, playButton.Y + 88, playButton.Width, playButton.Height);
        Rectangle settingsButton = new(playButton.X, unlocksButton.Y + 88, playButton.Width, playButton.Height);

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(mouse, playButton))
            {
                if (saveGameStore.HasCurrentSave())
                {
                    return AppScreen.SaveMenu;
                }

                SaveGameMetadata save = saveGameStore.CreateNewSave();
                ReplaceGameSession(ref gameSession, save, settings);
                return AppScreen.InGame;
            }

            if (Raylib.CheckCollisionPointRec(mouse, settingsButton))
            {
                return AppScreen.Settings;
            }
        }

        Raylib.BeginDrawing();
        DrawMenuBackground();
        DrawMenuTitle("Technolize", "Reactive ant-world prototype");
        DrawMenuButton(playButton, "Play", true);
        DrawMenuButton(unlocksButton, "Unlocks", false);
        DrawMenuButton(settingsButton, "Settings", true);
        Raylib.EndDrawing();

        return AppScreen.MainMenu;
    }

    private static AppScreen HandleSaveMenu(SaveGameStore saveGameStore, AppSettings settings, ref GameSession? gameSession)
    {
        Rectangle continueButton = new(Raylib.GetScreenWidth() / 2f - 170, 220, 340, 56);
        Rectangle newGameButton = new(continueButton.X, continueButton.Y + 76, continueButton.Width, continueButton.Height);
        Rectangle deleteButton = new(continueButton.X, newGameButton.Y + 76, continueButton.Width, continueButton.Height);
        Rectangle backButton = new(continueButton.X, deleteButton.Y + 76, continueButton.Width, continueButton.Height);

        bool hasSave = saveGameStore.TryLoadCurrentSave(out SaveGameMetadata? save);

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (hasSave && Raylib.CheckCollisionPointRec(mouse, continueButton))
            {
                if (gameSession is null)
                {
                    ReplaceGameSession(ref gameSession, save!, settings);
                }

                gameSession!.SetPlaybackMode(PlaybackMode.Play);
                return AppScreen.InGame;
            }

            if (Raylib.CheckCollisionPointRec(mouse, newGameButton))
            {
                SaveGameMetadata newSave = saveGameStore.CreateNewSave();
                ReplaceGameSession(ref gameSession, newSave, settings);
                return AppScreen.InGame;
            }

            if (Raylib.CheckCollisionPointRec(mouse, deleteButton))
            {
                saveGameStore.DeleteCurrentSave();
                DisposeGameSession(ref gameSession);
                return AppScreen.MainMenu;
            }

            if (Raylib.CheckCollisionPointRec(mouse, backButton))
            {
                return AppScreen.MainMenu;
            }
        }

        Raylib.BeginDrawing();
        DrawMenuBackground();
        DrawMenuTitle("Current Save", hasSave && save is not null
            ? $"Seed {save.WorldSeed}"
            : "No save slot yet");
        DrawMenuButton(continueButton, "Continue", hasSave);
        DrawMenuButton(newGameButton, "New Save", true);
        DrawMenuButton(deleteButton, "Delete Saves", hasSave);
        DrawMenuButton(backButton, "Back", true);
        Raylib.EndDrawing();

        return AppScreen.SaveMenu;
    }

    private static AppScreen HandleSettingsMenu(AppSettings settings, GameSession? gameSession)
    {
        Rectangle overlayToggleButton = new(Raylib.GetScreenWidth() / 2f - 210, 250, 420, 68);
        Rectangle backButton = new(overlayToggleButton.X, overlayToggleButton.Y + 92, overlayToggleButton.Width, 56);

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            return AppScreen.MainMenu;
        }

        if (Raylib.IsMouseButtonPressed(MouseButton.Left))
        {
            Vector2 mouse = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(mouse, overlayToggleButton))
            {
                settings.ShowScheduledRegionOverlay = !settings.ShowScheduledRegionOverlay;
                gameSession?.ApplySettings(settings);
            }

            if (Raylib.CheckCollisionPointRec(mouse, backButton))
            {
                return AppScreen.MainMenu;
            }
        }

        Raylib.BeginDrawing();
        DrawMenuBackground();
        DrawMenuTitle("Settings", "Rendering and debug options");
        DrawSettingsToggleButton(overlayToggleButton, "Tick Overlay", settings.ShowScheduledRegionOverlay, "Highlight regions scheduled for the next tick");
        DrawMenuButton(backButton, "Back", true);
        Raylib.EndDrawing();

        return AppScreen.Settings;
    }

    private static AppScreen HandleInGame(SaveGameStore saveGameStore, ref GameSession? gameSession)
    {
        if (gameSession is null)
        {
            return saveGameStore.HasCurrentSave() ? AppScreen.SaveMenu : AppScreen.MainMenu;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            if (gameSession.CloseInventory())
            {
                return AppScreen.InGame;
            }

            gameSession.SetPlaybackMode(PlaybackMode.Pause);
            return AppScreen.SaveMenu;
        }

        gameSession.DrawFrame();
        return AppScreen.InGame;
    }

    private static void ReplaceGameSession(ref GameSession? gameSession, SaveGameMetadata save, AppSettings settings)
    {
        DisposeGameSession(ref gameSession);
        gameSession = new GameSession(save.WorldSeed, settings);
        gameSession.SetPlaybackMode(PlaybackMode.Play);
    }

    private static void DisposeGameSession(ref GameSession? gameSession)
    {
        gameSession?.Dispose();
        gameSession = null;
    }

    private static void DrawMenuBackground()
    {
        Raylib.ClearBackground(new Color(14, 18, 24, 255));
        Raylib.DrawRectangleGradientV(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(18, 28, 38, 255), new Color(7, 10, 14, 255));
        Raylib.DrawCircleGradient(Raylib.GetScreenWidth() - 180, 110, 220, new Color(180, 110, 60, 70), Color.Blank);
        Raylib.DrawCircleGradient(130, Raylib.GetScreenHeight() - 80, 180, new Color(70, 120, 95, 40), Color.Blank);
    }

    private static void DrawMenuTitle(string title, string subtitle)
    {
        int titleSize = 48;
        int subtitleSize = 22;
        Vector2 titleMeasure = Raylib.MeasureTextEx(Raylib.GetFontDefault(), title, titleSize, 1);
        Vector2 subtitleMeasure = Raylib.MeasureTextEx(Raylib.GetFontDefault(), subtitle, subtitleSize, 1);

        int titleX = (int)(Raylib.GetScreenWidth() / 2f - titleMeasure.X / 2f);
        int subtitleX = (int)(Raylib.GetScreenWidth() / 2f - subtitleMeasure.X / 2f);

        Raylib.DrawText(title, titleX, 110, titleSize, new Color(244, 236, 222, 255));
        Raylib.DrawText(subtitle, subtitleX, 168, subtitleSize, new Color(170, 176, 182, 255));
    }

    private static void DrawMenuButton(Rectangle button, string label, bool enabled)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool hovered = enabled && Raylib.CheckCollisionPointRec(mouse, button);
        Color fill = enabled
            ? hovered ? new Color(116, 88, 60, 255) : new Color(73, 56, 41, 255)
            : new Color(42, 44, 48, 220);
        Color border = enabled ? new Color(222, 187, 120, 255) : new Color(86, 90, 96, 255);
        Color text = enabled ? new Color(246, 239, 229, 255) : new Color(120, 124, 130, 255);

        Raylib.DrawRectangleRounded(button, 0.22f, 8, fill);
        Raylib.DrawRectangleRoundedLinesEx(button, 0.22f, 8, 2.0f, border);

        int fontSize = 28;
        Vector2 size = Raylib.MeasureTextEx(Raylib.GetFontDefault(), label, fontSize, 1);
        int textX = (int)(button.X + (button.Width - size.X) / 2);
        int textY = (int)(button.Y + (button.Height - size.Y) / 2);
        Raylib.DrawText(label, textX, textY, fontSize, text);
    }

    private static void DrawSettingsToggleButton(Rectangle button, string label, bool value, string description)
    {
        Vector2 mouse = Raylib.GetMousePosition();
        bool hovered = Raylib.CheckCollisionPointRec(mouse, button);
        Color fill = hovered ? new Color(87, 64, 46, 255) : new Color(61, 48, 37, 255);
        Color border = value ? new Color(230, 211, 143, 255) : new Color(110, 118, 128, 255);
        Color titleColor = new Color(244, 236, 222, 255);
        Color descriptionColor = new Color(164, 171, 178, 255);
        Color valueColor = value ? new Color(228, 210, 136, 255) : new Color(138, 145, 153, 255);

        Raylib.DrawRectangleRounded(button, 0.18f, 8, fill);
        Raylib.DrawRectangleRoundedLinesEx(button, 0.18f, 8, 2.0f, border);
        Raylib.DrawText(label, (int)button.X + 20, (int)button.Y + 14, 26, titleColor);
        Raylib.DrawText(description, (int)button.X + 20, (int)button.Y + 42, 18, descriptionColor);

        string valueLabel = value ? "On" : "Off";
        Vector2 valueSize = Raylib.MeasureTextEx(Raylib.GetFontDefault(), valueLabel, 28, 1);
        Raylib.DrawText(valueLabel, (int)(button.X + button.Width - valueSize.X - 22), (int)(button.Y + (button.Height - valueSize.Y) / 2), 28, valueColor);
    }

    private static Rectangle GetPlaybackPanelBounds()
    {
        return new Rectangle(Raylib.GetScreenWidth() - 336, 20, 316, 68);
    }

    private static Rectangle GetHotbarPanelBounds()
    {
        float width = 9 * 58 + 16;
        return new Rectangle((Raylib.GetScreenWidth() - width) / 2f, Raylib.GetScreenHeight() - 86, width, 70);
    }

    private static Rectangle GetInventoryPanelBounds()
    {
        float width = Math.Min(860, Raylib.GetScreenWidth() - 80);
        float height = Math.Min(540, Raylib.GetScreenHeight() - 120);
        return new Rectangle((Raylib.GetScreenWidth() - width) / 2f, (Raylib.GetScreenHeight() - height) / 2f, width, height);
    }

    private static Rectangle GetBrushPanelBounds()
    {
        return new Rectangle(20, Raylib.GetScreenHeight() - 110, 240, 94);
    }

    private static Rectangle GetBrushDecreaseButtonBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + 16, panel.Y + 58, 26, 22);
    }

    private static Rectangle GetBrushIncreaseButtonBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + 46, panel.Y + 58, 26, 22);
    }

    private static IEnumerable<(PlaybackMode mode, Rectangle bounds)> GetPlaybackButtons(Rectangle panel)
    {
        const float buttonWidth = 52;
        const float buttonHeight = 44;
        const float gap = 8;
        float x = panel.X + 12;
        float y = panel.Y + 12;

        yield return (PlaybackMode.Pause, new Rectangle(x + 0 * (buttonWidth + gap), y, buttonWidth, buttonHeight));
        yield return (PlaybackMode.Play, new Rectangle(x + 1 * (buttonWidth + gap), y, buttonWidth, buttonHeight));
        yield return (PlaybackMode.Fast, new Rectangle(x + 2 * (buttonWidth + gap), y, buttonWidth, buttonHeight));
        yield return (PlaybackMode.Fastest, new Rectangle(x + 3 * (buttonWidth + gap), y, buttonWidth, buttonHeight));
    }

    private static Rectangle GetSingleTickButtonBounds(Rectangle panel)
    {
        const float buttonWidth = 52;
        const float buttonHeight = 44;
        const float gap = 8;
        float x = panel.X + 12 + 4 * (buttonWidth + gap);
        float y = panel.Y + 12;
        return new Rectangle(x, y, buttonWidth, buttonHeight);
    }

    private static void DrawPlaybackControls(PlaybackMode selectedMode)
    {
        Rectangle panel = GetPlaybackPanelBounds();
        Raylib.DrawRectangleRounded(panel, 0.25f, 8, new Color(16, 20, 26, 220));
        Raylib.DrawRectangleRoundedLinesEx(panel, 0.25f, 8, 2.0f, new Color(98, 120, 136, 255));

        foreach ((PlaybackMode mode, Rectangle button) in GetPlaybackButtons(panel))
        {
            bool selected = mode == selectedMode;
            Color fill = selected ? new Color(201, 139, 74, 255) : new Color(40, 48, 58, 255);
            Color border = selected ? new Color(247, 216, 175, 255) : new Color(88, 101, 114, 255);
            Color icon = selected ? new Color(20, 18, 15, 255) : new Color(235, 239, 242, 255);

            Raylib.DrawRectangleRounded(button, 0.24f, 6, fill);
            Raylib.DrawRectangleRoundedLinesEx(button, 0.24f, 6, 1.8f, border);
            DrawPlaybackIcon(mode, button, icon);
        }

        Rectangle singleTickButton = GetSingleTickButtonBounds(panel);
        bool singleTickEnabled = selectedMode == PlaybackMode.Pause;
        Color singleTickFill = singleTickEnabled ? new Color(100, 116, 68, 255) : new Color(40, 48, 58, 255);
        Color singleTickBorder = singleTickEnabled ? new Color(214, 232, 166, 255) : new Color(88, 101, 114, 255);
        Color singleTickIcon = singleTickEnabled ? new Color(20, 18, 15, 255) : new Color(142, 152, 162, 255);

        Raylib.DrawRectangleRounded(singleTickButton, 0.24f, 6, singleTickFill);
        Raylib.DrawRectangleRoundedLinesEx(singleTickButton, 0.24f, 6, 1.8f, singleTickBorder);
        DrawSingleTickIcon(singleTickButton, singleTickIcon);
    }

    private static IEnumerable<(int slotIndex, Rectangle button)> GetHotbarButtons(Rectangle panel)
    {
        const float slotSize = 50;
        const float gap = 8;
        float x = panel.X + 8;
        float y = panel.Y + 10;

        for (int index = 0; index < 9; index++)
        {
            yield return (index, new Rectangle(x + index * (slotSize + gap), y, slotSize, slotSize));
        }
    }

    private static void DrawHotbar(DevInteractions interactions)
    {
        Rectangle panel = GetHotbarPanelBounds();
        Raylib.DrawRectangleRounded(panel, 0.28f, 8, new Color(14, 18, 24, 220));
        Raylib.DrawRectangleRoundedLinesEx(panel, 0.28f, 8, 2.0f, new Color(88, 100, 114, 255));

        foreach ((int slotIndex, Rectangle button) in GetHotbarButtons(panel))
        {
            BlockInfo block = BlockRegistry.GetInfo(interactions.Hotbar[slotIndex]);
            bool selected = slotIndex == interactions.SelectedHotbarIndex;
            Color blockColor = block.GetTag(BlockInfo.TagColor);
            Color fill = selected ? new Color(222, 191, 130, 255) : new Color(34, 40, 48, 255);
            Color border = selected ? new Color(255, 233, 193, 255) : new Color(92, 102, 114, 255);

            Raylib.DrawRectangleRounded(button, 0.18f, 6, fill);
            Raylib.DrawRectangleRoundedLinesEx(button, 0.18f, 6, 2.0f, border);

            Rectangle swatch = new(button.X + 7, button.Y + 7, button.Width - 14, button.Height - 22);
            Raylib.DrawRectangleRounded(swatch, 0.18f, 6, blockColor);
            Raylib.DrawRectangleRoundedLinesEx(swatch, 0.18f, 6, 1.2f, new Color(10, 12, 14, 180));

            string slotLabel = (slotIndex + 1).ToString();
            Raylib.DrawText(slotLabel, (int)button.X + 6, (int)(button.Y + button.Height - 14), 12, new Color(16, 18, 20, 220));
        }
    }

    private static IEnumerable<(BrushShape brush, Rectangle button)> GetBrushButtons(Rectangle panel, IReadOnlyList<BrushShape> brushes)
    {
        const float buttonWidth = 50;
        const float buttonHeight = 50;
        const float gap = 10;
        float x = panel.X + 88;
        float y = panel.Y + 26;

        for (int index = 0; index < brushes.Count; index++)
        {
            yield return (brushes[index], new Rectangle(x + index * (buttonWidth + gap), y, buttonWidth, buttonHeight));
        }
    }

    private static void DrawBrushControls(DevInteractions interactions)
    {
        Rectangle panel = GetBrushPanelBounds();
        Raylib.DrawRectangleRounded(panel, 0.22f, 8, new Color(14, 18, 24, 220));
        Raylib.DrawRectangleRoundedLinesEx(panel, 0.22f, 8, 2.0f, new Color(88, 100, 114, 255));

        Raylib.DrawText("Brush", (int)panel.X + 16, (int)panel.Y + 14, 18, new Color(236, 240, 243, 255));
        Raylib.DrawText($"Size {interactions.BrushSize}", (int)panel.X + 16, (int)panel.Y + 38, 16, new Color(160, 169, 178, 255));

        Rectangle decreaseButton = GetBrushDecreaseButtonBounds(panel);
        Rectangle increaseButton = GetBrushIncreaseButtonBounds(panel);
        DrawBrushSizeButton(decreaseButton, "-");
        DrawBrushSizeButton(increaseButton, "+");

        foreach ((BrushShape brush, Rectangle button) in GetBrushButtons(panel, interactions.GetBrushShapes()))
        {
            bool selected = interactions.SelectedBrush == brush;
            Color fill = selected ? new Color(222, 191, 130, 255) : new Color(34, 40, 48, 255);
            Color border = selected ? new Color(255, 233, 193, 255) : new Color(92, 102, 114, 255);
            Color icon = selected ? new Color(20, 18, 15, 255) : new Color(235, 239, 242, 255);

            Raylib.DrawRectangleRounded(button, 0.18f, 6, fill);
            Raylib.DrawRectangleRoundedLinesEx(button, 0.18f, 6, 1.8f, border);
            DrawBrushIcon(brush, button, icon);
        }
    }

    private static void DrawBrushSizeButton(Rectangle button, string label)
    {
        Raylib.DrawRectangleRounded(button, 0.22f, 6, new Color(34, 40, 48, 255));
        Raylib.DrawRectangleRoundedLinesEx(button, 0.22f, 6, 1.6f, new Color(92, 102, 114, 255));
        Raylib.DrawText(label, (int)button.X + 8, (int)button.Y + 1, 20, new Color(235, 239, 242, 255));
    }

    private static void DrawBrushIcon(BrushShape brush, Rectangle button, Color color)
    {
        switch (brush)
        {
            case BrushShape.Circle:
                Raylib.DrawCircle((int)(button.X + button.Width / 2), (int)(button.Y + button.Height / 2), 12, color);
                break;
            case BrushShape.Square:
                Raylib.DrawRectangle((int)button.X + 13, (int)button.Y + 13, 24, 24, color);
                break;
            case BrushShape.Diamond:
                Raylib.DrawTriangle(
                    new Vector2(button.X + button.Width / 2, button.Y + 10),
                    new Vector2(button.X + button.Width - 10, button.Y + button.Height / 2),
                    new Vector2(button.X + button.Width / 2, button.Y + button.Height - 10),
                    color);
                Raylib.DrawTriangle(
                    new Vector2(button.X + button.Width / 2, button.Y + 10),
                    new Vector2(button.X + 10, button.Y + button.Height / 2),
                    new Vector2(button.X + button.Width / 2, button.Y + button.Height - 10),
                    color);
                break;
        }
    }

    private static Rectangle GetInventorySearchBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + 24, panel.Y + 58, panel.Width - 48, 42);
    }

    private static Rectangle GetInventoryCloseButtonBounds(Rectangle panel)
    {
        return new Rectangle(panel.X + panel.Width - 52, panel.Y + 16, 28, 28);
    }

    private static IEnumerable<(BlockInfo block, Rectangle button)> GetInventoryButtons(Rectangle panel, IReadOnlyList<BlockInfo> blocks)
    {
        const float tileWidth = 120;
        const float tileHeight = 82;
        const float gap = 12;

        Rectangle search = GetInventorySearchBounds(panel);
        float startX = panel.X + 24;
        float startY = search.Y + search.Height + 20;
        int columns = Math.Max(1, (int)((panel.Width - 48 + gap) / (tileWidth + gap)));

        for (int index = 0; index < blocks.Count; index++)
        {
            int column = index % columns;
            int row = index / columns;
            Rectangle button = new(
                startX + column * (tileWidth + gap),
                startY + row * (tileHeight + gap),
                tileWidth,
                tileHeight);
            yield return (blocks[index], button);
        }
    }

    private static void DrawInventory(DevInteractions interactions, string searchText)
    {
        Rectangle panel = GetInventoryPanelBounds();
        IReadOnlyList<BlockInfo> blocks = interactions.GetBlocks(searchText);

        Raylib.DrawRectangle(0, 0, Raylib.GetScreenWidth(), Raylib.GetScreenHeight(), new Color(0, 0, 0, 120));
        Raylib.DrawRectangleRounded(panel, 0.08f, 10, new Color(17, 21, 28, 245));
        Raylib.DrawRectangleRoundedLinesEx(panel, 0.08f, 10, 2.0f, new Color(108, 123, 139, 255));

        Raylib.DrawText("Inventory", (int)panel.X + 24, (int)panel.Y + 18, 28, new Color(244, 236, 222, 255));
        Raylib.DrawText("Click a block to save it to the selected hotbar slot", (int)panel.X + 180, (int)panel.Y + 22, 18, new Color(160, 169, 178, 255));

        Rectangle closeButton = GetInventoryCloseButtonBounds(panel);
        Raylib.DrawRectangleRounded(closeButton, 0.24f, 6, new Color(42, 48, 56, 255));
        Raylib.DrawRectangleRoundedLinesEx(closeButton, 0.24f, 6, 1.5f, new Color(116, 126, 138, 255));
        Raylib.DrawLineEx(
            new Vector2(closeButton.X + 8, closeButton.Y + 8),
            new Vector2(closeButton.X + closeButton.Width - 8, closeButton.Y + closeButton.Height - 8),
            2.0f,
            new Color(235, 239, 242, 255));
        Raylib.DrawLineEx(
            new Vector2(closeButton.X + closeButton.Width - 8, closeButton.Y + 8),
            new Vector2(closeButton.X + 8, closeButton.Y + closeButton.Height - 8),
            2.0f,
            new Color(235, 239, 242, 255));

        Rectangle searchBox = GetInventorySearchBounds(panel);
        Raylib.DrawRectangleRounded(searchBox, 0.18f, 6, new Color(30, 36, 44, 255));
        Raylib.DrawRectangleRoundedLinesEx(searchBox, 0.18f, 6, 1.6f, new Color(95, 108, 121, 255));
        string searchLabel = string.IsNullOrEmpty(searchText) ? "Search blocks..." : searchText;
        Color searchColor = string.IsNullOrEmpty(searchText) ? new Color(130, 140, 148, 255) : new Color(238, 242, 245, 255);
        Raylib.DrawText(searchLabel, (int)searchBox.X + 14, (int)searchBox.Y + 11, 20, searchColor);

        foreach ((BlockInfo block, Rectangle button) in GetInventoryButtons(panel, blocks))
        {
            Color blockColor = block.GetTag(BlockInfo.TagColor);
            string name = block.GetTag(BlockInfo.TagDisplayName) ?? $"Block {block.Id}";

            Raylib.DrawRectangleRounded(button, 0.16f, 6, new Color(36, 42, 50, 255));
            Raylib.DrawRectangleRoundedLinesEx(button, 0.16f, 6, 1.5f, new Color(88, 101, 114, 255));

            Rectangle swatch = new(button.X + 10, button.Y + 10, 30, 30);
            Raylib.DrawRectangleRounded(swatch, 0.22f, 4, blockColor);
            Raylib.DrawRectangleRoundedLinesEx(swatch, 0.22f, 4, 1.0f, new Color(10, 12, 14, 180));
            Raylib.DrawText(name, (int)button.X + 48, (int)button.Y + 12, 20, new Color(236, 240, 243, 255));
            Raylib.DrawText($"ID {block.Id}", (int)button.X + 48, (int)button.Y + 42, 15, new Color(144, 152, 160, 255));
        }

        if (blocks.Count == 0)
        {
            Raylib.DrawText("No blocks match that search.", (int)panel.X + 24, (int)searchBox.Y + 70, 20, new Color(160, 169, 178, 255));
        }
    }

    private static void DrawPlaybackIcon(PlaybackMode mode, Rectangle button, Color color)
    {
        float centerY = button.Y + button.Height / 2f;

        switch (mode)
        {
            case PlaybackMode.Pause:
                Raylib.DrawRectangle((int)(button.X + 16), (int)(button.Y + 12), 7, 20, color);
                Raylib.DrawRectangle((int)(button.X + 29), (int)(button.Y + 12), 7, 20, color);
                break;
            case PlaybackMode.Play:
                DrawTriangleIcon(button.X + 18, centerY, 18, color);
                break;
            case PlaybackMode.Fast:
                DrawTriangleIcon(button.X + 10, centerY, 14, color);
                DrawTriangleIcon(button.X + 24, centerY, 14, color);
                break;
            case PlaybackMode.Fastest:
                DrawTriangleIcon(button.X + 6, centerY, 12, color);
                DrawTriangleIcon(button.X + 18, centerY, 12, color);
                DrawTriangleIcon(button.X + 30, centerY, 12, color);
                break;
        }
    }

    private static void DrawTriangleIcon(float x, float centerY, float size, Color color)
    {
        Raylib.DrawTriangle(
            new Vector2(x, centerY - size * 0.8f),
            new Vector2(x, centerY + size * 0.8f),
            new Vector2(x + size, centerY),
            color);
    }

    private static void DrawSingleTickIcon(Rectangle button, Color color)
    {
        float centerY = button.Y + button.Height / 2f;
        Raylib.DrawRectangle((int)(button.X + 14), (int)(button.Y + 12), 4, 20, color);
        DrawTriangleIcon(button.X + 22, centerY, 14, color);
    }

    private static double GetPlaybackTicksPerSecond(PlaybackMode mode)
    {
        return mode switch
        {
            PlaybackMode.Pause => 0.0,
            PlaybackMode.Play => InitialTicksPerSecond,
            PlaybackMode.Fast => InitialTicksPerSecond * 4.0,
            PlaybackMode.Fastest => InitialTicksPerSecond * 64.0,
            _ => InitialTicksPerSecond
        };
    }

    private enum AppScreen
    {
        MainMenu,
        SaveMenu,
        Settings,
        InGame
    }

    private sealed class AppSettings
    {
        public bool ShowScheduledRegionOverlay { get; set; }
    }

    private enum PlaybackMode
    {
        Pause,
        Play,
        Fast,
        Fastest
    }

    private sealed class GameSession : IDisposable
    {
        private readonly TickableWorld _world;
        private readonly PublishedWorldRenderSource _renderSource;
        private readonly WorldCommandQueue _worldCommands;
        private readonly SimulationClockState _simulationClock;
        private readonly CancellationTokenSource _shutdown;
        private readonly Thread _simulationThread;
        private readonly IWorldRenderer _renderer;
        private readonly DevInteractions _interactions;

        private bool _inventoryOpen;
        private string _inventorySearch = string.Empty;
        private PlaybackMode _playbackMode = PlaybackMode.Play;

        public GameSession(int worldSeed, AppSettings settings)
        {
            _world = new TickableWorld
            {
                Generator = new SimpleNoiseGenerator(worldSeed)
            };
            SignatureWorldTicker ticker = new(_world);
            _renderSource = new PublishedWorldRenderSource();
            _worldCommands = new WorldCommandQueue();
            _simulationClock = new SimulationClockState(InitialTicksPerSecond);
            _shutdown = new CancellationTokenSource();

            _world.GetBlock(new Vector2(0, 0));
            _world.ProcessUpdate(new Vector2(0, 0));
            _renderSource.Publish(WorldRenderFrameBuilder.FromWorld(_world));

            _renderer = new WorldShaderRenderer(_renderSource, ScreenWidth, ScreenHeight);
            ApplySettings(settings);
            _interactions = new DevInteractions(_worldCommands, _renderer);
            _simulationThread = new Thread(() => RunSimulationLoop(_world, ticker, _renderSource, _worldCommands, _simulationClock, _shutdown.Token))
            {
                Name = "SimulationThread"
            };
            _simulationThread.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            _renderer.ShowScheduledRegionOverlay = settings.ShowScheduledRegionOverlay;
        }

        public void DrawFrame()
        {
            Rectangle playbackPanel = GetPlaybackPanelBounds();
            Rectangle hotbarPanel = GetHotbarPanelBounds();
            Rectangle brushPanel = GetBrushPanelBounds();
            Rectangle? inventoryPanel = _inventoryOpen ? GetInventoryPanelBounds() : null;
            Vector2 mousePosition = Raylib.GetMousePosition();
            bool uiHovered =
                Raylib.CheckCollisionPointRec(mousePosition, playbackPanel) ||
                Raylib.CheckCollisionPointRec(mousePosition, brushPanel) ||
                Raylib.CheckCollisionPointRec(mousePosition, hotbarPanel) ||
                (inventoryPanel.HasValue && Raylib.CheckCollisionPointRec(mousePosition, inventoryPanel.Value));

            HandleUiInput(playbackPanel, brushPanel, hotbarPanel, inventoryPanel);

            if (!uiHovered)
            {
                _renderer.UpdateCamera();
            }

            _interactions.Tick(uiHovered || _inventoryOpen);

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Color.Black);

            long renderStart = Stopwatch.GetTimestamp();
            _renderer.Draw();
            _simulationClock.RecordRenderFrame(Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);

            DrawPlaybackControls(_playbackMode);
            DrawBrushControls(_interactions);
            DrawHotbar(_interactions);
            if (_inventoryOpen)
            {
                DrawInventory(_interactions, _inventorySearch);
            }
            Raylib.EndDrawing();
        }

        public void SetPlaybackMode(PlaybackMode mode)
        {
            _playbackMode = mode;
            _simulationClock.SetTargetTicksPerSecond(GetPlaybackTicksPerSecond(mode));
        }

        public void AdvanceSingleTick()
        {
            if (_playbackMode == PlaybackMode.Pause)
            {
                _simulationClock.RequestSingleTick();
            }
        }

        public bool CloseInventory()
        {
            if (!_inventoryOpen)
            {
                return false;
            }

            _inventoryOpen = false;
            _inventorySearch = string.Empty;
            return true;
        }

        public void Dispose()
        {
            _shutdown.Cancel();
            _simulationThread.Join();
            _renderer.Dispose();
            _shutdown.Dispose();
            _world.Unload();
        }

        private void HandleUiInput(Rectangle playbackPanel, Rectangle brushPanel, Rectangle hotbarPanel, Rectangle? inventoryPanel)
        {
            bool inventoryOpened = false;
            if (!_inventoryOpen && Raylib.IsKeyPressed(KeyboardKey.E))
            {
                _inventoryOpen = true;
                inventoryOpened = true;
            }

            if (_inventoryOpen && !inventoryOpened)
            {
                HandleInventorySearchInput();
            }

            if (!Raylib.IsMouseButtonPressed(MouseButton.Left))
            {
                return;
            }

            Vector2 mouse = Raylib.GetMousePosition();
            if (Raylib.CheckCollisionPointRec(mouse, playbackPanel))
            {
                foreach ((PlaybackMode mode, Rectangle button) in GetPlaybackButtons(playbackPanel))
                {
                    if (Raylib.CheckCollisionPointRec(mouse, button))
                    {
                        SetPlaybackMode(mode);
                        return;
                    }
                }

                if (_playbackMode == PlaybackMode.Pause && Raylib.CheckCollisionPointRec(mouse, GetSingleTickButtonBounds(playbackPanel)))
                {
                    AdvanceSingleTick();
                    return;
                }
            }

            if (Raylib.CheckCollisionPointRec(mouse, brushPanel))
            {
                if (Raylib.CheckCollisionPointRec(mouse, GetBrushDecreaseButtonBounds(brushPanel)))
                {
                    _interactions.DecreaseBrushSize();
                    return;
                }

                if (Raylib.CheckCollisionPointRec(mouse, GetBrushIncreaseButtonBounds(brushPanel)))
                {
                    _interactions.IncreaseBrushSize();
                    return;
                }

                foreach ((BrushShape brush, Rectangle button) in GetBrushButtons(brushPanel, _interactions.GetBrushShapes()))
                {
                    if (Raylib.CheckCollisionPointRec(mouse, button))
                    {
                        _interactions.SelectBrush(brush);
                        return;
                    }
                }
            }

            if (Raylib.CheckCollisionPointRec(mouse, hotbarPanel))
            {
                foreach ((int slotIndex, Rectangle button) in GetHotbarButtons(hotbarPanel))
                {
                    if (Raylib.CheckCollisionPointRec(mouse, button))
                    {
                        _interactions.SelectHotbarSlot(slotIndex);
                        return;
                    }
                }
            }

            if (!_inventoryOpen || !inventoryPanel.HasValue || !Raylib.CheckCollisionPointRec(mouse, inventoryPanel.Value))
            {
                return;
            }

            if (Raylib.CheckCollisionPointRec(mouse, GetInventoryCloseButtonBounds(inventoryPanel.Value)))
            {
                CloseInventory();
                return;
            }

            foreach ((BlockInfo block, Rectangle button) in GetInventoryButtons(inventoryPanel.Value, _interactions.GetBlocks(_inventorySearch)))
            {
                if (Raylib.CheckCollisionPointRec(mouse, button))
                {
                    _interactions.SetHotbarBlock(_interactions.SelectedHotbarIndex, block);
                    return;
                }
            }
        }

        private void HandleInventorySearchInput()
        {
            int character = Raylib.GetCharPressed();
            while (character > 0)
            {
                if (!char.IsControl((char)character))
                {
                    _inventorySearch += (char)character;
                }

                character = Raylib.GetCharPressed();
            }

            if (Raylib.IsKeyPressed(KeyboardKey.Backspace) && _inventorySearch.Length > 0)
            {
                _inventorySearch = _inventorySearch[..^1];
            }
        }
    }

    private static void RunSimulationLoop(
        TickableWorld world,
        SignatureWorldTicker ticker,
        PublishedWorldRenderSource renderSource,
        WorldCommandQueue worldCommands,
        SimulationClockState simulationClock,
        CancellationToken shutdownToken)
    {
        Stopwatch simulationStopwatch = Stopwatch.StartNew();
        double nextTickAtSeconds = simulationStopwatch.Elapsed.TotalSeconds;

        while (!shutdownToken.IsCancellationRequested)
        {
            bool worldChanged = worldCommands.Drain(world);
            bool ticked = false;
            double targetTicksPerSecond = simulationClock.GetTargetTicksPerSecond();
            double nowSeconds = simulationStopwatch.Elapsed.TotalSeconds;

            if (targetTicksPerSecond <= 0.0)
            {
                nextTickAtSeconds = nowSeconds;

                if (simulationClock.TryConsumeSingleTick())
                {
                    RunSimulationTick(ticker, simulationClock);
                    ticked = true;
                }

                if (worldChanged || ticked)
                {
                    renderSource.Publish(WorldRenderFrameBuilder.FromWorld(world));
                }

                if (!ticked)
                {
                    Thread.Sleep(1);
                }
                continue;
            }

            double tickIntervalSeconds = 1.0 / targetTicksPerSecond;
            int catchUpTicks = 0;

            while (nowSeconds >= nextTickAtSeconds && catchUpTicks < 8 && !shutdownToken.IsCancellationRequested)
            {
                RunSimulationTick(ticker, simulationClock);

                ticked = true;
                catchUpTicks++;
                nextTickAtSeconds += tickIntervalSeconds;
                nowSeconds = simulationStopwatch.Elapsed.TotalSeconds;
            }

            if (nowSeconds - nextTickAtSeconds > tickIntervalSeconds * 4)
            {
                nextTickAtSeconds = nowSeconds;
            }

            if (worldChanged || ticked)
            {
                renderSource.Publish(WorldRenderFrameBuilder.FromWorld(world));
            }

            if (!ticked)
            {
                Thread.Sleep(1);
            }
        }
    }

    private static void RunSimulationTick(SignatureWorldTicker ticker, SimulationClockState simulationClock)
    {
        long simulationStart = Stopwatch.GetTimestamp();
        ticker.Tick();
        simulationClock.RecordSimulationTick(Stopwatch.GetElapsedTime(simulationStart).TotalMilliseconds);
    }
}
