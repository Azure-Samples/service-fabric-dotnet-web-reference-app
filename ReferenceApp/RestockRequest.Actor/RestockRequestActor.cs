// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Actor
{
    using Microsoft.ServiceFabric.Actors.Runtime;
    using RestockRequest.Domain;
    using System;
    using System.Fabric;
    using System.Threading.Tasks;

    //internal class RestockRequestActor : StatefulActor<RestockRequestActorState>, IRestockRequestActor, IRemindable
    internal class RestockRequestActor : Actor, IRestockRequestActor, IRemindable
    {
        // The duration the verification at beginning of each pipeline step takes
        private static TimeSpan PipelineStageVerificationDelay = TimeSpan.FromSeconds(5);
        // The duration each step of the pipeline takes
        private static TimeSpan PipelineStageProcessingDuration = TimeSpan.FromSeconds(10);

        private static string ActorStatePropertyName = "RestockRequestActorStatePropertyName";

        public Task ReceiveReminderAsync(string reminderName, byte[] context, TimeSpan dueTime, TimeSpan period)
        {
            switch (reminderName)
            {
                case RestockRequestReminderNames.RestockPipelineChangeReminderName:
                    return this.RestockPipeline();

                default:
                    // We should never arrive here normally. The system won't call reminders that don't exist. 
                    // But for our own sake in case we add a new reminder somewhere and forget to handle it, this will remind us.
                    throw new InvalidOperationException("Unknown reminder: " + reminderName);
            }
        }

        /// <summary>
        /// Accepts a restock request and changes the Actor's state accordingly. The request is processed
        /// async and the caller will be notified when the processing is done. 
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public async Task AddRestockRequestAsync(RestockRequest request)
        {
            var state = await this.StateManager.GetStateAsync<RestockRequestActorState>(ActorStatePropertyName);

            if (state.IsStarted()) //Don't accept a request that is already started
            {
                ActorEventSource.Current.Message(string.Format("RestockRequestActor: {0}: Can't accept restock request in this state", state));
                throw new InvalidOperationException(string.Format("{0}: Can't accept restock request in this state", state));
            }

            // Accept the request
            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: Accept update quantity request {0}", request);

            state.Status = RestockRequestStatus.Accepted;
            state.Request = request;

            await this.StateManager.SetStateAsync<RestockRequestActorState>(ActorStatePropertyName, state);

            // Start a reminder to go through the processing pipeline.
            // A reminder keeps the actor from being garbage collected due to lack of use, 
            // which works better than a timer in this case.
            await this.RegisterReminderAsync(
                RestockRequestReminderNames.RestockPipelineChangeReminderName,
                null,
                PipelineStageVerificationDelay,
                PipelineStageProcessingDuration);

            return;
        }

        protected override async Task OnActivateAsync()
        {
            var state = await this.StateManager.TryGetStateAsync<RestockRequestActorState>(ActorStatePropertyName);

            if (!state.HasValue)
            {
                await this.StateManager.SetStateAsync<RestockRequestActorState>(ActorStatePropertyName, new RestockRequestActorState());
                ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: State initialized");
            }

            return;
        }

        /// <summary>
        /// Simulates the processing of a restock request by advancing the processing status each time this method is invoked
        /// until it reaches the complete stage.
        /// </summary>
        /// <returns></returns>
        internal async Task RestockPipeline()
        {
            var state = await this.StateManager.GetStateAsync<RestockRequestActorState>(ActorStatePropertyName);

            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: {0}: Pipeline change reminder", state);

            switch (state.Status)
            {
                case RestockRequestStatus.Accepted:

                    // Change to next step and let it "execute" until the reminder fires again
                    state.Status = RestockRequestStatus.Manufacturing;
                    break;

                case RestockRequestStatus.Manufacturing:

                    // Changet the step to completed to indicate the "processing" is complete.
                    state.Status = RestockRequestStatus.Completed;

                    // Raise the event to let interested parties (RestockRequestManager) know that the restock is complete
                    this.SignalRequestStatusChange(state);

                    // Done, so unregister the reminder
                    await this.UnregisterRestockPipelineChangeReminderAsync();
                    break;

                default:
                    throw new InvalidOperationException(string.Format("{0}: remainder received in invalid status", state));

            }

            await this.StateManager.SetStateAsync<RestockRequestActorState>(ActorStatePropertyName, state);
            return;
        }

        private void SignalRequestStatusChange(RestockRequestActorState state)
        {
            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: {0}: Raise event for state change", state);

            IRestockRequestEvents events = this.GetEvent<IRestockRequestEvents>();
            events.RestockRequestCompleted(this.Id, state.Request);
        }

        private Task UnregisterRestockPipelineChangeReminderAsync()
        {
            IActorReminder reminder;
            try
            {
                reminder = this.GetReminder(RestockRequestReminderNames.RestockPipelineChangeReminderName);
            }
            catch (FabricException)
            {
                reminder = null;
            }

            return (reminder == null) ? Task.FromResult(true) : this.UnregisterReminderAsync(reminder);
        }
    }
}