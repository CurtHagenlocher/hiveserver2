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

# Turns the per-platform NativeAOT libraries built by csharp_release.yaml into
# the ADBC Driver Foundry release assets:
#
#   hiveserver2_<platform>_<arch>_v<version>.tar.gz   (driver + MANIFEST + LICENSE + NOTICE)
#   manifest.yaml                                     (index consumed by `dbc install`)
#   checksums.txt                                     (SHA256 of the above)
#
# The heavy lifting is `adbc-gen-package` from adbc-drivers/dev, which is what
# the Go and Rust Foundry drivers use, so the output layout matches theirs. It
# derives the driver name by splitting the shared library filename on `_`, which
# is why AdbcDrivers.HiveServer2.Native sets AssemblyName=adbc_driver_hiveserver2,
# and it takes each package's platform/arch from its input directory name, which
# is why the workflow names those artifacts drivers-<platform>-<arch>.
#
# Usage: csharp_release_package.sh <workspace> <drivers-dir> <work-dir> <assets-dir> <is-release>
#   <drivers-dir>  downloaded build artifacts, one dir per platform:
#                  <drivers-dir>/drivers-<platform>-<arch>/<lib>
#   <work-dir>     raw adbc-gen-package output (nested <name>/<version>/)
#   <assets-dir>   flat directory of files to attach to the GitHub Release
#   <is-release>   "true" for a tagged build; anything else runs non-strict so
#                  a dry run without a csharp/v* tag doesn't fail

set -ex

workspace=${1}
drivers_dir=${2}
work_dir=${3}
assets_dir=${4}
is_release=${5:-false}

csharp_dir=${workspace}/csharp

# --release makes version detection strict: it fails rather than falling back
# to v0.0.1-dev when no csharp/v* tag is reachable. That is what we want when
# actually releasing, and exactly what we don't want on a workflow_dispatch
# dry run in a repo that may not have any release tag yet.
release_args=()
if [ "${is_release}" = "true" ]; then
  release_args+=("--release")
fi

# --root is the directory whose path becomes the git tag prefix used to derive
# the version: csharp/ -> tags matching `csharp/v*` -> version `v0.24.0`.
pushd "${csharp_dir}"
pixi run adbc-gen-package \
  --name hiveserver2 \
  --root "${csharp_dir}" \
  --manifest-template "${csharp_dir}/manifest.toml" \
  "${release_args[@]}" \
  -o "${work_dir}" \
  "${drivers_dir}"/drivers-*-*/
popd

# adbc-gen-package writes tarballs under <work-dir>/<name>/<version>/ and
# manifest.yaml at <work-dir>/. Flatten both into one directory, since GitHub
# Release assets are a flat namespace keyed on basename.
mkdir -p "${assets_dir}"
find "${work_dir}" -type f \( -name '*.tar.gz' -o -name 'manifest.yaml' \) \
  -exec cp {} "${assets_dir}/" \;

# Not part of the Foundry asset set — the other drivers rely on `dbc` to verify
# downloads — but cheap, and useful to anyone fetching a tarball by hand.
# `--` rather than `./*.tar.gz`: it guards against a leading-dash filename
# without writing a `./` prefix into the file, so the names in checksums.txt
# match the flat names the assets are published under.
(cd "${assets_dir}" && sha256sum -- *.tar.gz manifest.yaml > checksums.txt)

ls -l "${assets_dir}"
cat "${assets_dir}/checksums.txt"
