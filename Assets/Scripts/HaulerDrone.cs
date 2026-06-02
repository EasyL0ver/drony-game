using UnityEngine;

/// <summary>
/// Hauler drone visual — wheeled ground drone with grabber arm.
/// No hover, no rotors. Wheels spin based on movement.
/// </summary>
public class HaulerDrone : MonoBehaviour, IDroneVisual
{
    [Header("Scale")]
    [SerializeField] float chassisLength = 0.55f;
    [SerializeField] float chassisWidth = 0.28f;

    [Header("Colors")]
    Color hullColor = new Color(0.18f, 0.15f, 0.10f);
    Color armColor = new Color(0.10f, 0.10f, 0.12f);
    Color glowColor = Palette.DroneIdle;
    [SerializeField] float glowIntensity = 3f;

    Transform[] wheels;
    Transform arm;
    Transform claw;
    Material matHull, matArm, matGlow;

    Vector3 lastPos;
    float wheelCircumference;

    /// <summary>The shared glow material used by all emissive parts.</summary>
    public Material GlowMaterial => matGlow;
    public Color BaseGlowColor => glowColor;
    public float BaseGlowIntensity => glowIntensity;

    void OnEnable()
    {
        if (transform.childCount > 0)
            FindParts();
        else
        {
            InitMaterials();
            Build();
        }
        lastPos = transform.position;
        float wheelR = chassisWidth * 0.4f;
        wheelCircumference = 2f * Mathf.PI * wheelR;
    }

    void Update()
    {
        if (!Application.isPlaying) return;

        // Wheel spin from movement
        Vector3 delta = transform.position - lastPos;
        float dist = new Vector2(delta.x, delta.z).magnitude;
        if (dist > 0.0001f && wheels != null)
        {
            float angleDeg = (dist / wheelCircumference) * 360f;
            // Determine forward direction vs movement for sign
            float dot = Vector3.Dot(transform.forward, delta.normalized);
            float sign = dot >= 0 ? 1f : -1f;
            for (int i = 0; i < wheels.Length; i++)
            {
                if (wheels[i] != null)
                    wheels[i].Rotate(Vector3.right, sign * angleDeg, Space.Self);
            }
        }
        lastPos = transform.position;

        // Arm idle bob
        if (arm != null)
        {
            float bob = Mathf.Sin(Time.time * 1.5f) * 0.005f;
            var lp = arm.localPosition;
            arm.localPosition = new Vector3(lp.x, lp.y, lp.z + bob * 0.3f);
        }
    }

    void InitMaterials()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        matHull = new Material(sh) { color = hullColor };
        matHull.SetFloat("_Smoothness", 0.25f);

        matArm = new Material(sh) { color = armColor };
        matArm.SetFloat("_Smoothness", 0.2f);

        matGlow = new Material(sh) { color = glowColor };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowColor * glowIntensity);
        matGlow.SetFloat("_Smoothness", 0.9f);
    }

    void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        var result = HaulerDroneMesh.Build(transform, chassisLength, chassisWidth,
            matHull, matArm, matGlow);
        wheels = result.wheels;
        arm = result.arm;
        claw = result.claw;
    }

    void FindParts()
    {
        wheels = new Transform[4];
        for (int i = 0; i < 4; i++)
            wheels[i] = transform.Find($"Wheel{i}");
        arm = transform.Find("ArmPillar");
        claw = transform.Find("Claw");
    }

    // ── flash ──────────────────────────────

    Coroutine flashRoutine;

    public void Flash(Color color, float duration = 0.3f)
    {
        if (flashRoutine != null) StopCoroutine(flashRoutine);
        flashRoutine = StartCoroutine(FlashRoutine(color, duration));
    }

    System.Collections.IEnumerator FlashRoutine(Color color, float duration)
    {
        if (matGlow == null) yield break;
        matGlow.SetColor("_EmissionColor", color * glowIntensity * 2f);
        matGlow.color = color;

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float lerp = t / duration;
            Color c = Color.Lerp(color, glowColor, lerp);
            float intensity = Mathf.Lerp(glowIntensity * 2f, glowIntensity, lerp);
            matGlow.SetColor("_EmissionColor", c * intensity);
            matGlow.color = c;
            yield return null;
        }

        matGlow.SetColor("_EmissionColor", glowColor * glowIntensity);
        matGlow.color = glowColor;
        flashRoutine = null;
    }
}
