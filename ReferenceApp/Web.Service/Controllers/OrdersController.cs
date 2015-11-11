// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Web.Service.Controllers
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using System.Web.Http;
    using Common;
    using CustomerOrder.Domain;
    using Microsoft.ServiceFabric.Actors;

    public class OrdersController : ApiController
    {
        private const string CustomerOrderServiceName = "CustomerOrderActorService";

        /// <summary>
        /// POST api/orders
        /// Based on a shopping cart passed into the method, PostCheckout creates an actor proxy object to activate an actor instance of CustomerOrderActor,
        /// which is used to represent the state of the customer order. Through the actor proxy, the FulfillOrderAsync method is invoked on the actor
        /// to fulfill the customer order by removing items from inventory.  
        ///        
        /// In the current version of this example, the shopping cart which a customer uses to check out exists
        /// entirely on the client side. A shopping cart, in this example, is therefore stateless and its state is
        /// only committed to memory when a customer order is created and an Actor Id is associated with it. 
        /// 
        /// Because the status of an order is changed inside the FulfillOrderAsync method, the OrdersController relies
        /// on a separate GetStatus method that the client can call to see if the Status of the order has changed to completed. 
        /// There is currently no event notification to the WebUI frontend if an order completes. 
        /// 
        /// </summary>
        /// <param name="cart"></param>
        /// <returns>Guid to identify the order and allow for status look-up later.</returns>
        [HttpPost]
        [Route("api/orders")]
        public async Task<Guid> PostCheckout(List<CustomerOrderItem> cart)
        {
            ServiceEventSource.Current.Message("Now printing cart for POSTCHECKOUT...");
            foreach (CustomerOrderItem item in cart)
            {
                ServiceEventSource.Current.Message("Guid {0}, quantity {1}", item.ItemId.ToString(), item.Quantity.ToString());
            }

            Guid orderId = Guid.NewGuid();
            ServiceUriBuilder builder = new ServiceUriBuilder(CustomerOrderServiceName);

            //We create a unique Guid that is associated with a customer order, as well as with the actor that represents that order's state.
            ICustomerOrderActor customerOrder = ActorProxy.Create<ICustomerOrderActor>(new ActorId(orderId), builder.ToUri());

            try
            {
                await customerOrder.SubmitOrderAsync(cart);
                ServiceEventSource.Current.Message("Customer order submitted successfully. ActorOrderID: {0} created", orderId);
            }
            catch (InvalidOperationException ex)
            {
                ServiceEventSource.Current.Message("Web Service: Actor rejected {0}: {1}", customerOrder, ex);
                throw;
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Web Service: Exception {0}: {1}", customerOrder, ex);
                throw;
            }

            return orderId;
        }

        /// <summary>
        /// Looks up a customer order based on its Guid identifier and by using an ActorProxy, retrieves the order's status and returns it to the client. 
        /// </summary>
        /// <param name="customerOrderId"></param>
        /// <returns>String</returns>
        [HttpGet]
        [Route("api/orders/{customerOrderId}")]
        public Task<string> GetOrderStatus(Guid customerOrderId)
        {
            ServiceUriBuilder builder = new ServiceUriBuilder(CustomerOrderServiceName);
            ICustomerOrderActor customerOrder = ActorProxy.Create<ICustomerOrderActor>(new ActorId(customerOrderId), builder.ToUri());

            try
            {
                return customerOrder.GetStatusAsync();
            }
            catch (Exception ex)
            {
                ServiceEventSource.Current.Message("Web Service: Exception {0}: {1}", customerOrder, ex);

                throw;
            }
        }
    }
}