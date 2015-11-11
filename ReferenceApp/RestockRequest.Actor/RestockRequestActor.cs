// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace RestockRequest.Actor
{
    using System;
    using System.Fabric;
    using System.Threading.Tasks;
    using Microsoft.ServiceFabric.Actors;
    using RestockRequest.Domain;

    internal class RestockRequestActor : StatefulActor<RestockRequestActorState>, IRestockRequestActor, IRemindable
    {
        // The duration the verification at beginning of each pipeline step takes
        private static TimeSpan PipelineStageVerificationDelay = TimeSpan.FromSeconds(5);
        // The duration each step of the pipeline takes
        private static TimeSpan PipelineStageProcessingDuration = TimeSpan.FromSeconds(10);

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
        public Task AddRestockRequestAsync(RestockRequest request)
        {
            if (this.State.IsStarted()) //Don't accept a request that is already started
            {
                ActorEventSource.Current.Message(string.Format("RestockRequestActor: {0}: Can't accept restock request in this state", this.State));
                throw new InvalidOperationException(string.Format("{0}: Can't accept restock request in this state", this.State));
            }

            // Accept the request
            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: Accept update quantity request {0}", request);

            this.State.Status = RestockRequestStatus.Accepted;
            this.State.Request = request;

            // Start a reminder to go through the processing pipeline.
            // A reminder keeps the actor from being garbage collected due to lack of use, 
            // which works better than a timer in this case.
            return this.RegisterReminderAsync(
                RestockRequestReminderNames.RestockPipelineChangeReminderName,
                null,
                PipelineStageVerificationDelay,
                PipelineStageProcessingDuration,
                ActorReminderAttributes.None);
        }

        protected override Task OnActivateAsync()
        {
            if (this.State == null)
            {
                this.State = new RestockRequestActorState();
            }

            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: State initialized to {0}", this.State);

            return Task.FromResult(true);
        }

        /// <summary>
        /// Simulates the processing of a restock request by advancing the processing status each time this method is invoked
        /// until it reaches the complete stage.
        /// </summary>
        /// <returns></returns>
        internal Task RestockPipeline()
        {
            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: {0}: Pipeline change reminder", this.State);

            switch (this.State.Status)
            {
                case RestockRequestStatus.Accepted:

                    // Change to next step and let it "execute" until the reminder fires again
                    this.State.Status = RestockRequestStatus.Manufacturing;
                    return Task.FromResult(true);

                case RestockRequestStatus.Manufacturing:

                    // Changet the step to completed to indicate the "processing" is complete.
                    this.State.Status = RestockRequestStatus.Completed;

                    // Raise the event to let interested parties (RestockRequestManager) know that the restock is complete
                    this.SignalRequestStatusChange();

                    // Done, so unregister the reminder
                    return this.UnregisterRestockPipelineChangeReminderAsync();

                default:
                    throw new InvalidOperationException(string.Format("{0}: remainder received in invalid status", this.State));
            }
        }

        private void SignalRequestStatusChange()
        {
            ActorEventSource.Current.ActorMessage(this, "RestockRequestActor: {0}: Raise event for state change", this.State);

            IRestockRequestEvents events = this.GetEvent<IRestockRequestEvents>();
            events.RestockRequestCompleted(this.Id, this.State.Request);
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