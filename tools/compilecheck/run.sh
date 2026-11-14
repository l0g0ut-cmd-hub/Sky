#!/bin/sh
# Проверка компиляции без Unity: нужен dotnet-sdk-8.0. Скрипты Editor/ не проверяются (нет сборок UnityEditor).
set -e
cd "$(dirname "$0")"
[ -d .unity-refs ] || GIT_LFS_SKIP_SMUDGE=1 git clone -q --depth 1 https://github.com/rmifka/unity-ref-sdk .unity-refs
dotnet build -nologo -v q CompileCheck.csproj 2>&1 | grep -E " error |Error\(s\)" | sed 's| \[/.*||' | sort -u
