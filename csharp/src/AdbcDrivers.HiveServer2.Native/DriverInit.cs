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

using System.Runtime.InteropServices;
using AdbcDrivers.HiveServer2.Hive2;
using AdbcDrivers.HiveServer2.Impala;
using AdbcDrivers.HiveServer2.Spark;
using Apache.Arrow.Adbc;
using Apache.Arrow.Adbc.C;

namespace AdbcDrivers.HiveServer2.Native
{
    /// <summary>
    /// Driver-specific native entry points exposed by this AOT-published shared library.
    ///
    /// <para>Callers must select the entry point for their server via
    /// <c>entrypoint=</c>. Each entry point delegates to
    /// <see cref="CAdbcDriverExporter.AdbcDriverInit"/> with a fresh instance
    /// of the corresponding managed driver.</para>
    ///
    /// <para>One driver instance is created per load; the
    /// exporter takes ownership via a GCHandle stored on the CAdbcDriver and
    /// disposes it when the driver is released.</para>
    /// </summary>
    public static unsafe class DriverInit
    {
        [UnmanagedCallersOnly(EntryPoint = "AdbcDriverHiveServer2Init")]
        public static AdbcStatusCode AdbcDriverHiveServer2Init(int version, CAdbcDriver* driver, CAdbcError* error)
        {
            return CAdbcDriverExporter.AdbcDriverInit(version, driver, error, new HiveServer2Driver());
        }

        [UnmanagedCallersOnly(EntryPoint = "AdbcDriverImpalaInit")]
        public static AdbcStatusCode AdbcDriverImpalaInit(int version, CAdbcDriver* driver, CAdbcError* error)
        {
            return CAdbcDriverExporter.AdbcDriverInit(version, driver, error, new ImpalaDriver());
        }

        [UnmanagedCallersOnly(EntryPoint = "AdbcDriverSparkInit")]
        public static AdbcStatusCode AdbcDriverSparkInit(int version, CAdbcDriver* driver, CAdbcError* error)
        {
            return CAdbcDriverExporter.AdbcDriverInit(version, driver, error, new SparkDriver());
        }
    }
}
