using UnityEngine;
using System.Collections;

/// <summary>
/// Passage override for blast doors. Handles the open/close animation sequence:
/// 1. Drone arrives at park point
/// 2. Signal: glow changes color briefly
/// 3. Door opens (barrier hides)
/// 4. Drone passes through (normal traversal)
/// 5. Door closes (barrier returns)
/// </summary>
public class BlastDoorPassage : Passage
{
    GameObject barrier;
    Renderer glowRenderer;

    const float signalDuration = 0.4f;
    const float openDuration = 0.3f;

    public void SetBarrier(GameObject barrierGO, Renderer glow)
    {
        barrier = barrierGO;
        glowRenderer = glow;
    }

    public override void PlayTraversal(Transform drone, float duration, bool departing, System.Action onComplete)
    {
        if (!departing || barrier == null)
        {
            // Arrival side or no barrier assigned — normal traversal
            base.PlayTraversal(drone, duration, departing, onComplete);
            return;
        }

        // Departure side: signal → open → traverse → close
        isReversing = false;
        int token = ++animationToken;
        activeAnimation = StartCoroutine(BlastDoorSequence(drone, duration, token, onComplete));
    }

    IEnumerator BlastDoorSequence(Transform drone, float duration, int token, System.Action onComplete)
    {
        // 1. Signal: flash glow to green
        Color origGlow = Color.clear;
        Material glowMat = glowRenderer != null ? glowRenderer.sharedMaterial : null;
        if (glowMat != null)
        {
            origGlow = glowMat.GetColor("_EmissionColor");
            glowMat.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.3f) * 4f);
        }

        yield return new WaitForSeconds(signalDuration);
        if (animationToken != token) yield break;

        // 2. Open door
        if (barrier != null) barrier.SetActive(false);
        if (glowRenderer != null) glowRenderer.enabled = false;

        yield return new WaitForSeconds(openDuration);
        if (animationToken != token) yield break;

        // 3. Normal traversal
        bool done = false;
        base.PlayTraversal(drone, duration, true, () => done = true);

        while (!done)
        {
            if (animationToken != token) yield break;
            yield return null;
        }

        // 4. Close door
        if (barrier != null) barrier.SetActive(true);
        if (glowRenderer != null) glowRenderer.enabled = true;
        if (glowMat != null) glowMat.SetColor("_EmissionColor", origGlow);

        onComplete?.Invoke();
    }
}
