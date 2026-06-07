using System.Diagnostics;
using System.Numerics;
using ImGuiNET;
using Technolize.Rendering;
using Technolize.Rendering.Graphics;
using Technolize.Runtime;
using Technolize.World;
using Technolize.World.Block;
using Technolize.World.Generation.Noise;
using Technolize.World.Interaction;
using Technolize.World.Ticking;
using TColor = Technolize.Utils.Color;

namespace Technolize;

/// <summary>
/// The Technolize application: a Silk.NET OpenGL 4.6 + Dear ImGui front-end. It owns the window/loop,
/// the main/save/settings menus, and the in-game HUD (playback, brush, lighting, hotbar, inventory),
/// all rendered through ImGui. The world is drawn by <see cref="WorldRenderer"/> and simulation runs on
/// a background thread.
/// </summary>
public static class Program
{
    private const int ScreenWidth = 1280;
    private const int ScreenHeight = 720;
    private const double InitialTicksPerSecond = 60.0;

    public static void Main()
    {
        using GlContext context = GlContext.CreateWindowed(ScreenWidth, ScreenHeight, "Technolize");
        using var imgui = new ImGuiHost(context);

        SaveGameStore saveGameStore = new();
        AppSettings settings = new()
        {
            UseColorOnlyRenderer = Environment.GetCommandLineArgs().Contains("--r2"),
        };
        AppScreen screen = AppScreen.MainMenu;
        GameSession? session = null;

        Stopwatch clock = Stopwatch.StartNew();
        double lastTime = 0.0;

        while (!context.Window.IsClosing)
        {
            context.Window.DoEvents();
            if (context.Window.IsClosing)
            {
                break;
            }

            double now = clock.Elapsed.TotalSeconds;
            float delta = (float)(now - lastTime);
            lastTime = now;

            // The in-game screen renders the world to the default framebuffer BEFORE the ImGui frame so
            // the HUD overlays it. Menus just clear to a dark background.
            if (screen == AppScreen.InGame && session is not null)
            {
                session.RenderWorld();
            }
            else
            {
                context.Gl.Viewport(0, 0, (uint)Math.Max(1, context.Window.Size.X), (uint)Math.Max(1, context.Window.Size.Y));
                context.Gl.ClearColor(0.05f, 0.07f, 0.09f, 1f);
                context.Gl.Clear((uint)Silk.NET.OpenGL.ClearBufferMask.ColorBufferBit);
            }

            imgui.BeginFrame(delta);
            screen = screen switch
            {
                AppScreen.MainMenu => DrawMainMenu(context, saveGameStore, settings, ref session),
                AppScreen.SaveMenu => DrawSaveMenu(context, saveGameStore, settings, ref session),
                AppScreen.Settings => DrawSettingsMenu(context, settings, session),
                AppScreen.InGame => UpdateInGame(context, saveGameStore, ref session),
                _ => screen,
            };
            imgui.EndFrame();

            context.Window.GLContext?.SwapBuffers();
        }

        session?.Dispose();
    }

    private static void CenterNextWindow(GlContext context, Vector2 size)
    {
        Vector2 viewport = new(context.Window.Size.X, context.Window.Size.Y);
        ImGui.SetNextWindowPos((viewport - size) * 0.5f, ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
    }

    private static AppScreen DrawMainMenu(GlContext context, SaveGameStore saveGameStore, AppSettings settings, ref GameSession? session)
    {
        AppScreen next = AppScreen.MainMenu;
        CenterNextWindow(context, new Vector2(380, 320));
        ImGui.Begin("Technolize", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);
        ImGui.TextWrapped("Reactive ant-world prototype");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Vector2 button = new(ImGui.GetContentRegionAvail().X, 56);
        if (ImGui.Button("Play", button))
        {
            if (saveGameStore.HasCurrentSave())
            {
                next = AppScreen.SaveMenu;
            }
            else
            {
                SaveGameMetadata save = saveGameStore.CreateNewSave();
                ReplaceSession(context, ref session, save, settings);
                next = AppScreen.InGame;
            }
        }

        ImGui.BeginDisabled();
        ImGui.Button("Unlocks", button);
        ImGui.EndDisabled();

        if (ImGui.Button("Settings", button))
        {
            next = AppScreen.Settings;
        }

        ImGui.End();
        return next;
    }

    private static AppScreen DrawSaveMenu(GlContext context, SaveGameStore saveGameStore, AppSettings settings, ref GameSession? session)
    {
        AppScreen next = AppScreen.SaveMenu;
        bool hasSave = saveGameStore.TryLoadCurrentSave(out SaveGameMetadata? save);

        CenterNextWindow(context, new Vector2(400, 320));
        ImGui.Begin("Current Save", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);
        ImGui.TextWrapped(hasSave && save is not null ? $"Seed {save.WorldSeed}" : "No save slot yet");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        Vector2 button = new(ImGui.GetContentRegionAvail().X, 48);

        ImGui.BeginDisabled(!hasSave);
        if (ImGui.Button("Continue", button))
        {
            if (session is null && save is not null)
            {
                ReplaceSession(context, ref session, save, settings);
            }

            session?.SetPlaybackMode(PlaybackMode.Play);
            next = AppScreen.InGame;
        }
        ImGui.EndDisabled();

        if (ImGui.Button("New Save", button))
        {
            SaveGameMetadata newSave = saveGameStore.CreateNewSave();
            ReplaceSession(context, ref session, newSave, settings);
            next = AppScreen.InGame;
        }

        ImGui.BeginDisabled(!hasSave);
        if (ImGui.Button("Delete Saves", button))
        {
            saveGameStore.DeleteCurrentSave();
            DisposeSession(ref session);
            next = AppScreen.MainMenu;
        }
        ImGui.EndDisabled();

        if (ImGui.Button("Back", button))
        {
            next = AppScreen.MainMenu;
        }

        ImGui.End();
        return next;
    }

    private static AppScreen DrawSettingsMenu(GlContext context, AppSettings settings, GameSession? session)
    {
        AppScreen next = AppScreen.Settings;

        CenterNextWindow(context, new Vector2(440, 240));
        ImGui.Begin("Settings", ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse);
        ImGui.TextWrapped("Rendering and debug options");
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        bool overlay = settings.ShowScheduledRegionOverlay;
        if (ImGui.Checkbox("Tick Overlay", ref overlay))
        {
            settings.ShowScheduledRegionOverlay = overlay;
            session?.ApplySettings(settings);
        }
        ImGui.TextDisabled("Highlight regions scheduled for the next tick");

        ImGui.Spacing();
        if (ImGui.Button("Back", new Vector2(ImGui.GetContentRegionAvail().X, 40)))
        {
            next = AppScreen.MainMenu;
        }

        ImGui.End();
        return next;
    }

    private static AppScreen UpdateInGame(GlContext context, SaveGameStore saveGameStore, ref GameSession? session)
    {
        if (session is null)
        {
            return saveGameStore.HasCurrentSave() ? AppScreen.SaveMenu : AppScreen.MainMenu;
        }

        bool escapePressed = ImGui.IsKeyPressed(ImGuiKey.Escape, false);
        if (escapePressed)
        {
            if (session.CloseInventory())
            {
                session.RenderUi();
                return AppScreen.InGame;
            }

            session.SetPlaybackMode(PlaybackMode.Pause);
            return AppScreen.SaveMenu;
        }

        session.UpdateInput();
        session.RenderUi();
        return AppScreen.InGame;
    }

    private static void ReplaceSession(GlContext context, ref GameSession? session, SaveGameMetadata save, AppSettings settings)
    {
        DisposeSession(ref session);
        session = new GameSession(context, save.WorldSeed, settings);
        session.SetPlaybackMode(PlaybackMode.Play);
    }

    private static void DisposeSession(ref GameSession? session)
    {
        session?.Dispose();
        session = null;
    }

    private static double GetPlaybackTicksPerSecond(PlaybackMode mode) => mode switch
    {
        PlaybackMode.Pause => 0.0,
        PlaybackMode.Play => InitialTicksPerSecond,
        PlaybackMode.Fast => InitialTicksPerSecond * 4.0,
        PlaybackMode.Fastest => InitialTicksPerSecond * 64.0,
        _ => InitialTicksPerSecond,
    };

    private enum AppScreen
    {
        MainMenu,
        SaveMenu,
        Settings,
        InGame,
    }

    private sealed class AppSettings
    {
        public bool ShowScheduledRegionOverlay { get; set; }

        /// <summary>When set (via the <c>--r2</c> flag), the world is drawn by the clean-slate
        /// colour-only <see cref="WorldColorRenderer"/> instead of the full lighting renderer.</summary>
        public bool UseColorOnlyRenderer { get; set; }
    }

    private enum PlaybackMode
    {
        Pause,
        Play,
        Fast,
        Fastest,
    }

    /// <summary>
    /// Owns one in-game world: the simulation thread, the world renderer, the interaction layer, and
    /// the in-game ImGui HUD.
    /// </summary>
    private sealed class GameSession : IDisposable
    {
        private readonly TickableWorld _world;
        private readonly PublishedWorldRenderSource _renderSource;
        private readonly WorldCommandQueue _worldCommands;
        private readonly SimulationClockState _simulationClock;
        private readonly CancellationTokenSource _shutdown;
        private readonly Thread _simulationThread;
        private readonly WorldRenderer _renderer;
        private readonly DevInteractions _interactions;
        private readonly GameInput _input;

        private bool _inventoryOpen;
        private string _inventorySearch = string.Empty;
        private PlaybackMode _playbackMode = PlaybackMode.Play;

        public GameSession(GlContext context, int worldSeed, AppSettings settings)
        {
            _world = new TickableWorld { Generator = new SimpleNoiseGenerator(worldSeed) };
            SignatureWorldTicker ticker = new(_world);
            _renderSource = new PublishedWorldRenderSource();
            _worldCommands = new WorldCommandQueue();
            _simulationClock = new SimulationClockState(InitialTicksPerSecond);
            _shutdown = new CancellationTokenSource();

            _world.GetBlock(new Vector2(0, 0));
            _world.ProcessUpdate(new Vector2(0, 0));
            _renderSource.Publish(WorldRenderFrameBuilder.FromWorld(_world));

            _renderer = new WorldRenderer(context, _renderSource, settings.UseColorOnlyRenderer);
            ApplySettings(settings);
            _input = new GameInput(context.Input!);
            _interactions = new DevInteractions(_worldCommands, _renderer, _input);

            _simulationThread = new Thread(() => RunSimulationLoop(_world, ticker, _renderSource, _worldCommands, _simulationClock, _shutdown.Token))
            {
                Name = "SimulationThread",
            };
            _simulationThread.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            _renderer.ShowScheduledRegionOverlay = settings.ShowScheduledRegionOverlay;
        }

        public void UpdateInput()
        {
            bool uiHovered = ImGui.GetIO().WantCaptureMouse;
            if (!uiHovered)
            {
                _renderer.UpdateCamera();
            }

            _interactions.Tick(uiHovered || _inventoryOpen);
        }

        public void RenderWorld()
        {
            long renderStart = Stopwatch.GetTimestamp();
            _renderer.Draw();
            _simulationClock.RecordRenderFrame(Stopwatch.GetElapsedTime(renderStart).TotalMilliseconds);
        }

        public void RenderUi()
        {
            DrawPlaybackControls();
            DrawBrushControls();
            DrawLightingControls();
            DrawHotbar();
            if (_inventoryOpen)
            {
                DrawInventory();
            }
            else if (ImGui.IsKeyPressed(ImGuiKey.E, false))
            {
                _inventoryOpen = true;
                _inventorySearch = string.Empty;
            }
        }

        private void DrawPlaybackControls()
        {
            ImGui.SetNextWindowPos(new Vector2(20, 20), ImGuiCond.FirstUseEver);
            ImGui.Begin("Playback", ImGuiWindowFlags.AlwaysAutoResize);
            DrawPlaybackButton("Pause", PlaybackMode.Pause);
            ImGui.SameLine();
            DrawPlaybackButton("Play", PlaybackMode.Play);
            ImGui.SameLine();
            DrawPlaybackButton("Fast", PlaybackMode.Fast);
            ImGui.SameLine();
            DrawPlaybackButton("Fastest", PlaybackMode.Fastest);

            ImGui.BeginDisabled(_playbackMode != PlaybackMode.Pause);
            ImGui.SameLine();
            if (ImGui.Button("Step"))
            {
                AdvanceSingleTick();
            }
            ImGui.EndDisabled();
            ImGui.End();
        }

        private void DrawPlaybackButton(string label, PlaybackMode mode)
        {
            bool selected = _playbackMode == mode;
            if (selected)
            {
                ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.79f, 0.55f, 0.29f, 1f));
            }

            if (ImGui.Button(label))
            {
                SetPlaybackMode(mode);
            }

            if (selected)
            {
                ImGui.PopStyleColor();
            }
        }

        private void DrawBrushControls()
        {
            ImGui.SetNextWindowPos(new Vector2(20, 110), ImGuiCond.FirstUseEver);
            ImGui.Begin("Brush", ImGuiWindowFlags.AlwaysAutoResize);
            ImGui.Text($"Size {_interactions.BrushSize}");
            ImGui.SameLine();
            if (ImGui.Button("-"))
            {
                _interactions.DecreaseBrushSize();
            }
            ImGui.SameLine();
            if (ImGui.Button("+"))
            {
                _interactions.IncreaseBrushSize();
            }

            foreach (BrushShape brush in _interactions.GetBrushShapes())
            {
                bool selected = _interactions.SelectedBrush == brush;
                if (selected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.79f, 0.55f, 0.29f, 1f));
                }

                if (ImGui.Button(brush.ToString()))
                {
                    _interactions.SelectBrush(brush);
                }

                if (selected)
                {
                    ImGui.PopStyleColor();
                }

                ImGui.SameLine();
            }

            ImGui.NewLine();
            ImGui.End();
        }

        private void DrawLightingControls()
        {
            ImGui.SetNextWindowPos(new Vector2(20, 200), ImGuiCond.FirstUseEver);
            ImGui.Begin("Lighting", ImGuiWindowFlags.AlwaysAutoResize);
            Vector2 sun = _interactions.GetSunDirection();
            float sunAngle = MathF.Atan2(sun.Y, sun.X) * (180f / MathF.PI);
            ImGui.Text($"Sun {sunAngle:F1} deg");
            ImGui.Text($"Rays {_interactions.GetSunRayCount()}");
            ImGui.TextDisabled("Hold Left/Right rotate, Home reset, PgUp/PgDn rays");
            ImGui.End();
        }

        private void DrawHotbar()
        {
            ImGui.SetNextWindowPos(new Vector2(20, ImGui.GetIO().DisplaySize.Y - 90), ImGuiCond.FirstUseEver);
            ImGui.Begin("Hotbar", ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoTitleBar);
            for (int slot = 0; slot < _interactions.Hotbar.Count; slot++)
            {
                BlockInfo block = BlockRegistry.GetInfo(_interactions.Hotbar[slot]);
                TColor color = block.GetTag(BlockInfo.TagColor);
                bool selected = slot == _interactions.SelectedHotbarIndex;

                if (selected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Border, new Vector4(1f, 0.9f, 0.75f, 1f));
                    ImGui.PushStyleVar(ImGuiStyleVar.FrameBorderSize, 2f);
                }

                if (ImGui.ColorButton($"##slot{slot}", color.ToVector4(), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, new Vector2(40, 40)))
                {
                    _interactions.SelectHotbarSlot(slot);
                }

                if (selected)
                {
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor();
                }

                if (slot < _interactions.Hotbar.Count - 1)
                {
                    ImGui.SameLine();
                }
            }

            ImGui.End();
        }

        private void DrawInventory()
        {
            Vector2 display = ImGui.GetIO().DisplaySize;
            Vector2 size = new(MathF.Min(860, display.X - 80), MathF.Min(540, display.Y - 120));
            ImGui.SetNextWindowPos((display - size) * 0.5f, ImGuiCond.Appearing);
            ImGui.SetNextWindowSize(size, ImGuiCond.Appearing);

            bool open = _inventoryOpen;
            ImGui.Begin("Inventory", ref open, ImGuiWindowFlags.NoCollapse);
            if (!open)
            {
                CloseInventory();
                ImGui.End();
                return;
            }

            ImGui.TextDisabled("Click a block to save it to the selected hotbar slot");
            ImGui.InputTextWithHint("##search", "Search blocks...", ref _inventorySearch, 64);
            ImGui.Separator();

            IReadOnlyList<BlockInfo> blocks = _interactions.GetBlocks(_inventorySearch);
            if (blocks.Count == 0)
            {
                ImGui.TextDisabled("No blocks match that search.");
            }

            ImGui.BeginChild("blocks");
            float available = ImGui.GetContentRegionAvail().X;
            int columns = Math.Max(1, (int)(available / 132f));
            int column = 0;
            foreach (BlockInfo block in blocks)
            {
                TColor color = block.GetTag(BlockInfo.TagColor);
                string name = block.GetTag(BlockInfo.TagDisplayName) ?? $"Block {block.Id}";

                ImGui.BeginGroup();
                if (ImGui.ColorButton($"##inv{block.Id}", color.ToVector4(), ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoAlpha, new Vector2(30, 30)))
                {
                    _interactions.SetHotbarBlock(_interactions.SelectedHotbarIndex, block);
                }
                ImGui.SameLine();
                ImGui.BeginGroup();
                ImGui.TextUnformatted(name);
                ImGui.TextDisabled($"ID {block.Id}");
                ImGui.EndGroup();
                ImGui.EndGroup();

                column++;
                if (column < columns)
                {
                    ImGui.SameLine(0, 24);
                }
                else
                {
                    column = 0;
                }
            }
            ImGui.EndChild();
            ImGui.End();
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
            _input.Dispose();
            _shutdown.Dispose();
            _world.Unload();
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
}
