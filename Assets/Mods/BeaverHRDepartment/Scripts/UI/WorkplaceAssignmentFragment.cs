using Mods.BeaverHRDepartment.Assignment;
using System.Linq;
using Timberborn.BaseComponentSystem;
using Timberborn.Characters;
using Timberborn.CoreUI;
using Timberborn.EntityPanelSystem;
using Timberborn.WorkSystem;
using UnityEngine.UIElements;

namespace Mods.BeaverHRDepartment.UI
{

    public class WorkplaceAssignmentFragment : IEntityPanelFragment
    {

        private readonly VisualElementLoader _visualElementLoader;
        private readonly AssignmentService _assignmentService;

        private VisualElement _root;
        private VisualElement _workerList;
        private Button _assignButton;
        private VisualElement _pickerPanel;
        private Workplace _currentWorkplace;
        private bool _pickerVisible;
        private int _lastWorkerCount;

        public WorkplaceAssignmentFragment(
            VisualElementLoader visualElementLoader,
            AssignmentService assignmentService)
        {
            _visualElementLoader = visualElementLoader;
            _assignmentService = assignmentService;
        }

        public VisualElement InitializeFragment()
        {
            _root = _visualElementLoader.LoadVisualElement("WorkplaceAssignmentPanel");
            _workerList = _root.Q<VisualElement>("WorkerList");
            _assignButton = _root.Q<Button>("AssignButton");
            _pickerPanel = _root.Q<VisualElement>("PickerPanel");
            _assignButton.clicked += OnAssignClicked;
            Hide(_root);
            Hide(_pickerPanel);
            return _root;
        }

        public void ShowFragment(BaseComponent entity)
        {
            var workplace = entity.GetComponent<Workplace>();
            if (workplace == null) { Hide(_root); return; }
            _currentWorkplace = workplace;
            Show(_root);
            _pickerVisible = false;
            Hide(_pickerPanel);
            RefreshWorkerList();
        }

        public void ClearFragment()
        {
            Hide(_root);
            _currentWorkplace = null;
        }

        public void UpdateFragment()
        {
            if (_currentWorkplace == null) return;
            var workers = _currentWorkplace.AssignedWorkers;
            if (workers.Count != _lastWorkerCount)
            {
                RefreshWorkerList();
            }
        }

        private void RefreshWorkerList()
        {
            _workerList.Clear();
            var workers = _currentWorkplace.AssignedWorkers;
            _lastWorkerCount = workers.Count;

            for (int i = 0; i < workers.Count; i++)
            {
                var worker = workers[i];
                var row = new VisualElement();
                row.AddToClassList("bhr-worker-row");

                var label = new Label(worker.GetComponent<Character>()?.FirstName);
                label.AddToClassList("bhr-worker-label");

                var capturedWorker = worker;
                var removeBtn = new Button(() =>
                {
                    _assignmentService.UnassignWorker(capturedWorker, _currentWorkplace);
                    RefreshWorkerList();
                });
                removeBtn.AddToClassList("bhr-remove-button");
                removeBtn.text = "X";

                row.Add(label);
                row.Add(removeBtn);
                _workerList.Add(row);
            }

            // Hide assign button if workplace is full
            bool isFull = workers.Count >= _currentWorkplace.MaxWorkers;
            _assignButton.style.display = isFull ? DisplayStyle.None : DisplayStyle.Flex;
        }

        private void OnAssignClicked()
        {
            _pickerVisible = !_pickerVisible;
            if (_pickerVisible) { Show(_pickerPanel); RefreshPicker(); }
            else { Hide(_pickerPanel); }
        }

        private void RefreshPicker()
        {
            _pickerPanel.Clear();
            var unemployed = _assignmentService.GetUnemployedWorkers();
            foreach (var worker in unemployed)
            {
                var btn = new Button(() =>
                {
                    _assignmentService.AssignWorker(worker, _currentWorkplace);
                    _pickerVisible = false;
                    Hide(_pickerPanel);
                    RefreshWorkerList();
                });
                btn.AddToClassList("bhr-picker-button");
                btn.text = worker.GetComponent<Character>()?.FirstName;
                _pickerPanel.Add(btn);
            }
            if (!unemployed.Any())
            {
                _pickerPanel.Add(new Label("No unemployed beavers"));
            }
        }

        private void Show(VisualElement el)
        {
            if (el != null) el.style.display = DisplayStyle.Flex;
        }
        private void Hide(VisualElement el)
        {
            if (el != null) el.style.display = DisplayStyle.None;
        }
    }
}
