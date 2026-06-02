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

    const float signalDuration = 0.6f;
    const float openDuration = 0.8f;
    const float closeDelay = 0.5f;

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
        // 1. Signal: flash door glow and drone glow to green
        Color signalGreen = new Color(0.2f, 1f, 0.3f);
        Color origGlow = Color.clear;
        Material glowMat = glowRenderer != null ? glowRenderer.sharedMaterial : null;
        if (glowMat != null)
        {
            origGlow = glowMat.GetColor("_EmissionColor");
            glowMat.SetColor("_EmissionColor", signalGreen * 4f);
        }
        IDroneVisual droneVisual = drone.GetComponentInChildren<LowPolyDrone>() as IDroneVisual
                                ?? drone.GetComponentInChildren<HaulerDrone>() as IDroneVisual;
        droneVisual?.Flash(signalGreen, signalDuration);

        yield return new WaitForSeconds(signalDuration);
        if (animationToken != token) yield break;

        // 2. Animate door open (scale Y to 0, sliding up)
        if (glowRenderer != null) glowRenderer.enabled = false;
        Transform doorPanel = barrier != null ? barrier.transform.Find("DoorPanel") : null;
        Transform stripe = barrier != null ? barrier.transform.Find("WarningStripe") : null;
        Vector3 origDoorScale = doorPanel != null ? doorPanel.localScale : Vector3.one;
        Vector3 origDoorPos = doorPanel != null ? doorPanel.localPosition : Vector3.zero;
        Vector3 origStripeScale = stripe != null ? stripe.localScale : Vector3.one;
        Vector3 origStripePos = stripe != null ? stripe.localPosition : Vector3.zero;

        float elapsed = 0f;
        while (elapsed < openDuration)
        {
            if (animationToken != token) yield break;
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / openDuration);
            float scaleY = Mathf.Lerp(1f, 0f, t);

            if (doorPanel != null)
            {
                doorPanel.localScale = new Vector3(origDoorScale.x, origDoorScale.y * scaleY, origDoorScale.z);
                doorPanel.localPosition = new Vector3(origDoorPos.x, origDoorPos.y + origDoorScale.y * (1f - scaleY) * 0.5f, origDoorPos.z);
            }
            if (stripe != null)
            {
                stripe.localScale = new Vector3(origStripeScale.x, origStripeScale.y * scaleY, origStripeScale.z);
                stripe.localPosition = new Vector3(origStripePos.x, origStripePos.y + origStripeScale.y * (1f - scaleY) * 0.5f, origStripePos.z);
            }
            yield return null;
        }

        if (doorPanel != null) doorPanel.gameObject.SetActive(false);
        if (stripe != null) stripe.gameObject.SetActive(false);

        // 3. Normal traversal → on complete: close door and forward callback
        var capturedGlowMat = glowMat;
        var capturedOrigGlow = origGlow;
        var capturedDoorPanel = doorPanel;
        var capturedStripe = stripe;
        var capturedOrigDoorScale = origDoorScale;
        var capturedOrigDoorPos = origDoorPos;
        var capturedOrigStripeScale = origStripeScale;
        var capturedOrigStripePos = origStripePos;

        base.PlayTraversal(drone, duration, true, () =>
        {
            StartCoroutine(CloseDoorAfterDelay(
                capturedDoorPanel, capturedOrigDoorScale, capturedOrigDoorPos,
                capturedStripe, capturedOrigStripeScale, capturedOrigStripePos,
                capturedGlowMat, capturedOrigGlow));
            onComplete?.Invoke();
        });
    }

    IEnumerator CloseDoorAfterDelay(
        Transform doorPanel, Vector3 origScale, Vector3 origPos,
        Transform stripe, Vector3 stripeScale, Vector3 stripePos,
        Material gMat, Color origGlow)
    {
        yield return new WaitForSeconds(closeDelay);

        if (doorPanel != null)
        {
            doorPanel.gameObject.SetActive(true);
            doorPanel.localScale = origScale;
            doorPanel.localPosition = origPos;
        }
        if (stripe != null)
        {
            stripe.gameObject.SetActive(true);
            stripe.localScale = stripeScale;
            stripe.localPosition = stripePos;
        }
        if (glowRenderer != null) glowRenderer.enabled = true;
        if (gMat != null) gMat.SetColor("_EmissionColor", origGlow);
    }
}
