using System;
using Timberborn.BaseComponentSystem;
using Timberborn.EntitySystem;
using Timberborn.LevelVisibilitySystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Wyrm
{
    /// <summary>
    /// Sustained dirt eruption when a wyrm emerges. Spawned alongside
    /// the wyrm by <see cref="WyrmFactory"/>; emits brown soft-dot
    /// particles for <see cref="EmissionDurationSeconds"/>, then stops
    /// emitting and lets in-flight clods fall under gravity for another
    /// <see cref="ParticleLifetime"/> seconds. Procedural texture and
    /// material are cached statically (no AssetBundle, no PNG); shared
    /// with <see cref="HazardEmergenceVisual"/> via
    /// <see cref="GetSharedDirtMaterial"/>.
    /// </summary>
    public class WyrmEmergenceDirtEffect : TickableComponent,
        IInitializableEntity, IDeletableEntity
    {
        private const float EmissionDurationSeconds = 2.5f;
        private const float EmissionRatePerSecond = 35f;
        private const int InitialBurstCount = 25;
        private const float ParticleLifetime = 1.5f;
        private const float MinSpeed = 1.2f;
        private const float MaxSpeed = 3.0f;
        private const float MinSize = 0.06f;
        private const float MaxSize = 0.16f;

        // Equal to the hemisphere shape radius below so the dome's base
        // sits flush with the surface. Keep these in sync.
        private const float HemisphereLift = 0.25f;

        // One Texture2D / Material instance for every wyrm. Created
        // lazily, never destroyed.
        private static Texture2D _sharedDirtTexture;
        private static Material _sharedDirtMaterial;

        private readonly ILevelVisibilityService _levelVisibilityService;

        private GameObject _effectsRoot;
        private ParticleSystem _dirtSystem;

        private float _emissionTimeRemaining;
        private bool _emitting;

        // Particle shaders don't honour Timberborn's
        // _USE_LEVEL_VISIBILITY keyword, so we toggle the GameObject
        // ourselves on layer-slider changes.
        private bool _hiddenByLevel;

        public WyrmEmergenceDirtEffect(ILevelVisibilityService levelVisibilityService)
        {
            _levelVisibilityService = levelVisibilityService;
        }

        public void InitializeEntity()
        {
            if (_levelVisibilityService != null)
            {
                _levelVisibilityService.MaxVisibleLevelChanged += OnMaxVisibleLevelChanged;
                RecomputeHiddenByLevel();
            }
            // try/catch so a missing shader at runtime doesn't break
            // wyrm spawning — the burst is cosmetic.
            try
            {
                EnsureSystem();
                if (_dirtSystem != null)
                {
                    // Initial burst gives a sharp leading edge; the
                    // continuous rate sustains the cloud while the
                    // wyrm rises.
                    _dirtSystem.Emit(InitialBurstCount);
                    _dirtSystem.Play();
                    _emitting = true;
                    _emissionTimeRemaining = EmissionDurationSeconds;
                }
                ApplyVisibility();
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not start emergence dirt effect: " +
                    $"{ex.Message}");
            }
        }

        public override void StartTickable()
        {
            // A loaded wyrm (post-save) shouldn't re-emerge; only stay
            // ticking if InitializeEntity flipped _emitting on.
            if (!_emitting)
            {
                DisableComponent();
            }
        }

        public override void Tick()
        {
            if (!_emitting || _dirtSystem == null)
            {
                DisableComponent();
                return;
            }
            // Time.deltaTime so a paused game also pauses the countdown.
            _emissionTimeRemaining -= Time.deltaTime;
            if (_emissionTimeRemaining > 0f) return;

            // Stop emitting; let in-flight clods finish their arc.
            _dirtSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            _emitting = false;
            DisableComponent();
        }

        public void DeleteEntity()
        {
            _emitting = false;
            _dirtSystem = null;
            if (_levelVisibilityService != null)
            {
                _levelVisibilityService.MaxVisibleLevelChanged -= OnMaxVisibleLevelChanged;
            }
            if (_effectsRoot != null)
            {
                UnityEngine.Object.Destroy(_effectsRoot);
                _effectsRoot = null;
            }
        }

        private void OnMaxVisibleLevelChanged(object sender, int e)
        {
            RecomputeHiddenByLevel();
            ApplyVisibility();
        }

        private void RecomputeHiddenByLevel()
        {
            if (_levelVisibilityService == null || Transform == null)
            {
                _hiddenByLevel = false;
                return;
            }
            // World Y → grid Z because Timberborn's grid has Z up. Same
            // convention HazardEmergenceVisual uses.
            Vector3 pos = Transform.position;
            var coord = new Vector3Int(
                Mathf.FloorToInt(pos.x),
                Mathf.FloorToInt(pos.z),
                Mathf.FloorToInt(pos.y));
            _hiddenByLevel = !_levelVisibilityService.BlockIsVisible(coord);
        }

        private void ApplyVisibility()
        {
            if (_effectsRoot == null) return;
            // SetActive pauses the simulation; just toggling the
            // renderer would let particles keep drifting through hidden
            // layers and reappear on slider toggle.
            _effectsRoot.SetActive(!_hiddenByLevel);
        }

        private void EnsureSystem()
        {
            if (_dirtSystem != null || Transform == null) return;

            _effectsRoot = new GameObject("WyrmEmergenceDirt");
            _effectsRoot.transform.SetParent(Transform, worldPositionStays: false);
            // Lift by the hemisphere radius so the dome's base sits
            // flush with the surface; without this, particles spawn
            // half-buried in the dirt below and shoot sideways out.
            _effectsRoot.transform.localPosition = new Vector3(0f, HemisphereLift, 0f);

            _dirtSystem = _effectsRoot.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_dirtSystem);
        }

        private static void ConfigureParticleSystem(ParticleSystem ps)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var material = GetSharedDirtMaterial();
            if (material != null)
            {
                renderer.material = material;
            }
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.alignment = ParticleSystemRenderSpace.View;
            renderer.shadowCastingMode =
                UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            var main = ps.main;
            // duration=1 + loop=true lets rateOverTime keep firing past
            // the 1s mark; Tick() owns the actual stop time.
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                ParticleLifetime * 0.6f, ParticleLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(MinSpeed, MaxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(MinSize, MaxSize);
            var startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.42f, 0.30f, 0.18f, 1f),
                new Color(0.55f, 0.42f, 0.25f, 1f));
            startColor.mode = ParticleSystemGradientMode.TwoColors;
            main.startColor = startColor;
            main.gravityModifier = 1.6f;
            // 35/s × 1.5s lifetime → ~53 in flight; doubled for headroom.
            main.maxParticles = 250;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = EmissionRatePerSecond;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.25f;
            // -90° around X aims the dome at world +Y; without it,
            // Unity's hemisphere emits along local +Z and clods shoot
            // sideways into the dirt.
            shape.rotation = new Vector3(-90f, 0f, 0f);

            var color = ps.colorOverLifetime;
            color.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.50f, 0.38f, 0.22f), 0f),
                    new GradientColorKey(new Color(0.35f, 0.28f, 0.18f), 1f),
                },
                new[]
                {
                    new GradientAlphaKey(0.95f, 0f),
                    new GradientAlphaKey(0.70f, 0.5f),
                    new GradientAlphaKey(0.00f, 1f),
                });
            color.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1.0f, 0f, 0f),
                new Keyframe(0.6f, 0.85f, 0f, 0f),
                new Keyframe(1f, 0.5f, 0f, 0f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);
        }

        /// <summary>
        /// Shared procedural dirt material used by both the wyrm-side
        /// emergence burst and the hazard-side warmup puffs.
        /// </summary>
        internal static Material GetSharedDirtMaterial()
        {
            if (_sharedDirtMaterial != null) return _sharedDirtMaterial;
            var shader = Shader.Find("Sprites/Default")
                ?? Shader.Find("Particles/Standard Unlit")
                ?? Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (shader == null)
            {
                Debug.LogWarning(
                    "[WhereWyrmsWait] No suitable particle shader found; " +
                    "emergence dirt visuals disabled.");
                return null;
            }
            _sharedDirtMaterial = new Material(shader)
            {
                name = "WWW_EmergenceDirt",
                color = Color.white,
                mainTexture = GetSharedTexture(),
            };
            return _sharedDirtMaterial;
        }

        private static Texture2D GetSharedTexture()
        {
            if (_sharedDirtTexture != null) return _sharedDirtTexture;
            const int size = 32;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, mipChain: false)
            {
                name = "WWW_DirtSoftDot",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
            };
            float half = size / 2f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x - half) / half;
                    float dy = (y - half) / half;
                    float dist = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = Mathf.Clamp01(1f - dist);
                    // Slightly harder falloff than a pure radial — reads
                    // as solid clods rather than mist.
                    alpha = alpha * alpha * (1.4f - 0.4f * alpha);
                    alpha = Mathf.Clamp01(alpha);
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
                }
            }
            tex.Apply(updateMipmaps: false, makeNoLongerReadable: true);
            _sharedDirtTexture = tex;
            return tex;
        }
    }
}
