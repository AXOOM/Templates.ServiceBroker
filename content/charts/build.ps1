﻿Param ([string]$Version = "0.1-dev")
$ErrorActionPreference = "Stop"
pushd $PSScriptRoot

if (!(Test-Path ~\.helm)) {helm init --client-only}

helmfile -f myvendor-myservice\helmfile.repos.yaml repos

$env:TENANT_ID="example-tenant"
helmfile -f myvendor-servicebroker\helmfile.yaml charts --args "--dry-run"
if ($LASTEXITCODE -ne 0) {throw "Exit Code: $LASTEXITCODE"}

if (!(Test-Path ..\artifacts)) {mkdir ..\artifacts | Out-Null}
helm package --dependency-update --destination ..\artifacts --version $Version myvendor-servicebroker

popd
