﻿using Messaging;
using Microsoft.Xna.Framework;
using Shared;

namespace GameServer;

public class GameServer
{
    private const int TickRate = 20;
    private const float TickTime = 1f / TickRate;
    private const float HeartbeatTime = 1f;
    private const float ScoreDecayTime = 1f;
    private readonly MapData mapData;
    private readonly int effectJump;
    private readonly int effectStart;
    private readonly int effectEnd;
    private readonly int effectFullTrap;
    private readonly int effectFloorTrap;

    private readonly Dictionary<int, Player> players = new();
    private float heartbeatTimer;
    private float scoreDecayTimer;

    public GameServer()
    {
        mapData = MapData.LoadFromFile("Content/map.json");
        mapData.FindSpawnPoint();
        effectJump = mapData.Effect["Jump"];
        effectStart = mapData.Effect["Start"];
        effectEnd = mapData.Effect["End"];
        effectFullTrap = mapData.Effect["FullTrap"];
        effectFloorTrap = mapData.Effect["FloorTrap"];

        Dictionary<Message.MessageType, MessageStream.MessageHandler> messageHandlers = new()
        {
            { Message.MessageType.MovePlayer, HandleMovePlayer },
            { Message.MessageType.Disconnect, HandleDisconnect },
            { Message.MessageType.UpdateName, HandleUpdateName },
            { Message.MessageType.Heartbeat, (_, _) => { } }
        };

        Server.StartServer(messageHandlers, 20, OnTick, OnDisconnect, OnConnect);
    }

    private void OnTick()
    {
        heartbeatTimer += TickTime;

        if (heartbeatTimer > HeartbeatTime)
        {
            heartbeatTimer -= HeartbeatTime;
            Server.SendMessageToAll(Message.MessageType.Heartbeat, new HeartbeatData());
        }

        scoreDecayTimer += TickTime;
        bool decayScore = scoreDecayTimer > ScoreDecayTime;
        
        if (decayScore)
        {
            scoreDecayTimer -= ScoreDecayTime;
        }

        KeyValuePair<int, Player>[] allPlayerPairs = players.ToArray();
        foreach (KeyValuePair<int, Player> pair in allPlayerPairs)
        {
            Server.SendMessageToAllExcluding(pair.Key, Message.MessageType.MovePlayer, new MovePlayerData
            {
                Id = pair.Key,
                X = pair.Value.Position.X,
                Y = pair.Value.Position.Y,
                Direction = (byte)pair.Value.Direction,
                Animation = (byte)pair.Value.Animation,
                Grounded = pair.Value.Grounded
            });

            // If this player fell out of the map, reset it's position.
            if (pair.Value.Position.Y > mapData.TileSize * mapData.Height) KillPlayer(pair);

            TileData tileAtPlayer = mapData.GetTileDataAtWorldPos(pair.Value.Position);
            bool playerNearFloor = mapData.IsCollidingWith(pair.Value.Position + new Vector2(0f, mapData.FloorTileSize),
                Player.HitBoxSize);
            
            if (tileAtPlayer.Effect == effectFullTrap || (playerNearFloor && tileAtPlayer.Effect == effectFloorTrap))
            {
                KillPlayer(pair);
            }
            
            var updatedScore = false;
            var updatedHighScore = false;
            if (pair.Value.Score != 0)
            {
                if (decayScore)
                {
                    pair.Value.Score -= 10;
                    updatedScore = true;
                }
                
                // If the player has points and reached the goal, try to cash in those points.
                if (tileAtPlayer.Effect == effectEnd)
                {
                    if (pair.Value.Score > pair.Value.HighScore)
                    {
                        pair.Value.HighScore = pair.Value.Score;
                        updatedHighScore = true;
                    }

                    pair.Value.Score = 0;
                    updatedScore = true;
                }
            }
            else
            {
                // If this player is near the start, give them points.
                if (tileAtPlayer.Effect == effectStart)
                {
                    pair.Value.Score = Player.StartingScore;
                    updatedScore = true;
                }
            }

            if (updatedScore)
            {
                Server.SendMessageToAll(Message.MessageType.UpdateScore, new UpdateScoreData
                {
                    Id = pair.Key,
                    Score = pair.Value.Score
                });
            }
            
            if (updatedHighScore)
            {
                Server.SendMessageToAll(Message.MessageType.UpdateHighScore, new UpdateHighScoreData
                {
                    Id = pair.Key,
                    HighScore = pair.Value.HighScore
                });
            }
        }
    }

    private void OnConnect(int id)
    {
        Console.WriteLine($"Player connected: {id}");

        var newPlayer = new Player(mapData.SpawnPos, Player.DefaultName, 0);
        players.Add(id, newPlayer);

        // Tell old players about the new player.
        Server.SendMessageToAllExcluding(id, Message.MessageType.SpawnPlayer, new SpawnPlayerData
        {
            Id = id,
            X = newPlayer.Position.X,
            Y = newPlayer.Position.Y,
            Name = newPlayer.Name,
            HighScore = newPlayer.HighScore
        });

        // Tell new players about all players (old players and themself).
        KeyValuePair<int, Player>[] allPlayerPairs = players.ToArray();
        foreach (KeyValuePair<int, Player> pair in allPlayerPairs)
            Server.SendMessage(id, Message.MessageType.SpawnPlayer, new SpawnPlayerData
            {
                Id = pair.Key,
                X = pair.Value.Position.X,
                Y = pair.Value.Position.Y,
                Name = pair.Value.Name,
                HighScore = pair.Value.HighScore
            });
    }

    private void OnDisconnect(int id)
    {
        Console.WriteLine($"Player disconnected: {id}");
        players.Remove(id);
        Server.SendMessageToAll(Message.MessageType.DestroyPlayer, new DestroyPlayerData
        {
            Id = id
        });
    }

    private void KillPlayer(KeyValuePair<int, Player> pair)
    {
        Server.SendMessageToAll(Message.MessageType.MovePlayer, new MovePlayerData
        {
            Id = pair.Key,
            X = mapData.SpawnPos.X,
            Y = mapData.SpawnPos.Y,
            Direction = (byte)Direction.Right,
            Animation = (byte)Animation.PlayerIdle,
            Grounded = false
        });

        pair.Value.Score = 0;
        
        Server.SendMessageToAll(Message.MessageType.UpdateScore, new UpdateScoreData
        {
            Id = pair.Key,
            Score = 0
        });
    }

    private void HandleMovePlayer(int fromId, IData data)
    {
        if (data is not MovePlayerData moveData) return;

        Player player = players[fromId];
        player.Position.X = moveData.X;
        player.Position.Y = moveData.Y;
        player.Direction = (Direction)moveData.Direction;
        player.Animation = (Animation)moveData.Animation;
        player.Grounded = moveData.Grounded;
    }
    
    private void HandleDisconnect(int fromId, IData data)
    {
        Server.Disconnect(fromId);
    }
    
    private void HandleUpdateName(int fromId, IData data)
    {
        if (data is not UpdateNameData nameData) return;
        if (!players.TryGetValue(fromId, out Player player)) return;

        string name = nameData.Name;

        if (name.Length > Player.MaxNameLength)
        {
            name = name.Substring(0, Player.MaxNameLength);
        }

        player.Name = name;
        
        Server.SendMessageToAll(Message.MessageType.UpdateName, new UpdateNameData
        {
            Name = name,
            Id = fromId
        });
    }
}