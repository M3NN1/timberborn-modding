using System;
using Mods.WhereWyrmsWait.Wyrm;
using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.EntitySystem;
using Timberborn.LevelVisibilitySystem;
using Timberborn.TickSystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Hazards
{
    /// <summary>
    /// Procedural pre-emergence dirt teaser on the surface where a wyrm
    /// is about to climb out. Polls <see cref="WyrmEmergencePicker"/>
    /// every <see cref="TileResampleSeconds"/> for the current target
    /// tile (it shifts as buildings move) and emits puffs whose cadence
    /// scales with <see cref="IWyrmHazard.WarmupFraction"/>:
    /// <list type="bullet">
    /// <item>Below 5%: silent.</item>
    /// <item>5–95%: discrete puffs every 1.5–3 wall-clock seconds.</item>
    /// <item>≥95%: constant emission until the wyrm spawns.</item>
    /// </list>
    /// Wall-clock seconds, not in-game days, so the rhythm reads the
    /// same regardless of game speed. Shares the procedural texture
    /// with <see cref="WyrmEmergenceDirtEffect"/>.
    /// </summary>
    public class HazardEmergenceVisual : TickableComponent,
        IInitializableEntity, IDeletableEntity
    {
        private const float VisibleThreshold = 0.05f;
        private const float ClimaxThreshold = 0.95f;
        private const float TileResampleSeconds = 1.5f;
        private const float MinPuffIntervalSeconds = 1.5f;
        private const float MaxPuffIntervalSeconds = 3.0f;
        private const int PerPuffParticleCount = 6;
        // Lower than the wyrm-side burst (35/s) so the climax visibly
        // intensifies once the wyrm actually spawns.
        private const float ClimaxEmissionRate = 18f;
        private const float ParticleLifetime = 1.4f;
        private const float MinSpeed = 0.4f;
        private const float MaxSpeed = 1.4f;
        private const float MinSize = 0.05f;
        private const float MaxSize = 0.10f;

        // Equal to the hemisphere shape radius so the dome's base sits
        // flush with the surface. Keep in sync with shape.radius below.
        private const float HemisphereLift = 0.18f;

        private readonly WyrmEmergencePicker _emergencePicker;
        private readonly ILevelVisibilityService _levelVisibilityService;

        private IWyrmHazard _hazard;
        private BlockObject _blockObject;

        private GameObject _effectsRoot;
        private ParticleSystem _puffSystem;

        private Vector3Int _currentTargetTile;
        private bool _hasTargetTile;
        private float _resampleTimer;
        private float _puffTimer;
        private bool _climaxEmitting;
        private bool _hiddenByLevel;

        public HazardEmergenceVisual(
            WyrmEmergencePicker emergencePicker,
            ILevelVisibilityService levelVisibilityService)
        {
            _emergencePicker = emergencePicker;
            _levelVisibilityService = levelVisibilityService;
        }

        public void InitializeEntity()
        {
            _blockObject = GetComponent<BlockObject>();
            _hazard = GetComponent<WyrmHusk>() as IWyrmHazard
                   ?? GetComponent<WyrmDen>() as IWyrmHazard;
            if (_hazard == null)
            {
                DisableComponent();
            }
            if (_levelVisibilityService != null)
            {
                _levelVisibilityService.MaxVisibleLevelChanged += OnMaxVisibleLevelChanged;
            }
        }

        public override void Tick()
        {
            if (_hazard == null || _blockObject == null) return;

            float fraction = Mathf.Clamp01(_hazard.WarmupFraction);
            bool active = _hazard.SurfaceIsGreen && fraction >= VisibleThreshold;
            if (!active)
            {
                StopClimaxIfActive();
                return;
            }

            ResampleTargetTileIfDue();
            if (!_hasTargetTile) return;

            EnsureSystem();
            if (_puffSystem == null) return;

            if (_hiddenByLevel)
            {
                StopClimaxIfActive();
                return;
            }

            if (fraction >= ClimaxThreshold)
            {
                EnsureClimaxEmission();
            }
            else
            {
                StopClimaxIfActive();
                EmitPuffIfDue(fraction);
            }
        }

        public void DeleteEntity()
        {
            _hazard = null;
            _puffSystem = null;
            _hasTargetTile = false;
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
            if (_levelVisibilityService == null || !_hasTargetTile)
            {
                _hiddenByLevel = false;
                return;
            }
            _hiddenByLevel =
                !_levelVisibilityService.BlockIsVisible(_currentTargetTile);
        }

        private void ApplyVisibility()
        {
            if (_effectsRoot == null) return;
            // SetActive(false) pauses the system entirely so in-flight
            // particles stop drifting. Without this they'd keep floating
            // through hidden layers because the GameObject is unparented
            // and uses world-space simulation.
            _effectsRoot.SetActive(!_hiddenByLevel);
        }

        private void ResampleTargetTileIfDue()
        {
            _resampleTimer -= Time.deltaTime;
            if (_resampleTimer > 0f && _hasTargetTile) return;
            _resampleTimer = TileResampleSeconds;

            int radius = ResolveRadius();
            // Multi-block hazard support: a 2×2×2 den exposes four
            // probe origins. Try each until one finds an open surface
            // tile. A 1×1 husk collapses to a single attempt.
            bool resolved = false;
            Vector3Int picked = default;
            foreach (var origin in _hazard.EmergenceProbeCells)
            {
                if (_emergencePicker.TryPick(origin, radius, out var cell))
                {
                    picked = cell;
                    resolved = true;
                    break;
                }
            }
            if (resolved)
            {
                if (picked != _currentTargetTile || !_hasTargetTile)
                {
                    _currentTargetTile = picked;
                    _hasTargetTile = true;
                    if (_effectsRoot != null)
                    {
                        // Move the existing system rather than rebuilding
                        // it — rebuilding would cut all in-flight clods.
                        _effectsRoot.transform.position =
                            TileToWorldPosition(picked);
                    }
                    RecomputeHiddenByLevel();
                    ApplyVisibility();
                }
            }
            else
            {
                _hasTargetTile = false;
            }
        }

        private int ResolveRadius()
        {
            var husk = _hazard as WyrmHusk;
            if (husk?.Spec != null) return husk.Spec.EmergenceShiftRadius;
            var den = _hazard as WyrmDen;
            if (den?.Spec != null) return den.Spec.EmergenceShiftRadius;
            return 2;
        }

        private void EnsureSystem()
        {
            if (_puffSystem != null) return;

            _effectsRoot = new GameObject("HazardEmergencePuffs");
            // World-space sim + unparented so we can move the GameObject
            // without dragging in-flight particles along.
            _effectsRoot.transform.position =
                TileToWorldPosition(_currentTargetTile);
            _effectsRoot.transform.SetParent(null, worldPositionStays: true);

            _puffSystem = _effectsRoot.AddComponent<ParticleSystem>();
            ConfigureParticleSystem(_puffSystem);
            ApplyVisibility();
        }

        private static void ConfigureParticleSystem(ParticleSystem ps)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            var material = WyrmEmergenceDirtEffect.GetSharedDirtMaterial();
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
            main.duration = 1f;
            main.loop = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(
                ParticleLifetime * 0.6f, ParticleLifetime);
            main.startSpeed = new ParticleSystem.MinMaxCurve(MinSpeed, MaxSpeed);
            main.startSize = new ParticleSystem.MinMaxCurve(MinSize, MaxSize);
            // Same earthy chord as the wyrm-side burst so the climax
            // reads as continuation, not a different effect.
            var startColor = new ParticleSystem.MinMaxGradient(
                new Color(0.42f, 0.30f, 0.18f, 1f),
                new Color(0.55f, 0.42f, 0.25f, 1f));
            startColor.mode = ParticleSystemGradientMode.TwoColors;
            main.startColor = startColor;
            main.gravityModifier = 1.4f;
            main.maxParticles = 80;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.playOnAwake = false;

            var emission = ps.emission;
            emission.rateOverTime = 0f;

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Hemisphere;
            shape.radius = 0.18f;
            // -90° around X aims the dome at world +Y; without it,
            // Unity's hemisphere emits along local +Z and puffs shoot
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
                    new GradientAlphaKey(0.85f, 0f),
                    new GradientAlphaKey(0.55f, 0.5f),
                    new GradientAlphaKey(0.00f, 1f),
                });
            color.color = grad;

            var sizeOverLife = ps.sizeOverLifetime;
            sizeOverLife.enabled = true;
            var sizeCurve = new AnimationCurve(
                new Keyframe(0f, 1.0f, 0f, 0f),
                new Keyframe(1f, 0.5f, 0f, 0f));
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, sizeCurve);

            // Play() so rateOverTime / Emit are honoured. Actual
            // emission is driven by Tick() via rateOverTime + Emit.
            ps.Play();
        }

        private void EmitPuffIfDue(float fraction)
        {
            _puffTimer -= Time.deltaTime;
            if (_puffTimer > 0f) return;
            try
            {
                _puffSystem.Emit(PerPuffParticleCount);
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[WhereWyrmsWait] Could not emit hazard puff: {ex.Message}");
            }
            _puffTimer = ResolveNextPuffInterval(fraction);
        }

        private static float ResolveNextPuffInterval(float fraction)
        {
            float t = Mathf.InverseLerp(VisibleThreshold, ClimaxThreshold, fraction);
            return Mathf.Lerp(MaxPuffIntervalSeconds, MinPuffIntervalSeconds, t);
        }

        private void EnsureClimaxEmission()
        {
            if (_climaxEmitting) return;
            var emission = _puffSystem.emission;
            emission.rateOverTime = ClimaxEmissionRate;
            _climaxEmitting = true;
        }

        private void StopClimaxIfActive()
        {
            if (!_climaxEmitting || _puffSystem == null) return;
            var emission = _puffSystem.emission;
            emission.rateOverTime = 0f;
            _climaxEmitting = false;
        }

        private static Vector3 TileToWorldPosition(Vector3Int tile)
        {
            // HemisphereLift plants the dome flush with the surface;
            // without it, particles spawn half-buried in the top dirt
            // voxel and shoot sideways out of it.
            return new Vector3(tile.x + 0.5f, tile.z + HemisphereLift, tile.y + 0.5f);
        }
    }
}
