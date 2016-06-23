using Microsoft.ServiceFabric.Actors.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Mocks
{
    public abstract class MockableActor : Actor
    {
        private IActorStateManager stateManager;

        public new IActorStateManager StateManager
        {
            get
            {
                return this.stateManager ?? base.StateManager;
            }

            set
            {
                this.stateManager = value;
            }
        }
    }
}
