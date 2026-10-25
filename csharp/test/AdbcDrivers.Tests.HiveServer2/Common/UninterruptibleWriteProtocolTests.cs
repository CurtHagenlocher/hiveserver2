/*
 * Copyright (c) 2026 ADBC Drivers Contributors
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *    http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

using System;
using System.Linq;
using System.Reflection;
using AdbcDrivers.HiveServer2.Thrift;
using Thrift.Protocol;
using Xunit;

namespace AdbcDrivers.Tests.HiveServer2.Common
{
    public class UninterruptibleWriteProtocolTests
    {
        [Fact]
        public void OverridesEveryProtocolWriteMethod()
        {
            foreach (MethodInfo method in typeof(TProtocol).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!method.Name.StartsWith("Write", StringComparison.Ordinal) ||
                    !method.Name.EndsWith("Async", StringComparison.Ordinal))
                {
                    continue;
                }

                Type[] parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();
                MethodInfo? implementation = typeof(UninterruptibleWriteProtocol).GetMethod(method.Name, parameters);
                Assert.True(implementation?.DeclaringType == typeof(UninterruptibleWriteProtocol),
                    $"{method.Name} must suppress cancellation, including after a Thrift upgrade.");
            }
        }
    }
}
