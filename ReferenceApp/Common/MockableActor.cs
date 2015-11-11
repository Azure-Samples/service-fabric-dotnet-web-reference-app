// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Common
{
    using System;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors;

    public class MockableActor<T> : StatefulActor<T> where T : class
    {
        protected new Task<IActorReminder> RegisterReminderAsync(
            string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period, ActorReminderAttributes attribute)
        {
            return base.RegisterReminderAsync(reminderName, state, dueTime, period, attribute);
        }

        protected new Task UnregisterReminderAsync(IActorReminder reminder)
        {
            return base.UnregisterReminderAsync(reminder);
        }

        internal Task<IActorReminder> RegisterReminderAccessor(
            string reminderName, byte[] state, TimeSpan dueTime, TimeSpan period, ActorReminderAttributes attribute)
        {
            return base.RegisterReminderAsync(reminderName, state, dueTime, period, attribute);
        }

        internal Task UnregisterReminderAccessor(IActorReminder reminder)
        {
            return base.UnregisterReminderAsync(reminder);
        }
    }
}