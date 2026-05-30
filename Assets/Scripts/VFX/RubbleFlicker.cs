using UnityEngine;

/// <summary>
/// Flickers an emissive material's emission intensity to simulate a damaged warning light.
/// </summary>
public class RubbleFlicker : MonoBehaviour
{
    Renderer rend;
    Material mat;
    Color baseEmission;
    float timer;
    float nextFlicker;
    bool isOn = true;

    void Start()
    {
        rend = GetComponent<Renderer>();
        mat = rend.material; // instance so we don't affect shared
        baseEmission = mat.GetColor("_EmissionColor");
        nextFlicker = Random.Range(0.05f, 0.3f);
        // Offset timer so lights don't all sync
        timer = Random.Range(0f, 0.5f);
    }

    void Update()
    {
        timer += Time.deltaTime;
        if (timer >= nextFlicker)
        {
            timer = 0f;
            isOn = !isOn;
            // Irregular timing: short on, variable off
            nextFlicker = isOn
                ? Random.Range(0.08f, 0.25f)
                : Random.Range(0.03f, 0.15f);

            float intensity = isOn ? Random.Range(0.6f, 1.0f) : Random.Range(0f, 0.15f);
            mat.SetColor("_EmissionColor", baseEmission * intensity);
        }
    }
}
