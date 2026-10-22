using System.Collections.Generic;
using NeonVelocity.Utilities;
using UnityEngine;
using UnityEngine.Rendering;

namespace NeonVelocity.Effects
{
    /// <summary>
    /// Pooled visual effects. Particle systems are created once and reused; no prefabs or
    /// particle assets are required. Reduced-motion mode lowers particle counts globally.
    /// </summary>
    public sealed class VfxService : MonoBehaviour
    {
        private sealed class BurstPool
        {
            public ParticleSystem System;
            public ParticleSystemRenderer Renderer;
        }

        private readonly List<BurstPool> _bursts = new List<BurstPool>();
        private int _next;
        private bool _reduced;
        private int _budget = 24;
        private ParticleSystem _speedLines;
        private ParticleSystem _engineTrail;

        public static VfxService Create(Transform parent, bool reducedMotion, int budget)
        {
            var go = new GameObject("VFX");
            go.transform.SetParent(parent, false);
            var vfx = go.AddComponent<VfxService>();
            vfx._reduced = reducedMotion;
            vfx._budget = Mathf.Clamp(budget, 8, 28);
            vfx.Build();
            return vfx;
        }

        private void Build()
        {
            for (int i = 0; i < _budget; i++) _bursts.Add(BuildBurst(i));
        }

        private BurstPool BuildBurst(int index)
        {
            var go = new GameObject("Burst_" + index);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            // A freshly added ParticleSystem is already playing; stop it before touching duration.
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.duration = 0.5f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.62f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.4f, 8.5f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.32f);
            main.startColor = Color.white;
            main.gravityModifier = 0.15f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 120;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            var emission = ps.emission;
            emission.enabled = true;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, 10) });
            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.16f;
            shape.radiusThickness = 1f;

            var colorOverLife = ps.colorOverLifetime;
            colorOverLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colorOverLife.color = new ParticleSystem.MinMaxGradient(gradient);

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f,
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.05f)));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = MaterialLibrary.Additive(Color.white);
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sortMode = ParticleSystemSortMode.Distance;
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return new BurstPool { System = ps, Renderer = renderer };
        }

        private BurstPool Next()
        {
            var pool = _bursts[_next];
            _next = (_next + 1) % _bursts.Count;
            return pool;
        }

        public void Burst(Vector3 position, Color color, int count, float radius = 0.35f, float speed = 6f)
        {
            var pool = Next();
            var main = pool.System.main;
            main.startColor = new ParticleSystem.MinMaxGradient(Color.Lerp(color, Color.white, 0.35f), color);
            var emission = pool.System.emission;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, Mathf.Max(1, _reduced ? count / 3 : count)) });
            var shape = pool.System.shape;
            shape.radius = Mathf.Max(0.05f, radius);
            pool.System.transform.position = position;
            pool.System.Clear();
            pool.System.Play();
        }

        public void Impact(Vector3 position, Color color)
        {
            Burst(position, color, _reduced ? 14 : 34, 0.4f, 11f);
            Burst(position + Vector3.up * 0.4f, Color.white, _reduced ? 4 : 12, 0.7f, 4f);
        }

        public void Dust(Vector3 position, Color color, int count)
        {
            Burst(position, Color.Lerp(color, Color.white, 0.2f), _reduced ? Mathf.Max(1, count / 2) : count, 0.5f, 2.4f);
        }

        public void Pickup(Vector3 position, Color color, bool rare)
        {
            Burst(position, color, _reduced ? 3 : (rare ? 24 : 8), rare ? 0.6f : 0.25f, rare ? 8f : 4f);
        }

        public void CreateSpeedLines(Camera camera, Color color)
        {
            if (_speedLines != null) return;
            var go = new GameObject("Speed Lines");
            go.transform.SetParent(camera.transform, false);
            go.transform.localPosition = new Vector3(0f, 0f, 2f);
            _speedLines = go.AddComponent<ParticleSystem>();
            var main = _speedLines.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.4f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(10f, 24f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.015f, 0.05f);
            main.startColor = new Color(color.r, color.g, color.b, 0.5f);
            main.simulationSpace = ParticleSystemSimulationSpace.Local;
            main.maxParticles = _reduced ? 30 : 90;
            var emission = _speedLines.emission;
            emission.rateOverTime = 0f;
            var shape = _speedLines.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(12f, 6f, 10f);
            shape.position = new Vector3(0f, 0f, 18f);
            var velocity = _speedLines.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.Local;
            velocity.z = new ParticleSystem.MinMaxCurve(-18f, -26f);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = MaterialLibrary.Additive(color);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.12f;
            renderer.lengthScale = 4f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _speedLines.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        public void SetSpeedLines(bool active, float intensity = 1f)
        {
            if (_speedLines == null) return;
            var emission = _speedLines.emission;
            if (active)
            {
                if (!_speedLines.isPlaying) _speedLines.Play();
                emission.rateOverTime = (_reduced ? 12f : 42f) * Mathf.Clamp01(intensity);
            }
            else
            {
                emission.rateOverTime = 0f;
            }
        }

        public void CreateEngineTrail(Transform parent, Color color)
        {
            if (_engineTrail != null) return;
            var go = new GameObject("Engine Trail");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = new Vector3(0f, 0.95f, -0.25f);
            _engineTrail = go.AddComponent<ParticleSystem>();
            var main = _engineTrail.main;
            main.loop = true;
            main.playOnAwake = false;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.18f, 0.34f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.01f, 0.16f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.04f, 0.13f);
            main.startColor = new Color(color.r, color.g, color.b, 0.6f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = _reduced ? 20 : 55;
            var emission = _engineTrail.emission;
            emission.rateOverTime = _reduced ? 16f : 38f;
            var shape = _engineTrail.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 12f;
            shape.radius = 0.1f;
            var colorLife = _engineTrail.colorOverLifetime;
            colorLife.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(color, 0f), new GradientColorKey(Color.white, 0.2f), new GradientColorKey(color, 1f) },
                new[] { new GradientAlphaKey(0.7f, 0f), new GradientAlphaKey(0f, 1f) });
            colorLife.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.material = MaterialLibrary.Additive(color);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            _engineTrail.Play();
        }

        public void SetEngineTrailColor(Color color)
        {
            if (_engineTrail == null) return;
            var main = _engineTrail.main;
            main.startColor = new Color(color.r, color.g, color.b, 0.65f);
        }
    }
}
