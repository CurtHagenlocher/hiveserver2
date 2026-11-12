<!--
  Copyright (c) 2025 ADBC Drivers Contributors

  Licensed under the Apache License, Version 2.0 (the "License");
  you may not use this file except in compliance with the License.
  You may obtain a copy of the License at

          http://www.apache.org/licenses/LICENSE-2.0

  Unless required by applicable law or agreed to in writing, software
  distributed under the License is distributed on an "AS IS" BASIS,
  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
  See the License for the specific language governing permissions and
  limitations under the License.
-->

# ADBC Drivers for HiveServer2-based Servers

This repository contains [ADBC drivers](https://arrow.apache.org/adbc/) for
HiveServer2-based servers (Hive, Impala, and Spark), implemented in C# language.

## Installation

At this time pre-packaged drivers are not yet available. See [Building](#building) for instructions on building from source.

## Native API

The NativeAOT shared library (`adbc_driver_hiveserver2.dll`,
`adbc_driver_hiveserver2.so`, or `adbc_driver_hiveserver2.dylib`) contains all
three drivers. Select the appropriate initialization entry point when loading it:

| Server | Entry point |
| ------ | ----------- |
| HiveServer2 | `AdbcDriverHiveServer2Init` |
| Impala | `AdbcDriverImpalaInit` |
| Spark | `AdbcDriverSparkInit` |

For example, with the managed C API importer:

```csharp
using AdbcDriver driver = CAdbcDriverImporter.Load(libraryPath, "AdbcDriverImpalaInit");
```

With an ADBC driver manager, set its `entrypoint` option to the corresponding
symbol. Use the connection options for the selected managed driver.

**Breaking change:** the former `AdbcDriverInit` export has been renamed to
`AdbcDriverHiveServer2Init`. Existing native consumers must update their entry
point selection; there is no longer a generic `AdbcDriverInit` export.

## Cancellation

On standard (TCP, including TLS/SASL) connections, cancelling or timing out an
in-flight Thrift RPC closes and invalidates the connection to avoid reusing a
partially written or read stream. Create a new connection before issuing more
requests. Cancellation while waiting to start an RPC does not invalidate the connection.
HTTP connections remain reusable after cancellation.

## Building

See [CONTRIBUTING.md](CONTRIBUTING.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md).
