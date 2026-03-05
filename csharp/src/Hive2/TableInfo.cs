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

namespace AdbcDrivers.HiveServer2.Hive2
{
    internal struct TableInfo(string type)
    {
        public string Type { get; } = type;

        public List<string> ColumnName { get; } = new();

        public List<short> ColType { get; } = new();

        public List<string> BaseTypeName { get; } = new();

        public List<string> TypeName { get; } = new();

        public List<short> Nullable { get; } = new();

        public List<int?> Precision { get; } = new();

        public List<short?> Scale { get; } = new();

        public List<int> OrdinalPosition { get; } = new();

        public List<string> ColumnDefault { get; } = new();

        public List<string> IsNullable { get; } = new();

        public List<bool> IsAutoIncrement { get; } = new();
    }
}
