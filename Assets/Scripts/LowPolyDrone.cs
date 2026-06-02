using UnityEngine;

/// <summary>
/// Low-poly procedural drone that matches the hex-map art style.
/// Tiny geometric shapes — hex body, thin arms, flat 2-blade rotors.
/// </summary>
public class LowPolyDrone : MonoBehaviour, IDroneVisual
{
    [Header("Scale")]
    [SerializeField] float bodyRadius = 0.15f;
    [SerializeField] float armLength  = 0.18f;
    [SerializeField] float rotorSpeed = 2800f;

    [Header("Colors")]
    Color hullColor  = new Color(0.12f, 0.12f, 0.15f);
    Color armColor   = new Color(0.08f, 0.08f, 0.10f);
    Color glowColor  = Palette.DroneIdle;
    [SerializeField] float glowIntensity = 4f;

    Transform[] rotors;
    Material matHull, matArm, matGlow;
    float baseLocalY;

    /// <summary>The shared glow material used by all emissive parts.</summary>
    public Material GlowMaterial => matGlow;
    public Color BaseGlowColor => glowColor;
    public float BaseGlowIntensity => glowIntensity;

    // ── lifecycle ──────────────────────────

    void OnEnable()
    {
        if (transform.childCount > 0)
            FindRotors();
        else
        {
            InitMaterials();
            Build();
        }
        baseLocalY = transform.localPosition.y;
    }

    /// <summary>Manual rebuild from editor (right-click → Rebuild Drone, or menu).</summary>
    [ContextMenu("Rebuild Drone")]
    public void Rebuild()
    {
        InitMaterials();
        Build();
    }

    void Update()
    {
        if (!Application.isPlaying) return;
        if (rotors == null) return;

        float dt = Time.deltaTime;
        float[] dirs = { 1, -1, 1, -1 };
        for (int i = 0; i < rotors.Length; i++)
        {
            if (rotors[i] != null)
                rotors[i].Rotate(Vector3.up, dirs[i] * rotorSpeed * dt, Space.Self);
        }

        // gentle hover bob (relative to parent)
        float bob = Mathf.Sin(Time.time * 2.5f) * 0.03f;
        transform.localPosition = new Vector3(
            transform.localPosition.x,
            baseLocalY + bob,
            transform.localPosition.z);
    }

    // ── materials ──────────────────────────

    void InitMaterials()
    {
        Shader sh = Shader.Find("Universal Render Pipeline/Lit");
        if (sh == null) sh = Shader.Find("Standard");

        matHull = new Material(sh) { color = hullColor };
        matHull.SetFloat("_Smoothness", 0.3f);

        matArm = new Material(sh) { color = armColor };
        matArm.SetFloat("_Smoothness", 0.2f);

        matGlow = new Material(sh) { color = glowColor };
        matGlow.EnableKeyword("_EMISSION");
        matGlow.SetColor("_EmissionColor", glowColor * glowIntensity);
        matGlow.SetFloat("_Smoothness", 0.9f);
    }

    // ── build ──────────────────────────────

    void Build()
    {
        while (transform.childCount > 0)
            DestroyImmediate(transform.GetChild(0).gameObject);

        var result = DroneMesh.Build(transform, bodyRadius, armLength, matHull, matArm, matGlow);
        rotors = result.rotors;
    }

    void FindRotors()
    {
        rotors = new Transform[4];
        for (int i = 0; i < 4; i++)
        {
            var t = transform.Find($"Rotor{i}");
            if (t != null) rotors[i] = t;
        }
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
