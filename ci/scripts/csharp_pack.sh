#!/usr/bin/env bash
#
# Copyright (c) 2026 ADBC Drivers Contributors
#
# Licensed under the Apache License, Version 2.0 (the "License");
# you may not use this file except in compliance with the License.
# You may obtain a copy of the License at
#
#         http://www.apache.org/licenses/LICENSE-2.0
#
# Unless required by applicable law or agreed to in writing, software
# distributed under the License is distributed on an "AS IS" BASIS,
# WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
# See the License for the specific language governing permissions and
# limitations under the License.

# Packs the managed HiveServer2 driver (and its symbol package) for nuget.org.
#
# Only AdbcDrivers.HiveServer2 is packed. AdbcDrivers.HiveServer2.Native is
# deliberately excluded: it ships as a NativeAOT shared library through the
# GitHub Release tarballs, and its own packaging path is opt-in behind
# -p:IsPackagingPipeline=true (see csharp_aot_pack.sh).
#
# Usage: csharp_pack.sh <workspace> <out-dir> [version]
#   <version>  release version without the leading `v`, e.g. 0.24.0. When
#              omitted, the version from Directory.Build.props is used
#              (0.X.Y-SNAPSHOT), which is what local/dry runs want.

set -ex

workspace=${1}
out_dir=${2}
version=${3:-}

project=${workspace}/csharp/src/AdbcDrivers.HiveServer2/AdbcDrivers.HiveServer2.csproj

version_args=()
if [ -n "${version}" ]; then
  # -p:Version wins over the VersionPrefix/VersionSuffix pair in
  # Directory.Build.props, so a tagged build drops the SNAPSHOT suffix without
  # the working tree having to be edited first.
  version_args+=("-p:Version=${version}")
fi

dotnet pack "${project}" -c Release -o "${out_dir}" "${version_args[@]}"
