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
using Apache.Arrow;
using Apache.Arrow.Adbc;
using Apache.Arrow.Adbc.Extensions;
using Apache.Arrow.Types;
using static Apache.Arrow.Adbc.AdbcConnection;

namespace AdbcDrivers.HiveServer2.Hive2
{
    internal static class GetObjectsResultBuilder
    {
        internal static async Task<HiveInfoArrowStream> BuildGetObjectsResultAsync(
            IGetObjectsDataProvider provider,
            GetObjectsDepth depth,
            string? catalogPattern,
            string? schemaPattern,
            string? tableNamePattern,
            IReadOnlyList<string>? tableTypes,
            string? columnNamePattern,
            CancellationToken cancellationToken)
        {
            var catalogMap = new Dictionary<string, Dictionary<string, Dictionary<string, TableInfo>>>();

            if (depth == GetObjectsDepth.All || depth >= GetObjectsDepth.Catalogs)
            {
                foreach (string catalog in await provider.GetCatalogsAsync(catalogPattern, cancellationToken).ConfigureAwait(false))
                {
                    catalogMap[catalog] = new Dictionary<string, Dictionary<string, TableInfo>>();
                }
            }

            if (depth == GetObjectsDepth.All || depth >= GetObjectsDepth.DbSchemas)
            {
                foreach (var (catalog, schema) in await provider.GetSchemasAsync(catalogPattern, schemaPattern, cancellationToken).ConfigureAwait(false))
                {
                    if (catalogMap.TryGetValue(catalog, out var schemaMap) && !schemaMap.ContainsKey(schema))
                    {
                        schemaMap.Add(schema, new Dictionary<string, TableInfo>());
                    }
                }
            }

            if (depth == GetObjectsDepth.All || depth >= GetObjectsDepth.Tables)
            {
                foreach (var (catalog, schema, table, tableType) in await provider.GetTablesAsync(catalogPattern, schemaPattern, tableNamePattern, tableTypes, cancellationToken).ConfigureAwait(false))
                {
                    if (catalogMap.TryGetValue(catalog, out var schemaMap)
                        && schemaMap.TryGetValue(schema, out var tableMap)
                        && !tableMap.ContainsKey(table))
                    {
                        tableMap.Add(table, new TableInfo(tableType));
                    }
                }
            }

            if (depth == GetObjectsDepth.All)
            {
                await provider.PopulateColumnInfoAsync(catalogPattern, schemaPattern, tableNamePattern, columnNamePattern, catalogMap, cancellationToken).ConfigureAwait(false);
            }

            return BuildResult(depth, catalogMap);
        }

        internal static HiveInfoArrowStream BuildResult(
            GetObjectsDepth depth,
            Dictionary<string, Dictionary<string, Dictionary<string, TableInfo>>> catalogMap)
        {
            StringArray.Builder catalogNameBuilder = new StringArray.Builder();
            List<IArrowArray?> catalogDbSchemasValues = new List<IArrowArray?>();

            foreach (KeyValuePair<string, Dictionary<string, Dictionary<string, TableInfo>>> catalogEntry in catalogMap)
            {
                catalogNameBuilder.Append(catalogEntry.Key);

                if (depth == GetObjectsDepth.Catalogs)
                {
                    catalogDbSchemasValues.Add(null);
                }
                else
                {
                    catalogDbSchemasValues.Add(BuildDbSchemas(
                                depth, catalogEntry.Value));
                }
            }

            Schema schema = StandardSchemas.GetObjectsSchema;
            IReadOnlyList<IArrowArray> dataArrays = schema.Validate(
                new List<IArrowArray>
                {
                    catalogNameBuilder.Build(),
                    catalogDbSchemasValues.BuildListArrayForType(new StructType(StandardSchemas.DbSchemaSchema)),
                });

            return new HiveInfoArrowStream(schema, dataArrays);
        }

        internal static StructArray BuildDbSchemas(
            GetObjectsDepth depth,
            Dictionary<string, Dictionary<string, TableInfo>> schemaMap)
        {
            StringArray.Builder dbSchemaNameBuilder = new StringArray.Builder();
            List<IArrowArray?> dbSchemaTablesValues = new List<IArrowArray?>();
            ArrowBuffer.BitmapBuilder nullBitmapBuffer = new ArrowBuffer.BitmapBuilder();
            int length = 0;

            foreach (KeyValuePair<string, Dictionary<string, TableInfo>> schemaEntry in schemaMap)
            {
                dbSchemaNameBuilder.Append(schemaEntry.Key);
                length++;
                nullBitmapBuffer.Append(true);

                if (depth == GetObjectsDepth.DbSchemas)
                {
                    dbSchemaTablesValues.Add(null);
                }
                else
                {
                    dbSchemaTablesValues.Add(BuildTables(
                        depth, schemaEntry.Value));
                }
            }

            IReadOnlyList<Field> schema = StandardSchemas.DbSchemaSchema;
            IReadOnlyList<IArrowArray> dataArrays = schema.Validate(
                new List<IArrowArray>
                {
                    dbSchemaNameBuilder.Build(),
                    dbSchemaTablesValues.BuildListArrayForType(new StructType(StandardSchemas.TableSchema)),
                });

            return new StructArray(
                new StructType(schema),
                length,
                dataArrays,
                nullBitmapBuffer.Build());
        }

        internal static StructArray BuildTables(
            GetObjectsDepth depth,
            Dictionary<string, TableInfo> tableMap)
        {
            StringArray.Builder tableNameBuilder = new StringArray.Builder();
            StringArray.Builder tableTypeBuilder = new StringArray.Builder();
            List<IArrowArray?> tableColumnsValues = new List<IArrowArray?>();
            List<IArrowArray?> tableConstraintsValues = new List<IArrowArray?>();
            ArrowBuffer.BitmapBuilder nullBitmapBuffer = new ArrowBuffer.BitmapBuilder();
            int length = 0;

            foreach (KeyValuePair<string, TableInfo> tableEntry in tableMap)
            {
                tableNameBuilder.Append(tableEntry.Key);
                tableTypeBuilder.Append(tableEntry.Value.Type);
                nullBitmapBuffer.Append(true);
                length++;

                tableConstraintsValues.Add(null);

                if (depth == GetObjectsDepth.Tables)
                {
                    tableColumnsValues.Add(null);
                }
                else
                {
                    tableColumnsValues.Add(BuildColumns(tableEntry.Value));
                }
            }

            IReadOnlyList<Field> schema = StandardSchemas.TableSchema;
            IReadOnlyList<IArrowArray> dataArrays = schema.Validate(
                new List<IArrowArray>
                {
                    tableNameBuilder.Build(),
                    tableTypeBuilder.Build(),
                    tableColumnsValues.BuildListArrayForType(new StructType(StandardSchemas.ColumnSchema)),
                    tableConstraintsValues.BuildListArrayForType( new StructType(StandardSchemas.ConstraintSchema))
                });

            return new StructArray(
                new StructType(schema),
                length,
                dataArrays,
                nullBitmapBuffer.Build());
        }

        internal static StructArray BuildColumns(TableInfo tableInfo)
        {
            StringArray.Builder columnNameBuilder = new StringArray.Builder();
            Int32Array.Builder ordinalPositionBuilder = new Int32Array.Builder();
            StringArray.Builder remarksBuilder = new StringArray.Builder();
            Int16Array.Builder xdbcDataTypeBuilder = new Int16Array.Builder();
            StringArray.Builder xdbcTypeNameBuilder = new StringArray.Builder();
            Int32Array.Builder xdbcColumnSizeBuilder = new Int32Array.Builder();
            Int16Array.Builder xdbcDecimalDigitsBuilder = new Int16Array.Builder();
            Int16Array.Builder xdbcNumPrecRadixBuilder = new Int16Array.Builder();
            Int16Array.Builder xdbcNullableBuilder = new Int16Array.Builder();
            StringArray.Builder xdbcColumnDefBuilder = new StringArray.Builder();
            Int16Array.Builder xdbcSqlDataTypeBuilder = new Int16Array.Builder();
            Int16Array.Builder xdbcDatetimeSubBuilder = new Int16Array.Builder();
            Int32Array.Builder xdbcCharOctetLengthBuilder = new Int32Array.Builder();
            StringArray.Builder xdbcIsNullableBuilder = new StringArray.Builder();
            StringArray.Builder xdbcScopeCatalogBuilder = new StringArray.Builder();
            StringArray.Builder xdbcScopeSchemaBuilder = new StringArray.Builder();
            StringArray.Builder xdbcScopeTableBuilder = new StringArray.Builder();
            BooleanArray.Builder xdbcIsAutoincrementBuilder = new BooleanArray.Builder();
            BooleanArray.Builder xdbcIsGeneratedcolumnBuilder = new BooleanArray.Builder();
            ArrowBuffer.BitmapBuilder nullBitmapBuffer = new ArrowBuffer.BitmapBuilder();
            int length = 0;

            for (int i = 0; i < tableInfo.ColumnName.Count; i++)
            {
                columnNameBuilder.Append(tableInfo.ColumnName[i]);
                ordinalPositionBuilder.Append(tableInfo.OrdinalPosition[i]);
                // Use the "remarks" field to store the original type name value
                remarksBuilder.Append(tableInfo.TypeName[i]);
                xdbcColumnSizeBuilder.Append(tableInfo.Precision[i]);
                xdbcDecimalDigitsBuilder.Append(tableInfo.Scale[i]);
                xdbcDataTypeBuilder.Append(tableInfo.ColType[i]);
                // Just the base type name without precision or scale clause
                xdbcTypeNameBuilder.Append(tableInfo.BaseTypeName[i]);
                xdbcNumPrecRadixBuilder.AppendNull();
                xdbcNullableBuilder.Append(tableInfo.Nullable[i]);
                xdbcColumnDefBuilder.Append(tableInfo.ColumnDefault[i]);
                xdbcSqlDataTypeBuilder.Append(tableInfo.ColType[i]);
                xdbcDatetimeSubBuilder.AppendNull();
                xdbcCharOctetLengthBuilder.AppendNull();
                xdbcIsNullableBuilder.Append(tableInfo.IsNullable[i]);
                xdbcScopeCatalogBuilder.AppendNull();
                xdbcScopeSchemaBuilder.AppendNull();
                xdbcScopeTableBuilder.AppendNull();
                xdbcIsAutoincrementBuilder.Append(tableInfo.IsAutoIncrement[i]);
                xdbcIsGeneratedcolumnBuilder.Append(true);
                nullBitmapBuffer.Append(true);
                length++;
            }

            IReadOnlyList<Field> schema = StandardSchemas.ColumnSchema;
            IReadOnlyList<IArrowArray> dataArrays = schema.Validate(
                new List<IArrowArray>
                {
                    columnNameBuilder.Build(),
                    ordinalPositionBuilder.Build(),
                    remarksBuilder.Build(),
                    xdbcDataTypeBuilder.Build(),
                    xdbcTypeNameBuilder.Build(),
                    xdbcColumnSizeBuilder.Build(),
                    xdbcDecimalDigitsBuilder.Build(),
                    xdbcNumPrecRadixBuilder.Build(),
                    xdbcNullableBuilder.Build(),
                    xdbcColumnDefBuilder.Build(),
                    xdbcSqlDataTypeBuilder.Build(),
                    xdbcDatetimeSubBuilder.Build(),
                    xdbcCharOctetLengthBuilder.Build(),
                    xdbcIsNullableBuilder.Build(),
                    xdbcScopeCatalogBuilder.Build(),
                    xdbcScopeSchemaBuilder.Build(),
                    xdbcScopeTableBuilder.Build(),
                    xdbcIsAutoincrementBuilder.Build(),
                    xdbcIsGeneratedcolumnBuilder.Build()
                });

            return new StructArray(
                new StructType(schema),
                length,
                dataArrays,
                nullBitmapBuffer.Build());
        }
    }
}
