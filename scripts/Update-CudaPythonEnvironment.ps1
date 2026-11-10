[CmdletBinding()]
param(
	[string]$EnvironmentPath = (Join-Path $PSScriptRoot "..\CUDAPython.Dotnet.Python\bin\Release\net10.0\python-env"),
	[switch]$Upgrade
)

$ErrorActionPreference = "Stop"
$manifestPath = Join-Path $PSScriptRoot "python-runtime-win-x64.json"
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json

function Get-Sha256Hash([string]$FilePath) {
	$stream = [System.IO.File]::OpenRead($FilePath)
	$sha256 = [System.Security.Cryptography.SHA256]::Create()
	try {
		return [System.BitConverter]::ToString($sha256.ComputeHash($stream)).Replace("-", "").ToLowerInvariant()
	}
	finally {
		$sha256.Dispose()
		$stream.Dispose()
	}
}

if ($env:OS -ne "Windows_NT" -or $env:PROCESSOR_ARCHITECTURE -ne "AMD64") {
	throw "The bundled Python runtime currently supports Windows x64 only."
}

$resolvedEnvironmentPath = [System.IO.Path]::GetFullPath($EnvironmentPath)
$runtimeConfigPath = Join-Path $resolvedEnvironmentPath "runtime.json"
$pythonExecutable = Join-Path $resolvedEnvironmentPath "python\python.exe"

if (-not $Upgrade -and (Test-Path $runtimeConfigPath) -and (Test-Path $pythonExecutable)) {
	$currentRuntime = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
	if ($currentRuntime.pythonVersion -eq $manifest.pythonVersion -and $currentRuntime.runtimeId -eq $manifest.runtimeId) {
		Write-Output "Using the existing CPython $($manifest.pythonVersion) environment at '$resolvedEnvironmentPath'."
		return
	}
}

$environmentParent = Split-Path -Parent $resolvedEnvironmentPath
$downloadDirectory = Join-Path $env:TEMP "CUDAPython.Dotnet\downloads"
$archivePath = Join-Path $downloadDirectory (Split-Path -Leaf $manifest.pythonArchiveUrl)
$partialArchivePath = "$archivePath.partial"
$stagingPath = "$resolvedEnvironmentPath.staging-$([guid]::NewGuid().ToString('N'))"
$backupPath = "$resolvedEnvironmentPath.backup-$([guid]::NewGuid().ToString('N'))"

New-Item -ItemType Directory -Path $environmentParent -Force | Out-Null
New-Item -ItemType Directory -Path $downloadDirectory -Force | Out-Null

try {
	if (-not (Test-Path $archivePath) -or
		(Get-Sha256Hash $archivePath) -ne $manifest.pythonArchiveSha256) {
		if (Test-Path $archivePath) {
			Remove-Item -LiteralPath $archivePath -Force
		}
		if (Test-Path $partialArchivePath) {
			Remove-Item -LiteralPath $partialArchivePath -Force
		}

		Invoke-WebRequest -Uri $manifest.pythonArchiveUrl -OutFile $partialArchivePath
		$downloadHash = Get-Sha256Hash $partialArchivePath
		if ($downloadHash -ne $manifest.pythonArchiveSha256) {
			throw "The downloaded CPython archive failed SHA-256 verification."
		}

		Move-Item -LiteralPath $partialArchivePath -Destination $archivePath
	}

	New-Item -ItemType Directory -Path $stagingPath | Out-Null
	& tar.exe -xzf $archivePath -C $stagingPath
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to extract the bundled CPython runtime."
	}

	$stagedPython = Join-Path $stagingPath "python\python.exe"
	if (-not (Test-Path $stagedPython) -or -not (Test-Path (Join-Path $stagingPath "python\python311.dll"))) {
		throw "The CPython archive does not contain the expected Python 3.11 x64 runtime files."
	}

	$pipCache = Join-Path $env:TEMP "CUDAPython.Dotnet\pip-cache"
	New-Item -ItemType Directory -Path $pipCache -Force | Out-Null
	& $stagedPython -m pip install --upgrade --disable-pip-version-check --no-warn-script-location --cache-dir $pipCache $manifest.cudaPythonRequirement
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to install the latest CPython-compatible cuda-python release."
	}

	& $stagedPython -m pip check
	if ($LASTEXITCODE -ne 0) {
		throw "The bundled Python environment has dependency conflicts."
	}

	$requirements = & $stagedPython -m pip freeze --all
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to create the Python dependency lock file."
	}
	[System.IO.File]::WriteAllLines(
		(Join-Path $stagingPath "requirements.lock.txt"),
		[string[]]$requirements,
		[System.Text.UTF8Encoding]::new($false))

	$runtimeMetadataScript = @'
import importlib.metadata
import json
import os
from pathlib import Path
import sys

prefix = Path(sys.prefix).resolve()
dll = prefix / f"python{sys.version_info.major}{sys.version_info.minor}.dll"
if not dll.is_file():
	raise FileNotFoundError(f"CPython shared library not found: {dll}")

python_paths = []
for entry in sys.path:
	if not entry:
		continue
	resolved = Path(entry).resolve()
	try:
		relative = resolved.relative_to(prefix)
	except ValueError:
		continue
	python_paths.append((Path("python") / relative).as_posix())

print(json.dumps({
	"pythonDllPath": (Path("python") / dll.relative_to(prefix)).as_posix(),
	"pythonHome": "python",
	"pythonPath": os.pathsep.join(python_paths),
	"pythonVersion": ".".join(map(str, sys.version_info[:3])),
	"cudaPythonVersion": importlib.metadata.version("cuda-python"),
	"runtimeId": "win-x64"
}, indent=2))
'@
	$runtimeMetadataScriptPath = Join-Path $stagingPath "_write_runtime_metadata.py"
	[System.IO.File]::WriteAllText(
		$runtimeMetadataScriptPath,
		$runtimeMetadataScript,
		[System.Text.UTF8Encoding]::new($false))
	$runtimeMetadataLines = & $stagedPython $runtimeMetadataScriptPath
	if ($LASTEXITCODE -ne 0) {
		throw "Failed to inspect the bundled Python environment."
	}
	Remove-Item -LiteralPath $runtimeMetadataScriptPath -Force
	$runtimeMetadataJson = $runtimeMetadataLines -join [Environment]::NewLine
	[System.IO.File]::WriteAllText(
		(Join-Path $stagingPath "runtime.json"),
		$runtimeMetadataJson,
		[System.Text.UTF8Encoding]::new($false))
	Copy-Item -LiteralPath $manifestPath -Destination (Join-Path $stagingPath "provisioning.json")

	if (Test-Path $resolvedEnvironmentPath) {
		Move-Item -LiteralPath $resolvedEnvironmentPath -Destination $backupPath
	}
	try {
		Move-Item -LiteralPath $stagingPath -Destination $resolvedEnvironmentPath
	}
	catch {
		if (Test-Path $backupPath) {
			Move-Item -LiteralPath $backupPath -Destination $resolvedEnvironmentPath
		}
		throw
	}

	if (Test-Path $backupPath) {
		Remove-Item -LiteralPath $backupPath -Recurse -Force
	}

	Write-Output "Bundled CPython $($manifest.pythonVersion) and cuda-python $((Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json).cudaPythonVersion) at '$resolvedEnvironmentPath'."
}
finally {
	if (Test-Path $partialArchivePath) {
		Remove-Item -LiteralPath $partialArchivePath -Force
	}
	if (Test-Path $stagingPath) {
		Remove-Item -LiteralPath $stagingPath -Recurse -Force
	}
}
