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

# Emits the LICENSE file embedded in each release tarball, on stdout.
#
# adbc-gen-package (adbc-drivers/dev) looks for this script at
# <manifest dir>/ci/scripts/generate_license.sh and runs it with the csharp/
# directory as the working directory. It is the C# equivalent of the
# go-licenses and cargo-about paths that tool has built in for the Go and Rust
# drivers, neither of which applies here.
#
# This matters because the driver ships as a NativeAOT shared library: its
# managed dependencies are statically linked into the binary rather than
# resolved at runtime, so their licenses have to travel with it.

set -euo pipefail

project=src/AdbcDrivers.HiveServer2.Native/AdbcDrivers.HiveServer2.Native.csproj

# Everything on stdout is the license file, so send build chatter to stderr.
dotnet restore "${project}" >&2

PACKAGE_JSON=$(dotnet list "${project}" package --include-transitive --format json)
export PACKAGE_JSON
GLOBAL_PACKAGES=$(dotnet nuget locals global-packages --list | sed 's/^[a-z-]*: *//')
export GLOBAL_PACKAGES

python3 - <<'PY'
import json
import os
import re
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

# `dotnet list package` emits a UTF-8 BOM on some platforms; utf-8-sig strips it.
report = json.loads(os.environ["PACKAGE_JSON"].encode("utf-8").decode("utf-8-sig"))
global_packages = Path(os.environ["GLOBAL_PACKAGES"].strip())

packages: dict[tuple[str, str], None] = {}
for project in report["projects"]:
    for framework in project.get("frameworks") or []:
        for kind in ("topLevelPackages", "transitivePackages"):
            for package in framework.get(kind) or []:
                # Skip the NativeAOT toolchain itself: ILCompiler and the
                # trimmer run at build time and contribute no code to the
                # shipped library.
                if package.get("autoReferenced") == "true":
                    continue
                version = package.get("resolvedVersion") or package.get(
                    "requestedVersion"
                )
                packages[(package["id"], version)] = None


def describe(package_id: str, version: str) -> str:
    """Best-effort license summary read from the package's own .nuspec."""
    nuspec = (
        global_packages
        / package_id.lower()
        / version.lower()
        / f"{package_id.lower()}.nuspec"
    )
    if not nuspec.is_file():
        return "license metadata unavailable"

    tree = ET.parse(nuspec)
    # The nuspec schema is namespaced, and the namespace URI varies by the
    # version of NuGet that produced the package, so match on local names.
    def find(tag: str) -> str | None:
        for element in tree.iter():
            if re.sub(r"^\{.*\}", "", element.tag) == tag and element.text:
                return element.text.strip()
        return None

    license_text = find("license")
    if license_text:
        return license_text
    license_url = find("licenseUrl")
    if license_url:
        return license_url
    return "license not declared"


print(
    Path("../LICENSE.txt").read_text(encoding="utf-8")
    if Path("../LICENSE.txt").is_file()
    else Path("LICENSE.txt").read_text(encoding="utf-8"),
    end="",
)

print()
print("=" * 79)
print("THIRD-PARTY COMPONENTS")
print("=" * 79)
print()
print(
    "This driver is compiled ahead-of-time into a single shared library, which\n"
    "statically links the following components. Each remains under its own\n"
    "license."
)
print()

if not packages:
    print("(none detected)", file=sys.stderr)
    sys.exit("No packages detected; refusing to emit an empty license listing")

for package_id, version in sorted(packages):
    print(f"  {package_id} {version} -- {describe(package_id, version)}")
PY
