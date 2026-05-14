using System;
using System.IO;
using Timberborn.Persistence;
using Timberborn.SingletonSystem;
using Timberborn.WorldPersistence;
using UnityEngine;

namespace Mods.DamDegradation.Core
{
    /// <summary>
    /// Mod-wide tuning that can be overridden by the player. Loaded from a
    /// <c>mod-settings.json</c> file in <see cref="Application.persistentDataPath"/>
    /// on first load and persisted into the save game thereafter so each save
    /// can keep its own balance.
    /// </summary>
    public class DamSettings : ILoadableSingleton, ISaveableSingleton
    {
        private static readonly SingletonKey SaveKey = new SingletonKey("DamDegradationSettings");
        private static readonly PropertyKey<bool> DegradationEnabledKey = new PropertyKey<bool>("DegradationEnabled");
        private static readonly PropertyKey<float> GlobalWearMultiplierKey = new PropertyKey<float>("GlobalWearMultiplier");
        private static readonly PropertyKey<float> RandomVarianceFractionKey = new PropertyKey<float>("RandomVarianceFraction");
        private static readonly PropertyKey<int> RepairCostInPlanksKey = new PropertyKey<int>("RepairCostInPlanks");
        private static readonly PropertyKey<float> RepairTimeInHoursKey = new PropertyKey<float>("RepairTimeInHours");
        private static readonly PropertyKey<bool> AllowInstantRepairKey = new PropertyKey<bool>("AllowInstantRepair");

        private const string SettingsFileName = "DamDegradationSettings.json";

        private readonly ISingletonLoader _singletonLoader;

        public DamSettings(ISingletonLoader singletonLoader)
        {
            _singletonLoader = singletonLoader;
        }

        /// <summary>Master switch. When false the mod ticks but never applies wear.</summary>
        public bool DegradationEnabled { get; private set; } = true;

        /// <summary>Multiplier applied on top of every block's per-spec wear.</summary>
        public float GlobalWearMultiplier { get; private set; } = 1.0f;

        /// <summary>Random per-tick variance, e.g. 0.15 → ±15%.</summary>
        public float RandomVarianceFraction { get; private set; } = 0.15f;

        /// <summary>How many planks a single repair action consumes. Reserved for v2 — currently unused.</summary>
        public int RepairCostInPlanks { get; private set; } = 0;

        /// <summary>How many in-game hours a beaver spends on a single repair.</summary>
        public float RepairTimeInHours { get; private set; } = 4f;

        /// <summary>If true, the entity-panel button repairs instantly (free, no beaver). Useful for debugging.</summary>
        public bool AllowInstantRepair { get; private set; } = false;

        public void Load()
        {
            if (_singletonLoader.TryGetSingleton(SaveKey, out var loader))
            {
                LoadFromSave(loader);
            }
            else
            {
                LoadFromJsonFileOrDefaults();
            }
        }

        public void Save(ISingletonSaver singletonSaver)
        {
            var saver = singletonSaver.GetSingleton(SaveKey);
            saver.Set(DegradationEnabledKey, DegradationEnabled);
            saver.Set(GlobalWearMultiplierKey, GlobalWearMultiplier);
            saver.Set(RandomVarianceFractionKey, RandomVarianceFraction);
            saver.Set(RepairCostInPlanksKey, RepairCostInPlanks);
            saver.Set(RepairTimeInHoursKey, RepairTimeInHours);
            saver.Set(AllowInstantRepairKey, AllowInstantRepair);
        }

        private void LoadFromSave(IObjectLoader loader)
        {
            if (loader.Has(DegradationEnabledKey))
            {
                DegradationEnabled = loader.Get(DegradationEnabledKey);
            }
            if (loader.Has(GlobalWearMultiplierKey))
            {
                GlobalWearMultiplier = Mathf.Max(0f, loader.Get(GlobalWearMultiplierKey));
            }
            if (loader.Has(RandomVarianceFractionKey))
            {
                RandomVarianceFraction = Mathf.Clamp(loader.Get(RandomVarianceFractionKey), 0f, 0.9f);
            }
            if (loader.Has(RepairCostInPlanksKey))
            {
                RepairCostInPlanks = Mathf.Max(0, loader.Get(RepairCostInPlanksKey));
            }
            if (loader.Has(RepairTimeInHoursKey))
            {
                RepairTimeInHours = Mathf.Max(0.1f, loader.Get(RepairTimeInHoursKey));
            }
            if (loader.Has(AllowInstantRepairKey))
            {
                AllowInstantRepair = loader.Get(AllowInstantRepairKey);
            }
        }

        private void LoadFromJsonFileOrDefaults()
        {
            var path = Path.Combine(Application.persistentDataPath, SettingsFileName);
            if (!File.Exists(path))
            {
                return;
            }
            try
            {
                var json = File.ReadAllText(path);
                var dto = JsonUtility.FromJson<SettingsDto>(json);
                if (dto == null)
                {
                    return;
                }
                if (dto.HasDegradationEnabled)
                {
                    DegradationEnabled = dto.DegradationEnabled;
                }
                if (dto.HasGlobalWearMultiplier)
                {
                    GlobalWearMultiplier = Mathf.Max(0f, dto.GlobalWearMultiplier);
                }
                if (dto.HasRandomVarianceFraction)
                {
                    RandomVarianceFraction = Mathf.Clamp(dto.RandomVarianceFraction, 0f, 0.9f);
                }
                if (dto.HasRepairCostInPlanks)
                {
                    RepairCostInPlanks = Mathf.Max(0, dto.RepairCostInPlanks);
                }
                if (dto.HasRepairTimeInHours)
                {
                    RepairTimeInHours = Mathf.Max(0.1f, dto.RepairTimeInHours);
                }
                if (dto.HasAllowInstantRepair)
                {
                    AllowInstantRepair = dto.AllowInstantRepair;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning(
                    $"[DamDegradation] Could not parse {SettingsFileName}: {ex.Message}. Using defaults.");
            }
        }

        [Serializable]
        private class SettingsDto
        {
            // JsonUtility doesn't support nullable types so we use sentinel
            // booleans to know whether a field was actually present in the file.
            public bool HasDegradationEnabled;
            public bool DegradationEnabled;

            public bool HasGlobalWearMultiplier;
            public float GlobalWearMultiplier;

            public bool HasRandomVarianceFraction;
            public float RandomVarianceFraction;

            public bool HasRepairCostInPlanks;
            public int RepairCostInPlanks;

            public bool HasRepairTimeInHours;
            public float RepairTimeInHours;

            public bool HasAllowInstantRepair;
            public bool AllowInstantRepair;
        }
    }
}
