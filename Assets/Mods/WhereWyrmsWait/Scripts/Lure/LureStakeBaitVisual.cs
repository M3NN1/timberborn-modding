using Timberborn.BaseComponentSystem;
using Timberborn.BlockSystem;
using Timberborn.Buildings;
using Timberborn.EntitySystem;
using Timberborn.InventorySystem;
using UnityEngine;

namespace Mods.WhereWyrmsWait.Lure
{
    /// <summary>
    /// Toggles three bait sub-models attached to the Lure Stake's
    /// finished building based on Soothesop stock:
    /// <c>Bait_Low</c> / <c>Bait_Mid</c> / <c>Bait_Full</c>.
    /// At most one is active at a time; the empty stake hides all
    /// three.
    /// <para>
    /// The bait sub-meshes live in their own <c>.timbermesh</c> files
    /// because the official Blender exporter (1.2.0) merges every
    /// visible mesh in a single export into one node. Listing each
    /// variant under <c>Children</c> in the blueprint sidesteps that
    /// limitation and creates a sub-<see cref="GameObject"/> per node
    /// with the names this component looks up. If a node is renamed in
    /// the blueprint, update the matching constant here (and vice
    /// versa).
    /// </para>
    /// </summary>
    public class LureStakeBaitVisual : BaseComponent,
        IInitializableEntity, IDeletableEntity, IFinishedStateListener
    {
        private const string BaitLowName = "Bait_Low";
        private const string BaitMidName = "Bait_Mid";
        private const string BaitFullName = "Bait_Full";

        // Threshold tuning. With default Capacity = 5:
        //   stock 0           → none
        //   stock 1           → Low
        //   stock 2-3         → Mid (≥ 40% of capacity)
        //   stock 4-5         → Full (≥ 80% of capacity)
        // Using fractions instead of absolute counts keeps the buckets
        // sensible if Capacity is tuned per blueprint.
        private const float MidThresholdFraction = 0.4f;
        private const float FullThresholdFraction = 0.8f;

        private LureStake _stake;
        private Inventory _inventory;
        private BuildingModel _buildingModel;

        private GameObject _baitLow;
        private GameObject _baitMid;
        private GameObject _baitFull;
        private GameObject _activeBait;

        private bool _builtAndFinished;
        private bool _missingNodesWarned;

        public void InitializeEntity()
        {
            _stake = GetComponent<LureStake>();
            _buildingModel = GetComponent<BuildingModel>();
            _inventory = _stake?.Inventory;
            if (_inventory != null)
            {
                _inventory.InventoryStockChanged += OnInventoryStockChanged;
                _inventory.InventoryEnabled += OnInventoryStateChanged;
                _inventory.InventoryDisabled += OnInventoryStateChanged;
            }
        }

        public void DeleteEntity()
        {
            if (_inventory != null)
            {
                _inventory.InventoryStockChanged -= OnInventoryStockChanged;
                _inventory.InventoryEnabled -= OnInventoryStateChanged;
                _inventory.InventoryDisabled -= OnInventoryStateChanged;
            }
            _baitLow = _baitMid = _baitFull = _activeBait = null;
        }

        public void OnEnterFinishedState()
        {
            ResolveBaitNodes();
            _builtAndFinished = true;
            UpdateVisual();
        }

        public void OnExitFinishedState()
        {
            // Construction-mode preview should not show bait.
            _builtAndFinished = false;
            HideAll();
        }

        private void OnInventoryStockChanged(
            object sender, InventoryAmountChangedEventArgs e) => UpdateVisual();

        private void OnInventoryStateChanged(
            object sender, System.EventArgs e) => UpdateVisual();

        private void ResolveBaitNodes()
        {
            if (_buildingModel == null || _buildingModel.FinishedModel == null)
            {
                return;
            }
            var root = _buildingModel.FinishedModel.transform;
            _baitLow  = root.Find(BaitLowName)?.gameObject;
            _baitMid  = root.Find(BaitMidName)?.gameObject;
            _baitFull = root.Find(BaitFullName)?.gameObject;
            HideAll();
        }

        private void UpdateVisual()
        {
            if (!_builtAndFinished) return;

            if (_baitLow == null && _baitMid == null && _baitFull == null)
            {
                if (!_missingNodesWarned)
                {
                    Debug.LogWarning(
                        "[WhereWyrmsWait] LureStake #Finished has no " +
                        $"'{BaitLowName}', '{BaitMidName}', or " +
                        $"'{BaitFullName}' child. Bait visual disabled.");
                    _missingNodesWarned = true;
                }
                return;
            }

            GameObject target = _inventory != null && _inventory.Enabled
                ? PickBaitForStock()
                : null;
            SwitchActive(target);
        }

        private GameObject PickBaitForStock()
        {
            int capacity = _stake?.Capacity ?? 0;
            float stock = _stake?.Stock ?? 0f;
            if (capacity <= 0 || stock <= 0f)
            {
                return null;
            }
            float fraction = Mathf.Clamp01(stock / capacity);

            // Prefer the highest-tier mesh whose threshold is met. Fall
            // back to lower tiers if the higher one's node is missing,
            // so a partially-authored timbermesh still gives some
            // visible feedback.
            if (fraction >= FullThresholdFraction && _baitFull != null) return _baitFull;
            if (fraction >= MidThresholdFraction && _baitMid != null) return _baitMid;
            if (_baitLow != null) return _baitLow;
            return _baitMid ?? _baitFull;
        }

        private void SwitchActive(GameObject target)
        {
            if (_activeBait == target) return;
            if (_activeBait != null) _activeBait.SetActive(false);
            if (target != null) target.SetActive(true);
            _activeBait = target;
        }

        private void HideAll()
        {
            if (_baitLow != null) _baitLow.SetActive(false);
            if (_baitMid != null) _baitMid.SetActive(false);
            if (_baitFull != null) _baitFull.SetActive(false);
            _activeBait = null;
        }
    }
}
