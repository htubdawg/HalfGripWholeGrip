#!/bin/bash
# Compiles the mod against the game, BepInEx and HalfGripTools without starting the game, the way HalfGripTools' check-mods.ps1 does
# (C# 10, netstandard2.1, warnings as errors), on Linux, then runs tests/ (the hand input and assignment logic as it ships, and a scan of
# the game assembly for the members the patches name). Nothing is written to the repo.
#   tools/check.sh <game folder> <HalfGripTools.dll>
# The game folder can be the HalfGripRefs repo's game/ folder. HalfGripTools.dll comes from building HalfGripTools:
#   dotnet build HalfGripTools/HalfGripTools.csproj -c Release -p:GameDir=<game folder>
# which puts it in HalfGripTools/dist/BepInEx/plugins/HalfGripTools/.
set -euo pipefail
game="$(realpath "${1:?game folder}")"
tools="$(realpath "${2:?HalfGripTools.dll}")"
repo="$(cd "$(dirname "$0")/.." && pwd)"
work="$(mktemp -d)"
trap 'rm -rf "$work"' EXIT
managed="$game/HalfGrip_Data/Managed"
cat > "$work/check.csproj" <<XML
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <LangVersion>10.0</LangVersion>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <ImplicitUsings>disable</ImplicitUsings>
    <Nullable>disable</Nullable>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <AssemblyName>HalfGripMod.WholeGrip</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="$repo/WholeGrip/src/**/*.cs" />
    <Reference Include="$managed/*.dll" Exclude="$managed/mscorlib.dll;$managed/netstandard.dll;$managed/System*.dll" />
    <Reference Include="$game/BepInEx/core/BepInEx.dll" />
    <Reference Include="$game/BepInEx/core/0Harmony.dll" />
    <Reference Include="$tools" />
  </ItemGroup>
</Project>
XML
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet build "$work/check.csproj" -c Release -v q --nologo -p:OutDir="$work/out/" -warnaserror
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 dotnet run --project "$repo/tests/Checks.csproj" -c Release -- "$managed/Assembly-CSharp.dll"
