/*
 * Copyright (c) 2025 ADBC Drivers Contributors
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AdbcDrivers.HiveServer2.Hive2
{
    /// <summary>
    /// Provides metadata retrieval for building GetObjects results.
    /// Abstracts the data-fetching protocol (Thrift or REST) from the
    /// Arrow result structure construction in <see cref="GetObjectsResultBuilder"/>.
    /// Each protocol implements this interface to supply catalog, schema,
    /// table, and column metadata from its respective backend.
    /// </summary>
    internal interface IGetObjectsDataProvider
    {
        Task<IReadOnlyList<string>> GetCatalogsAsync(string? catalogPattern, CancellationToken cancellationToken);

        Task<IReadOnlyList<(string catalog, string schema)>> GetSchemasAsync(string? catalogPattern, string? schemaPattern, CancellationToken cancellationToken);

        Task<IReadOnlyList<(string catalog, string schema, string table, string tableType)>> GetTablesAsync(
            string? catalogPattern, string? schemaPattern, string? tableNamePattern, IReadOnlyList<string>? tableTypes, CancellationToken cancellationToken);

        /// <summary>
        /// Populates column metadata into existing TableInfo entries in the catalog map.
        /// Implementers should look up matching entries by catalog, schema, and table name,
        /// then add column information (name, type, nullability, etc.) to the TableInfo lists.
        /// For Thrift, column metadata comes from server-provided values.
        /// For SEA, column metadata is computed from type name strings.
        /// </summary>
        /// <param name="catalogMap">Pre-populated map of catalog -> schema -> table -> TableInfo.
        /// Entries are created by prior GetCatalogsAsync/GetSchemasAsync/GetTablesAsync calls.</param>
        Task PopulateColumnInfoAsync(string? catalogPattern, string? schemaPattern,
            string? tablePattern, string? columnPattern,
            Dictionary<string, Dictionary<string, Dictionary<string, TableInfo>>> catalogMap,
            CancellationToken cancellationToken);
    }
}
