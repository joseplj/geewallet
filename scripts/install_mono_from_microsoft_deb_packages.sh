#!/usr/bin/env bash
set -e

# required by apt-key
apt install -y gnupg2
# required by apt-update when pulling from mono-project.com
apt install -y ca-certificates

# taken from http://www.mono-project.com/download/stable/#download-lin
apt-key adv --keyserver hkp://keyserver.ubuntu.com:80 --recv-keys 3FA7E0328081BFF6A14DA29AA6A19B38D3D831EF
echo "deb https://download.mono-project.com/repo/ubuntu stable-bionic main" | tee /etc/apt/sources.list.d/mono-official-stable.list
apt update
apt install -y mono-devel fsharp

mono --version
msbuild -version

find /usr | grep NuGet | grep Packaging
apt install -y mono-utils
monodis --assembly /usr/lib/mono/msbuild/15.0/bin/NuGet.Packaging.Core.dll | grep Version
monodis --assembly /usr/lib/mono/msbuild/15.0/bin/Sdks/Microsoft.NET.Sdk/tools/net46/NuGet.Packaging.Core.dll | grep Version
monodis --assembly /usr/lib/mono/msbuild/15.0/bin/Sdks/Microsoft.NET.Sdk/tools/net46/NuGet.Packaging.dll | grep Version
monodis --assembly /usr/lib/mono/msbuild/15.0/bin/NuGet.Packaging.dll | grep Version
