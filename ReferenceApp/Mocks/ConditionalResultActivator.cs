// ------------------------------------------------------------
//  Copyright (c) Microsoft Corporation.  All rights reserved.
//  Licensed under the MIT License (MIT). See License.txt in the repo root for license information.
// ------------------------------------------------------------

namespace Mocks
{
    using System;
    using Microsoft.ServiceFabric.Data;

    internal static class ConditionalResultActivator
    {
        public static ConditionalResult<T> Create<T>(bool result, T value)

        {
            // on "my machine" (antogh github user) the Activator for the ConditionalResult was throwing an exception saying could not find the matching contructor,
            // so I created the new object with "new" instead and it works fine. 
            return (new ConditionalResult<T>(result, value));
            // The original lines are kept commented below
            /*
            return (ConditionalResult<T>) Activator.CreateInstance(
                typeof(ConditionalResult<T>),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object[] {result, value},
                null);
                */
        }
    }
}