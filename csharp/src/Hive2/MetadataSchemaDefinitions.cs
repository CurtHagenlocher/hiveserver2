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

namespace AdbcDrivers.HiveServer2.Hive2
{
    internal static class MetadataColumnNames
    {
        internal const string ColumnDef = "COLUMN_DEF";
        internal const string ColumnName = "COLUMN_NAME";
        internal const string DataType = "DATA_TYPE";
        internal const string IsAutoIncrement = "IS_AUTO_INCREMENT";
        internal const string IsNullable = "IS_NULLABLE";
        internal const string OrdinalPosition = "ORDINAL_POSITION";
        internal const string TableCat = "TABLE_CAT";
        internal const string TableCatalog = "TABLE_CATALOG";
        internal const string TableName = "TABLE_NAME";
        internal const string TableSchem = "TABLE_SCHEM";
        internal const string TableMd = "TABLE_MD";
        internal const string TableType = "TABLE_TYPE";
        internal const string TypeName = "TYPE_NAME";
        internal const string Nullable = "NULLABLE";
        internal const string ColumnSize = "COLUMN_SIZE";
        internal const string DecimalDigits = "DECIMAL_DIGITS";
        internal const string BufferLength = "BUFFER_LENGTH";
        internal const string BaseTypeName = "BASE_TYPE_NAME";

        internal const string PrimaryKeyPrefix = "PK_";
        internal const string ForeignKeyPrefix = "FK_";

        internal static readonly string[] PrimaryKeyFields = new[] { "COLUMN_NAME" };
        internal static readonly string[] ForeignKeyFields = new[] { "PKCOLUMN_NAME", "PKTABLE_CAT", "PKTABLE_SCHEM", "PKTABLE_NAME", "FKCOLUMN_NAME", "FK_NAME", "KEQ_SEQ" };
    }
}
