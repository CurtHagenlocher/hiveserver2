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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Apache.Arrow.Adbc;

namespace AdbcDrivers.HiveServer2.Hive2
{
    /// <summary>
    /// Shared <see cref="AdbcDatabase"/> implementation for the Hive, Spark and Impala
    /// flavors, which differ only in how they construct a connection.
    /// </summary>
    internal abstract class HiveServer2DatabaseBase : AdbcDatabase
    {
        private readonly IReadOnlyDictionary<string, string> _properties;

        protected HiveServer2DatabaseBase(IReadOnlyDictionary<string, string> properties)
        {
            _properties = properties;
        }

        /// <summary>Creates an unopened connection for this flavor.</summary>
        protected abstract HiveServer2Connection NewConnection(IReadOnlyDictionary<string, string> properties);

        // Connect blocks on ConnectAsync (the reverse of the AdbcDatabase default) so that
        // opening the transport and session runs asynchronously end-to-end for async callers.
        public override AdbcConnection Connect(IReadOnlyDictionary<string, string>? options) =>
            ConnectAsync(options, CancellationToken.None).ConfigureAwait(false).GetAwaiter().GetResult();

        public override async Task<AdbcConnection> ConnectAsync(IReadOnlyDictionary<string, string>? options, CancellationToken cancellationToken = default)
        {
            HiveServer2Connection connection = NewConnection(MergeProperties(options));
            try
            {
                await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
            }
            catch
            {
                // Don't leak a half-open transport when the open fails or is cancelled.
                try
                {
                    connection.Dispose();
                }
                catch
                {
                    // Preserve the original failure.
                }
                throw;
            }
            return connection;
        }

        private IReadOnlyDictionary<string, string> MergeProperties(IReadOnlyDictionary<string, string>? options)
        {
            // Connection options take precedence over database properties for the same option.
            return options == null
                ? _properties
                : options
                    .Concat(_properties.Where(x => !options.Keys.Contains(x.Key, StringComparer.OrdinalIgnoreCase)))
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }
    }
}
