using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Spawns a standalone explosion particle burst at a world position.
/// Self-destructs after the effect finishes.
/// </summary>
public static class ExplosionVFX
{
    public static void Spawn(Vector3 position)
    {
        var go = new GameObject("Explosion");
        go.transform.position = position;

        var ps = go.AddComponent<ParticleSystem>();
        // AddComponent auto-plays the system; stop it before editing duration etc.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main;
        main.loop = false;
        main.duration = 0.8f;
        main.startLifetime = new ParticleSystem.MinMaxCurve(0.4f, 0.8f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 8f);
        main.startSize = new ParticleSystem.MinMaxCurve(0.2f, 0.6f);
        main.maxParticles = 120;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.gravityModifier = 1.5f;
        main.startColor = new ParticleSystem.MinMaxGradient(
            new Color(1f, 0.6f, 0f),
            new Color(1f, 0.2f, 0f)
        );

        var emission = ps.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 80, 120) });

        var shape = ps.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = 0.3f;

        var sizeOverLife = ps.sizeOverLifetime;
        sizeOverLife.enabled = true;
        var sizeCurve = new AnimationCurve();
        sizeCurve.AddKey(0f, 1f);
        sizeCurve.AddKey(0.5f, 0.6f);
        sizeCurve.AddKey(1f, 0f);
        sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

        var colorOverLife = ps.colorOverLifetime;
        colorOverLife.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new[] {
                new GradientColorKey(new Color(1f, 0.8f, 0.2f), 0f),
                new GradientColorKey(new Color(1f, 0.3f, 0f), 0.4f),
                new GradientColorKey(new Color(0.3f, 0.1f, 0.05f), 1f)
            },
            new[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.3f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        colorOverLife.color = gradient;

        var renderer = go.GetComponent<ParticleSystemRenderer>();
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        Shader sh = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                    ?? Shader.Find("Particles/Standard Unlit");
        var mat = new Material(sh);
        mat.color = new Color(1f, 0.5f, 0f);
        mat.EnableKeyword("_EMISSION");
        mat.SetColor("_EmissionColor", new Color(2f, 1f, 0.2f));
        renderer.material = mat;

        ps.Play();
        Object.Destroy(go, 2f);
    }
}
