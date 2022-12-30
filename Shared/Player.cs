﻿using Microsoft.Xna.Framework;

namespace Shared;

public class Player
{
    public const int StartingScore = 1000;
    private const float JumpForce = 1.5f;
    private const float ExtraHeightGravityMultiplier = 0.5f;
    private const float Speed = 100f;
    private static readonly Vector2 HitBoxSize = new(8f, 14f);
    public Animation Animation = Animation.PlayerIdle;
    public Direction Direction = Direction.Right;
    private bool extraHeight;
    private bool grounded;

    public Vector2 Position;
    public int Score;

    private float velocity;

    public Player(Vector2 position)
    {
        Position = position;
    }

    public void Move(float horizontalDir, bool tryJump, bool noClip, MapData mapData, float deltaTime)
    {
        Vector2 newPosition = Position;
        Vector2 move = Vector2.Zero;

        move.X = horizontalDir;

        Direction = move.X switch
        {
            > 0f => Direction.Right,
            < 0f => Direction.Left,
            _ => Direction
        };

        if (grounded)
            Animation = move.X != 0f ? Animation.PlayerRunning : Animation.PlayerIdle;
        else
            Animation = Animation.PlayerJumping;

        newPosition.X += move.X * Speed * deltaTime;

        if (!noClip && mapData.IsCollidingWith(newPosition, HitBoxSize))
            newPosition.X = GetMaxPosInTile(Position.X, HitBoxSize.X, move.X, mapData.TileSize);

        // Make holding down the jump button after jumping make the player jump higher.
        float gravityMultiplier = extraHeight && velocity < 0f ? ExtraHeightGravityMultiplier : 1.0f;
        velocity += gravityMultiplier * Physics.Gravity * deltaTime;

        if (tryJump)
        {
            if (grounded)
            {
                extraHeight = true;
                velocity = -JumpForce;
            }
        }
        else
        {
            extraHeight = false;
        }

        move.Y = velocity;

        newPosition.Y += move.Y * Speed * deltaTime;
        grounded = false;

        if (!noClip && mapData.IsCollidingWith(newPosition, HitBoxSize))
        {
            if (velocity > 0f) grounded = true;

            newPosition.Y = GetMaxPosInTile(Position.Y, HitBoxSize.Y, move.Y, mapData.TileSize);
            velocity = 0f;
        }

        Position = newPosition;
    }

    // Find a position that would press the player up against the next tile.
    private float GetMaxPosInTile(float pos, float size, float direction, int tileSize)
    {
        if (direction > 0f) return MathF.Ceiling(pos / tileSize) * tileSize - size * 0.51f;

        return MathF.Floor(pos / tileSize) * tileSize + size * 0.51f;
    }
}