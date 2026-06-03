using UnityEngine;

/// <summary>
/// Generates the mesh for a wall-mounted storage locker / cache.
/// Looks like an industrial wall safe with a door panel.
/// All geometry in local space: Z+ = into room, Y+ = up.
/// </summary>
public static class LootCacheMesh
{
    public static void Build(Transform parent, Material matBase, Material matDoor, Material matGlow, bool isOpen)
    {
        // Wall-mounted frame
        Spawn(parent, "Frame", MeshPrimitives.Box(new Vector3(0, 0.5f, -0.02f), 1.0f, 1.0f, 0.04f), matBase);

        // Side pillars
        Spawn(parent, "PillarL", MeshPrimitives.Box(new Vector3(-0.48f, 0.5f, 0.15f), 0.08f, 0.9f, 0.3f), matBase);
        Spawn(parent, "PillarR", MeshPrimitives.Box(new Vector3(0.48f, 0.5f, 0.15f), 0.08f, 0.9f, 0.3f), matBase);

        // Top and bottom rails
        Spawn(parent, "TopRail", MeshPrimitives.Box(new Vector3(0, 0.98f, 0.15f), 1.0f, 0.06f, 0.3f), matBase);
        Spawn(parent, "BotRail", MeshPrimitives.Box(new Vector3(0, 0.04f, 0.15f), 1.0f, 0.06f, 0.3f), matBase);

        // Interior cavity
        Spawn(parent, "Cavity", MeshPrimitives.Box(new Vector3(0, 0.5f, 0.1f), 0.8f, 0.8f, 0.2f), matBase);

        if (isOpen)
        {
            // Door swung open (rotated panel on the side)
            Spawn(parent, "DoorOpen", MeshPrimitives.Box(new Vector3(-0.52f, 0.5f, 0.35f), 0.04f, 0.75f, 0.7f), matDoor);

            // Glow from inside (visible contents indicator)
            Spawn(parent, "ContentGlow", MeshPrimitives.Box(new Vector3(0, 0.5f, 0.18f), 0.5f, 0.5f, 0.06f), matGlow);
        }
        else
        {
            // Closed door panel
            Spawn(parent, "Door", MeshPrimitives.Box(new Vector3(0, 0.5f, 0.32f), 0.82f, 0.78f, 0.04f), matDoor);

            // Lock indicator (small glow on door)
            Spawn(parent, "LockGlow", MeshPrimitives.Box(new Vector3(0.25f, 0.5f, 0.35f), 0.08f, 0.08f, 0.02f), matGlow);

            // Handle
            Spawn(parent, "Handle", MeshPrimitives.Box(new Vector3(-0.2f, 0.5f, 0.36f), 0.15f, 0.04f, 0.03f), matBase);
        }
    }

    static GameObject Spawn(Transform parent, string name, Mesh mesh, Material mat)
        => MeshPrimitives.Spawn(parent, name, mesh, mat, addCollider: true);
}
