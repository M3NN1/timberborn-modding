using Timberborn.BaseComponentSystem;
using UnityEngine;

namespace Mods.DamDegradation.Components
{
    /// <summary>
    /// Placeholder visual feedback for dam health: tints the block's renderers
    /// yellow at the warning threshold and red at the critical threshold.
    /// <para>
    /// Uses <see cref="MaterialPropertyBlock"/> so the dam's shared materials
    /// stay untouched — no risk of recoloring every other dam in the world.
    /// </para>
    /// <para>
    /// To upgrade to real art: package damaged textures into an AssetBundle,
    /// swap this implementation for one that calls
    /// <c>MaterialPropertyBlock.SetTexture(_BaseMap, damagedTexture)</c>, and
    /// load the bundle via <c>IModEnvironment.ModPath</c>. The
    /// <see cref="IDamDeteriorationListener"/> contract stays the same.
    /// See <c>Placeholders/Art/README.md</c>.
    /// </para>
    /// </summary>
    public class DamDamageVisuals
        : BaseComponent, IAwakableComponent, IDamDeteriorationListener
    {
        // Tint multipliers applied to the existing material colour. Kept
        // intentionally subtle — heavy red would be ugly placeholder art.
        private static readonly Color HealthyTint = Color.white;
        private static readonly Color WarningTint = new Color(1f, 0.85f, 0.55f);
        private static readonly Color CriticalTint = new Color(1f, 0.45f, 0.40f);

        // Set both common shader colour properties so we cover whatever
        // shader the dam mesh actually uses (Built-in `_Color`, URP
        // `_BaseColor`).
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private MeshRenderer[] _renderers;
        private MaterialPropertyBlock _propertyBlock;
        private bool _inWarning;
        private bool _inCritical;
        private Color _currentTint = HealthyTint;

        public void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            _renderers = GameObject != null
                ? GameObject.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
                : new MeshRenderer[0];
        }

        public void OnEnterWarning(DamDeterioration dam)
        {
            _inWarning = true;
            ApplyTint();
        }

        public void OnExitWarning(DamDeterioration dam)
        {
            _inWarning = false;
            ApplyTint();
        }

        public void OnEnterCritical(DamDeterioration dam)
        {
            _inCritical = true;
            ApplyTint();
        }

        public void OnExitCritical(DamDeterioration dam)
        {
            _inCritical = false;
            ApplyTint();
        }

        public void OnRepaired(DamDeterioration dam, float amount)
        {
            // Threshold transitions trigger ApplyTint via the warning/critical
            // callbacks; nothing to do here.
        }

        public void OnBreached(DamDeterioration dam, Vector3Int coordinates)
        {
            // The entity is about to be deleted — no point updating tints.
        }

        private void ApplyTint()
        {
            Color target = _inCritical
                ? CriticalTint
                : (_inWarning ? WarningTint : HealthyTint);
            if (target == _currentTint || _renderers == null)
            {
                return;
            }
            _currentTint = target;
            foreach (var renderer in _renderers)
            {
                if (renderer == null)
                {
                    continue;
                }
                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(ColorId, target);
                _propertyBlock.SetColor(BaseColorId, target);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
