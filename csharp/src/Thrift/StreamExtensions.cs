/*
 * Copyright (c) 2025 ADBC Drivers Contributors
 *
 * This file has been modified from its original version, which is
 * under the Apache License:
 *
 * Licensed to the Apache Software Foundation (ASF) under one or more
 * contributor license agreements.  See the NOTICE file distributed with
 * this work for additional information regarding copyright ownership.
 * The ASF licenses this file to You under the Apache License, Version 2.0
 * (the "License"); you may not use this file except in compliance with
 * the License.  You may obtain a copy of the License at
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
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Thrift.Transport;

namespace AdbcDrivers.HiveServer2.Thrift
{
    internal static class StreamExtensions
    {
        public static TValue? GetValueOrDefault<TKey, TValue>(this IReadOnlyDictionary<TKey, TValue> dictionary, TKey key, TValue? defaultValue = default)
        {
            if (dictionary == null) throw new ArgumentNullException(nameof(dictionary));

            return dictionary.TryGetValue(key, out TValue? value) ? value : defaultValue;
        }

        public static async Task<bool> ReadExactlyAsync(this TTransport transport, Memory<byte> memory, CancellationToken cancellationToken = default)
        {
            if (transport == null) throw new ArgumentNullException(nameof(transport));

            // Try to get the underlying array from the Memory<byte>
            if (!MemoryMarshal.TryGetArray(memory, out ArraySegment<byte> arraySegment))
            {
                throw new InvalidOperationException("The provided Memory<byte> does not have an accessible underlying array.");
            }

            int totalBytesRead = 0;
            int count = memory.Length;

            while (totalBytesRead < count)
            {
                int bytesRead = await transport.ReadAsync(arraySegment.Array!, arraySegment.Offset + totalBytesRead, count - totalBytesRead, cancellationToken).ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    // End of the stream reached before reading the desired amount
                    return totalBytesRead == 0;
                }

                totalBytesRead += bytesRead;
            }

            return true;
        }
    }
}
