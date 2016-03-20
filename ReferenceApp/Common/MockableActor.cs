// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    using Microsoft.ServiceFabric.Actors.Runtime;
    using System;
    using System.Threading.Tasks;

    public class MockableActor : Actor
    {
        protected new Task<IActorReminder> RegisterReminderAsync(
            string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            return base.RegisterReminderAsync(reminderName, state, dueTime, period);
        }

        protected new Task UnregisterReminderAsync(IActorReminder reminder)
        {
            return base.UnregisterReminderAsync(reminder);
        }

        internal Task<IActorReminder> RegisterReminderAccessor(
            string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period)
        {
            return base.RegisterReminderAsync(reminderName, state, dueTime, period);
        }

        internal Task UnregisterReminderAccessor(IActorReminder reminder)
        {
            return base.UnregisterReminderAsync(reminder);
        }
    }
}