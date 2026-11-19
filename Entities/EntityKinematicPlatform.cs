using System;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace Vintagestory.GameContent;

public class EntityKinematicPlatform : Entity
{
    private const string motionStateAttribute = "kinematicPlatformMotion";

    private readonly Vec3d origin = new();
    private ITreeAttribute motionState = null!;
    private Vec3d movementOffset = null!;
    private double movementSpeed;
    private bool movingToEnd;

    public override bool ApplyGravity => false;

    public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3d)
    {
        base.Initialize(properties, api, InChunkIndex3d);

        JsonObject attributes = properties.Attributes ?? throw new InvalidOperationException($"Entity {Code} requires movement attributes.");
        Vec3d? configuredOffset = attributes["movementOffset"].AsObject<Vec3d>();
        movementSpeed = attributes["movementSpeed"].AsDouble();
        if (configuredOffset == null
            || (configuredOffset.X == 0 && configuredOffset.Y == 0 && configuredOffset.Z == 0)
            || !double.IsFinite(configuredOffset.X)
            || !double.IsFinite(configuredOffset.Y)
            || !double.IsFinite(configuredOffset.Z))
        {
            throw new InvalidOperationException($"Entity {Code} requires a finite, nonzero movementOffset.");
        }
        movementOffset = configuredOffset;
        if (movementSpeed <= 0 || !double.IsFinite(movementSpeed))
        {
            throw new InvalidOperationException($"Entity {Code} requires a positive movementSpeed.");
        }

        if (api.Side != EnumAppSide.Server) return;

        motionState = Attributes.GetTreeAttribute(motionStateAttribute);
        if (motionState == null)
        {
            origin.Set(Pos.X, Pos.Y, Pos.Z);
            motionState = new TreeAttribute();
            motionState.SetDouble("originX", origin.X);
            motionState.SetDouble("originY", origin.Y);
            motionState.SetDouble("originZ", origin.Z);
            Attributes.SetAttribute(motionStateAttribute, motionState);
        }
        else
        {
            origin.Set(
                motionState.GetDouble("originX"),
                motionState.GetDouble("originY"),
                motionState.GetDouble("originZ")
            );
        }

        movingToEnd = motionState.GetBool("movingToEnd", true);
    }

    public override void OnGameTick(float dt)
    {
        if (World.Side == EnumAppSide.Server) updatePosition(dt);
        base.OnGameTick(dt);
    }

    public override bool CanCollect(Entity byEntity) => false;

    private void updatePosition(float dt)
    {
        double offsetFactor = movingToEnd ? 1 : 0;
        double targetX = origin.X + offsetFactor * movementOffset.X;
        double targetY = origin.Y + offsetFactor * movementOffset.Y;
        double targetZ = origin.Z + offsetFactor * movementOffset.Z;
        double dx = targetX - Pos.X;
        double dy = targetY - Pos.Y;
        double dz = targetZ - Pos.Z;
        double distanceSq = dx * dx + dy * dy + dz * dz;
        double movement = movementSpeed * dt;

        if (distanceSq <= movement * movement)
        {
            Pos.SetPos(targetX, targetY, targetZ);
            movingToEnd = !movingToEnd;
            motionState.SetBool("movingToEnd", movingToEnd);
            return;
        }

        double factor = movement / Math.Sqrt(distanceSq);
        Pos.X += dx * factor;
        Pos.Y += dy * factor;
        Pos.Z += dz * factor;
    }
}
