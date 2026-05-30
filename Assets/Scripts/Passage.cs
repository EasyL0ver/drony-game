using UnityEngine;
using System.Collections;

/// <summary>
/// Wall entity representing a corridor/duct/vent passage on one side of a room.
/// Placed at the wall midpoint, facing into the room (+Z = inward).
/// Each connection spawns two Passage instances (one per room).
/// </summary>
public class Passage : WallView
{
    public override float ParkOffset => 0.5f;

    public PassageType Type { get; private set; }
    public Vector2Int Room { get; private set; }
    public Vector2Int Neighbor { get; private set; }
    public int Edge { get; private set; }

    public void Init(Vector2Int room, Vector2Int neighbor, int edge, PassageType type)
    {
        Room = room;
        Neighbor = neighbor;
        Edge = edge;
        Type = type;
    }

    public void UpdateType(PassageType newType)
    {
        Type = newType;
    }

    protected override IEnumerator RunInteraction(Transform drone, float duration, WallInteractionConfig config, int token, System.Action onComplete)
    {
        if (config != null && config.DestroysDrone)
        {
            yield return RunBombInteraction(drone, duration, token, onComplete);
            yield break;
        }

        // Default: beam interaction (e.g. non-bomb rubble clear if we ever add one)
        yield return base.RunInteraction(drone, duration, config, token, onComplete);
    }

    IEnumerator RunBombInteraction(Transform drone, float duration, int token, System.Action onComplete)
    {
        // Wall is at transform.position, forward points into the room.
        // Drone starts at park point (wall + forward * ParkOffset).
        Vector3 wallPos = transform.position;
        Vector3 intoRoom = transform.forward;
        Vector3 startPos = drone.position;
        float hoverY = startPos.y;

        // Phase timing
        float pullBackTime = duration * 0.3f;   // arc backward
        float flashTime = duration * 0.3f;      // flash red, hold
        float chargeTime = duration * 0.4f;     // slam into wall

        // Pull-back target: further into the room
        float pullBackDist = 1.2f;
        Vector3 pullBackTarget = startPos + intoRoom * pullBackDist;
        pullBackTarget.y = hoverY;

        // Impact target: at the wall surface
        Vector3 impactPos = wallPos;
        impactPos.y = hoverY;

        // Get drone glow material for flashing
        var droneModel = drone.GetComponentInChildren<LowPolyDrone>();
        Material glowMat = droneModel?.GlowMaterial;
        Color originalGlow = glowMat != null ? glowMat.GetColor("_EmissionColor") : Color.black;

        // ── Phase 1: Arc backward ──
        float elapsed = 0f;
        while (elapsed < pullBackTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / pullBackTime;
            // Ease out (decelerate)
            float ease = 1f - (1f - t) * (1f - t);
            // Arc upward at midpoint
            Vector3 pos = Vector3.Lerp(startPos, pullBackTarget, ease);
            pos.y = hoverY + Mathf.Sin(t * Mathf.PI) * 0.4f;
            drone.position = pos;
            // Rotate to face away from wall (looking into room)
            drone.rotation = Quaternion.LookRotation(intoRoom);
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = pullBackTarget;

        // ── Phase 2: Flash red, vibrate ──
        Color bombRed = new Color(1f, 0.1f, 0f);
        float flashIntensity = 12f;
        elapsed = 0f;
        while (elapsed < flashTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / flashTime;
            // Rapid pulsing flash
            float pulse = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 8f));
            if (glowMat != null)
            {
                glowMat.color = Color.Lerp(Color.red, Color.white, pulse * 0.3f);
                glowMat.SetColor("_EmissionColor", bombRed * Mathf.Lerp(flashIntensity, flashIntensity * 2f, pulse));
            }
            // Vibrate
            Vector3 shake = Random.insideUnitSphere * 0.03f * (0.5f + t);
            shake.y = 0f;
            drone.position = pullBackTarget + shake;
            // Turn to face the wall
            float turnT = Mathf.SmoothStep(0f, 1f, t);
            drone.rotation = Quaternion.Slerp(
                Quaternion.LookRotation(intoRoom),
                Quaternion.LookRotation(-intoRoom),
                turnT);
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = pullBackTarget;
        drone.rotation = Quaternion.LookRotation(-intoRoom);

        // ── Phase 3: Charge into the wall ──
        elapsed = 0f;
        while (elapsed < chargeTime)
        {
            if (token != animationToken) yield break;
            float t = elapsed / chargeTime;
            // Ease in (accelerate)
            float ease = t * t;
            Vector3 pos = Vector3.Lerp(pullBackTarget, impactPos, ease);
            pos.y = hoverY - t * 0.1f; // slight dive
            drone.position = pos;
            // Keep flashing intensely during charge
            if (glowMat != null)
            {
                float flash = Mathf.Abs(Mathf.Sin(t * Mathf.PI * 12f));
                glowMat.SetColor("_EmissionColor", bombRed * (flashIntensity * (1f + flash + t * 2f)));
            }
            elapsed += Time.deltaTime;
            yield return null;
        }
        drone.position = impactPos;

        // Restore glow (drone is about to be destroyed anyway, but just in case)
        if (glowMat != null)
            glowMat.SetColor("_EmissionColor", originalGlow);

        activeAnimation = null;
        if (token == animationToken) onComplete?.Invoke();
    }
}
