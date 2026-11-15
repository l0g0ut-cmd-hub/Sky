#!/bin/sh
# Быстрые тесты логики (баллистика, упреждение): dotnet-sdk-8.0, без Unity.
set -e
cd "$(dirname "$0")"
dotnet run -nologo -v q -c Release --project LogicTests.csproj
