﻿using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Shared;

namespace GameClient;

public class GameClient : Game
{
    private const int VirtualScreenWidth = 320;
    private const int VirtualScreenHeight = 180;
    private const int WindowDefaultSizeMultiplier = 2;

    private readonly GraphicsDeviceManager graphics;
    private readonly Input input;
    
    private SpriteBatch spriteBatch;
    private TextureAtlas textureAtlas;
    private Background background;
    
    private IGameState gameState;
    private GameState gameStateId;
    private bool gameStateInitialized;

    public GameClient()
    {
        graphics = new GraphicsDeviceManager(this);
        input = new Input();
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        InactiveSleepTime = TimeSpan.Zero; // Don't throttle FPS when the window is inactive.
        IsFixedTimeStep = false;
        graphics.PreferredBackBufferWidth = VirtualScreenWidth * WindowDefaultSizeMultiplier;
        graphics.PreferredBackBufferHeight = VirtualScreenHeight * WindowDefaultSizeMultiplier;
        graphics.ApplyChanges();
    }
    
    protected override void Initialize()
    {
        textureAtlas = new TextureAtlas(GraphicsDevice, "Content/atlas.png", 16);
        background = new Background(GraphicsDevice, "Content/background.png");
        Sprite.LoadAnimation(Animation.PlayerIdle, "Content/Animations/playerIdle.json");
        Sprite.LoadAnimation(Animation.PlayerRunning, "Content/Animations/playerRunning.json");
        Sprite.LoadAnimation(Animation.PlayerJumping, "Content/Animations/playerJumping.json");
        Audio.LoadSound(Sound.Jump, "Content/Sounds/jump.wav");
        Audio.LoadSound(Sound.Bounce, "Content/Sounds/bounce.wav");
        Audio.LoadSound(Sound.Special, "Content/Sounds/special.wav");

        SwitchGameState(GameState.MainMenu);
        
        base.Initialize();
    }

    protected override void LoadContent()
    {
        spriteBatch = new SpriteBatch(GraphicsDevice);
    }

    protected override void Update(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        
        input.Update();
        // Don't update if delta time is too large, to preserve physics/other systems.
        if (deltaTime < 0.1f && gameStateInitialized)
            gameState?.Update(input, deltaTime);

        base.Update(gameTime);
    }

    protected override void Draw(GameTime gameTime)
    {
        var deltaTime = (float)gameTime.ElapsedGameTime.TotalSeconds;
        GraphicsDevice.Clear(background.Color);

        if (gameStateInitialized)
            gameState?.Draw(background, textureAtlas, spriteBatch, Window.ClientBounds.Width, Window.ClientBounds.Height, deltaTime);

        base.Draw(gameTime);
    }

    protected override void Dispose(bool disposing)
    {
        if (gameStateInitialized)
            gameState?.Dispose();

        base.Dispose(disposing);
    }

    public void SwitchGameState(GameState newState, params string[] args)
    {
        gameStateId = newState;

        gameStateInitialized = false;
        gameState?.Dispose();

        gameState = gameStateId switch
        {
            GameState.MainMenu => new MainMenuState(),
            GameState.Connecting => new ConnectingState(),
            GameState.InGame => new InGameState(),
            _ => throw new ArgumentOutOfRangeException(nameof(newState))
        };
        
        gameState.Initialize(this, VirtualScreenWidth, VirtualScreenHeight, args);
        gameStateInitialized = true;
    }
}