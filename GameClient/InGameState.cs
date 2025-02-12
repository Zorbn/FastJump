﻿using System.Collections.Generic;
using System.Linq;
using Messaging;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Shared;

namespace GameClient;

public class InGameState : IGameState
{
    private const float SpriteInterpSpeed = 20f;
    private const float CameraInterpSpeed = 20f;
    private const float TickTime = 1f / 30f;
    
    private readonly Dictionary<int, PlayerData> players = new();
    private Map map;
    private Camera camera;
    private Camera uiCamera;
    private int localId = -1;
    private GameClient gameClient;
    private float tickTimer;

    public void Initialize(GameClient newGameClient, int screenWidth, int screenHeight, params string[] args)
    {
        gameClient = newGameClient;

        map = new Map("Content/map.json");
        camera = new Camera(screenWidth, screenHeight);
        uiCamera = new Camera(screenWidth, screenHeight);

        Client.RegisterHandler(Message.MessageType.SpawnPlayer, HandleSpawnPlayer);
        Client.RegisterHandler(Message.MessageType.DestroyPlayer, HandleDestroyPlayer);
        Client.RegisterHandler(Message.MessageType.MovePlayer, HandleMovePlayer);
        Client.RegisterHandler(Message.MessageType.Heartbeat, HandleHeartbeat);
        Client.RegisterHandler(Message.MessageType.UpdateScore, HandleUpdateScore);
        Client.RegisterHandler(Message.MessageType.UpdateHighScore, HandleUpdateHighScore);
        Client.RegisterHandler(Message.MessageType.UpdateName, HandleUpdateName);

        localId = int.Parse(args[0]);
        Client.SendMessage(Message.MessageType.UpdateName, new UpdateNameData
        {
            Id = localId,
            Name = args[1]
        });
    }

    public void Update(Input input, float deltaTime)
    {
        if (GamePad.GetState(PlayerIndex.One).Buttons.Back == ButtonState.Pressed ||
            input.IsKeyDown(Keys.Escape))
            gameClient.SwitchGameState(GameState.MainMenu);

        tickTimer += deltaTime;

        while (tickTimer > TickTime)
        {
            tickTimer -= TickTime;
            Tick();
        }

        if (input.IsKeyDown(Keys.LeftControl) && input.WasKeyPressed(Keys.R))
        {
            map = new Map("Content/map.json");
        }

        LocalUpdate(input, deltaTime);
    }

    private void Tick()
    {
        if (!TryGetLocalPlayer(out PlayerData playerData)) return;
        
        Player player = playerData.Player;
        
        Client.SendMessage(Message.MessageType.MovePlayer, new MovePlayerData
        {
            Id = localId,
            X = player.Position.X,
            Y = player.Position.Y,
            Direction = (byte)player.Direction,
            Animation = (byte)player.Animation,
            Grounded = player.Grounded
        });
    }
    
    private bool TryGetLocalPlayer(out PlayerData playerData)
    {
        playerData = null;
        
        return localId != -1 && players.TryGetValue(localId, out playerData);
    }

    private void LocalUpdate(Input input, float deltaTime)
    {
        if (!TryGetLocalPlayer(out PlayerData playerData)) return;

        Player player = playerData.Player;

        var dir = 0f;
        if (input.IsKeyDown(Keys.Left) || input.IsKeyDown(Keys.A)) dir -= 1f;
        if (input.IsKeyDown(Keys.Right) || input.IsKeyDown(Keys.D)) dir += 1f;
        bool tryJump = input.IsKeyDown(Keys.Space) || input.IsKeyDown(Keys.Up) || input.IsKeyDown(Keys.W);
        player.Move(dir, tryJump, map.MapData, deltaTime);
        camera.StepTowards(player.Position + new Vector2(map.MapData.TileSize * 0.5f), CameraInterpSpeed * deltaTime);
    }

    public void Draw(Background background, TextureAtlas atlas, SpriteBatch batch, int windowWidth, int windowHeight, float deltaTime)
    {
        camera.ScaleToScreen(windowWidth, windowHeight);
        uiCamera.ScaleToScreen(windowWidth, windowHeight);
        
        batch.Begin(samplerState: SamplerState.PointClamp);

        background.Draw(batch, camera);
        map.Draw(atlas, batch, camera);

        KeyValuePair<int, PlayerData>[] drawablePlayers = players.ToArray();
        foreach (KeyValuePair<int, PlayerData> pair in drawablePlayers)
        {
            Player player = pair.Value.Player;
            Sprite sprite = pair.Value.Sprite;
            bool playerIsLocal = pair.Key == localId;
            
            if (playerIsLocal)
                sprite.Teleport(player.Position);
            else
                sprite.StepTowards(player.Position, deltaTime * SpriteInterpSpeed);

            bool flipped = player.Direction == Direction.Left;
            Frame frame = sprite.UpdateAnimation(player.Animation, deltaTime);
            atlas.Draw(batch, camera, sprite.Position, frame.X, frame.Y, 2, 2, Color.White, Vector2.One,
                0f, flipped);

            if (playerIsLocal) continue;
            
            var nameX = (int)(sprite.Position.X + atlas.HalfTileSize);
            var nameY = (int)(sprite.Position.Y + map.MapData.TileSize);
            TextRenderer.Draw(player.Name, nameX, nameY, atlas, batch, camera, true, Player.NameScale, true);
            
            if (player.HighScore == 0) continue;
            
            int scoreY = nameY + atlas.TileSize;
            TextRenderer.Draw($":{player.HighScore}", nameX, scoreY, atlas, batch, camera, true, Player.NameScale, true);
        }

        if (TryGetLocalPlayer(out PlayerData playerData))
        {
            Player player = playerData.Player;
            string scoreText = player.Score == 0 ? $"HIGH SCORE:{player.HighScore}" : $"SCORE:{player.Score}";
            TextRenderer.Draw(scoreText, atlas.HalfTileSize, atlas.HalfTileSize, atlas, batch, uiCamera, true, 0.5f);
        }

        batch.End();
    }

    public void Dispose()
    {
        Client.SendMessage(Message.MessageType.Disconnect, new DisconnectData());
        Client.StopClient();
    }
    
    private void HandleSpawnPlayer(int fromId, IData data)
    {
        if (data is not SpawnPlayerData spawnData) return;

        var playerPos = new Vector2(spawnData.X, spawnData.Y);

        players.Add(spawnData.Id, new PlayerData
        {
            Player = new Player(playerPos, spawnData.Name, spawnData.HighScore),
            Sprite = new Sprite(playerPos)
        });
    }

    private void HandleDestroyPlayer(int fromId, IData data)
    {
        if (data is not DestroyPlayerData destroyData) return;

        players.Remove(destroyData.Id);
    }

    private void HandleMovePlayer(int fromId, IData data)
    {
        if (data is not MovePlayerData moveData) return;

        Player player = players[moveData.Id].Player;
        player.Position.X = moveData.X;
        player.Position.Y = moveData.Y;
        player.Direction = (Direction)moveData.Direction;
        player.Animation = (Animation)moveData.Animation;
        player.Grounded = moveData.Grounded;
    }

    private void HandleHeartbeat(int fromId, IData data)
    {
        Client.SendMessage(Message.MessageType.Heartbeat, new HeartbeatData());
    }

    private void HandleUpdateScore(int fromId, IData data)
    {
        if (data is not UpdateScoreData scoreData) return;

        Player player = players[scoreData.Id].Player;
        
        // Play a sound if the local player is starting a run.
        if (localId == scoreData.Id && player.Score == 0 && scoreData.Score != 0)
        {
            Audio.PlaySoundWithPitch(Sound.Special);
        }
        
        player.Score = scoreData.Score;
    }
    
    private void HandleUpdateHighScore(int fromId, IData data)
    {
        if (data is not UpdateHighScoreData scoreData) return;

        Player player = players[scoreData.Id].Player;
        
        // Play a sound if the local player got a new high score.
        if (localId == scoreData.Id)
        {
            Audio.PlaySoundWithPitch(Sound.Special);
        }
        
        player.HighScore = scoreData.HighScore;
    }
    
    private void HandleUpdateName(int fromId, IData data)
    {
        if (data is not UpdateNameData nameData) return;

        Player player = players[nameData.Id].Player;
        player.Name = nameData.Name;
    }
}