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
            return (ConditionalResult<T>) Activator.CreateInstance(
                typeof(ConditionalResult<T>),
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new object[] {result, value},
                null);
        }
    }
}