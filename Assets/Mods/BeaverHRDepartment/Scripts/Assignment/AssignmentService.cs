using System.Collections.Generic;
using System.Linq;
using Timberborn.EntitySystem;
using Timberborn.WorkSystem;

namespace Mods.BeaverHRDepartment.Assignment
{

    public class AssignmentService
    {

        private readonly EntityRegistry _entityRegistry;

        public AssignmentService(EntityRegistry entityRegistry)
        {
            _entityRegistry = entityRegistry;
        }

        public void AssignWorker(Worker worker, Workplace workplace)
        {
            if (worker == null || workplace == null) return;
            if (worker.Workplace != null) worker.Unemploy();
            try
            {
                workplace.IncreaseDesiredWorkers();
                worker.EmployAt(workplace);
            }
            catch (System.Exception e)
            {
                workplace.DecreaseDesiredWorkers();
            }
        }

        public void UnassignWorker(Worker worker, Workplace workplace)
        {
            if (worker == null || workplace == null) return;
            worker.Unemploy();
            if (workplace.DesiredWorkers > 1) workplace.DecreaseDesiredWorkers();
        }

        public void SwapWorkers(Worker workerA, Worker workerB)
        {
            if (workerA == null || workerB == null) return;
            var workplaceWorkerB = workerB.Workplace;
            workerB.Unemploy();
            workerA.EmployAt(workplaceWorkerB);
        }


        public IReadOnlyList<Worker> GetUnemployedWorkers()
        {
            return _entityRegistry.Entities
              .Select(e => e.GetComponent<Worker>())
              .Where(w => w != null && w.Workplace == null)
              .ToList();
        }
    }
}
