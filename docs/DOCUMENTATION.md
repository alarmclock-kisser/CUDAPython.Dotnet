# CUDAPython.Dotnet – Dokumentation

> Eine .NET-10-Wrapper-Bibliothek und REST-API, die die gesamte **cuda-python**-API (CUDAPython) über eine *generische Discovery- und Invoke-Schicht* zugänglich macht – ohne fest verkabelte C#-Methoden pro Python-Member, ohne Python-Installation beim Verbraucher, ohne Netzwerkzugriff zur Laufzeit.

**Zielgruppe:** Entwickler, die cuda-python aus .NET-10-Anwendungen (Windows x64 oder Linux x64) nutzen wollen – direkt über die NuGet-Bibliothek oder über die mitgelieferte REST-API (Windows-only) mit Swagger.

**Weitere Quelldokumente:** `README.md` und `PLAN.md` (Root des Repos), `CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.http` (Request-Beispiele für alle Endpunkte).

---

## Inhaltsverzeichnis

1. [Überblick](#1-überblick)
2. [Schnellstart – NuGet-Verbraucher](#2-schnellstart--nuget-verbraucher)
3. [Schnellstart – REST API](#3-schnellstart--rest-api)
4. [Verfügbarkeit & Voraussetzungen](#4-verfügbarkeit--voraussetzungen)
5. [Funktionen im Detail – API-Endpunkte](#5-funktionen-im-detail--api-endpunkte)
6. [Python-Umgebung & Provisions-Pipeline](#6-python-umgebung--provisions-pipeline)
7. [Build- & Pack-Pipeline](#7-build--pack-pipeline)
8. [Technische Architektur](#8-technische-architektur)
9. [Konfiguration](#9-konfiguration)
10. [Fehlerbehandlung & Sicherheit](#10-fehlerbehandlung--sicherheit)
11. [Tests](#11-tests)
12. [Bekannte Einschränkungen](#12-bekannte-einschränkungen)

---

## 1. Überblick

CUDAPython.Dotnet löst drei Probleme in einem Paket:

1. **Zugriff auf cuda-python aus .NET** – CPython 3.11 wird *in-process* via [Python.NET](https://github.com/pythonnet/pythonnet) eingebettet. Die ganze `cuda.*`-API-Oberfläche (Module, öffentliche Funktionen, öffentliche Class-Methoden) wird zur Laufzeit *entdeckt* (Reflection auf der Python-Seite, keine Code-Generierung) und über einen *generischen Invoke* aufgerufen.
2. **REST-API mit Swagger** – ein ASP.NET-Core-Host macht die same Schicht über HTTP erreichbar: `/api/info/*` (Runtime-/GPU-Infos), `/api/cuda-python/functions` (Discovery), `/api/cuda-python/invoke` (generischer JSON-Invoke), `/api/maintenance/*` (Umwelt-Update, NuGet-Build). NSwag liefert OpenAPI/Swagger.
3. **Self-contained Packaging** – beim Build und `dotnet pack` wird eine geprüfte, pinne CPython-Standalone-Umgebung (astral-sh python-build-standalone) + `cuda-python` und alle pip-Abhängigkeiten in `python-env/` gebaut und in den NuGet-Paket als **RID-scoped `contentFiles`** gebündelt (`win-x64/python-env/` + `linux-x64/python-env/`). Der Verbraucher erhält die komplette Umgebung *neben* seinen Assemblies – kein Python-Setup, kein Netzwerk, keine Install-Skripte. NuGet kopiert nur das Verzeichnis, das zum aktiven RID des Verbrauchers passt.

### Architektur (Skizze)

```
┌────────────────────────────────────────────────────────────────────────┐
│  Verbraucher (net10.0, win-x64 ODER linux-x64)                        │
│  └─ NuGet: CUDAPython.Dotnet  (0.2.0, MIT)                             │
│      ├─ lib/net10.0/CUDAPython.Dotnet.Python.dll (+.xml)              │
│      ├─ LICENSE, NOTICE, README.md                                    │
│      └─ contentFiles/any/net10.0/{win-x64,linux-x64}/python-env/      │
│          (CPython 3.11.18 + cuda-python 13.5.0 + Dependencies,         │
│           runtime.json, requirements.lock.txt, provisioning.json)      │
│           → nur das RID-matchinge Verzeichnis wird neben die          │
│             Assemblies des Verbrauchers kopiert (PackageCopyToOutput)  │
│                                                                        │
│  CudaPythonRuntime.Initialize()                                       │
│    → liest python-env-<rid>/runtime.json (Fallback: python-env/)      │
│      → setzt PythonDLL/Home/Path                                      │
│    → PythonEngine.Initialize() (embedded CPython, GIL)                │
│    → exec() eines Helper-Skripts in ein privates PyDict              │
│    → DiscoverFunctions() / Invoke() / InvokeJson() / …               │
└────────────────────────────────────────────────────────────────────────┘
			  ▲                                        ▲
			  │ (direkt)                               │ (HTTP)
┌────────────────────────────────────────────────────────────────────────┐
│  CUDAPython.Dotnet.Api (ASP.NET Core)                                 │
│  Program: promotion python-env.pending → python-env beim Start,        │
│           danach Singleton CudaPythonRuntime.Initialize()              │
│  Controllers → Services → CudaPythonRuntime (in-process Python.NET)   │
│    /api/info*                  → PythonEnvironmentService (runtime.json│
│                                    + pip check/freeze als Subprocess   │
│                                    von python-env\python\python.exe)   │
│                                    + CUDA-Driver-/Device-JSON-Helper   │
│    /api/cuda-python/functions  → runtime.DiscoverFunctions()          │
│    /api/cuda-python/invoke     → runtime.InvokeJson() (Admin-Auth)    │
│    /api/maintenance/*          → MaintenanceService (nur Development)  │
│  NSwag: OpenAPI /swagger/v1/swagger.json, UI /swagger (nur Dev)       │
└────────────────────────────────────────────────────────────────────────┘

  GPU-Anforderung:  NVIDIA-Treiber + CUDA-fähige GPU sind *Host*-Voraussetzungen
					für Device-Operationen – weder vom Paket noch vom NuGet
					installierbar (weder aus einem Python-Wheel noch sonstwie).
```

**Kern-Designentscheidungen (Kurzfassung):**

- *Kein* C#-Methoden-Spiegel der Python-API – stattdessen ein Runtime-Discovery-Snapshot + ein generischer JSON-Invoke.
- In-process eingebettete CPython (Python.NET), nicht Subprocess/pybind11/ctypes.
- Python-Umgebung = **Standalone-CPython-Tree** (kein venv!), zur Build-/Pack-Zeit per PowerShell-Skript mit SHA-256-Prüfung + atomarem Swap provisioniert.
- Updates werden *gestaged* (`python-env.pending`) und beim nächsten Start *promoted* – eine geladene CPython-DLL wird nie während der Laufzeit ausgetauscht.

---

## 2. Schnellstart – NuGet-Verbraucher

### 2.1 Paket installieren

```powershell
dotnet add package CUDAPython.Dotnet
```

> **Wichtig:** Das Paket targetet `net10.0` mit RID-scoped Content für **win-x64** und **linux-x64**. Auf x32/arm64-Plattformen ist es nicht verwendbar (siehe Abschnitt 12). Die NuGet-Bibliothek ist cross-platform; die mitgelieferte REST-API ist **Windows-only**.

Beim Restore/Bauen des *Verbraucher*projekts werden die `contentFiles` automatisch **neben die Assemblies** nach `bin/…/python-env/` kopiert. Es gibt **keine** Install-Skripte, **kein** benötigtes Host-Python, **keinen** Netzwerkzugriff zur Laufzeit.

### 2.2 Minimal-Beispiel (C#)

```csharp
using CUDAPython.Dotnet;
using pythonnet;

// Einmalig pro Prozess – lädt die Python-Umgebung
// aus python-env/runtime.json neben den Assemblies.
var runtime = CudaPythonRuntime.Initialize();

// Alle cuda.*-Funktionen/Methoden entdecken (mit Signatur + Doku).
var functions = runtime.DiscoverFunctions();
var function = functions.Single(
	i => i.PythonQualifiedName == "cuda.bindings.driver.cuInit");

// Aufruf unter GIL; das Ergebnis ist ein owned PyObject
// (vom Aufrufer zu dispen, nur unter Py.GIL() zugreifen).
using (Py.GIL())
using (var result = runtime.Invoke(function, [0]))
{
	Console.WriteLine(result);
}
```

**Regeln im Kurzform:**

- `Initialize()` ist **einmalig pro Prozess** und nicht neu konfigurierbar (werfen, wenn `PythonEngine.IsInitialized` bereits true ist oder die Options abweichen).
- Zurückgegebene `PyObject`-Instanzen sind **vom Aufrufer zu dispen**; Zugriff/Disposal **nur** unter `Py.GIL()`.
- Argumente können gewöhnliche .NET-Werte oder `PyObject`-Instanzen aus vorherigen Aufrufen sein.
- Die NVIDIA-GPU/Treiber ist für Device-Operationen eine **Host-Voraussetzung** – der Paket-Setup selbst läuft aber **ohne GPU** (z. B. `cuDriverGetVersion`).

### 2.3 Alternativen

- `runtime.Invoke("math", "sqrt", [9d])` – Modul + Attribut-Pfad statt Descriptor (z. B. für nicht-cuda-Module).
- `runtime.InvokeJson(module, path, argsJson, kwargsJson)` – der JSON-Grenzpfad, den die REST-API intern nutzt (siehe Abschnitt 5/8).
- `runtime.GetCudaDriverInfoJson()` / `GetCudaDevicesJson()` – fertige JSON-Infos.

---

## 3. Schnellstart – REST API

### 3.1 API starten

```powershell
dotnet run --project CUDAPython.Dotnet.Api
```

- **Base URL (Beispiel):** `http://localhost:5248` (aus `CUDAPython.Dotnet.Api.http`)
- **Swagger UI:** `http://localhost:5248/swagger` – **nur im Development-Profile** verfügbar
- **OpenAPI-JSON:** `http://localhost:5248/swagger/v1/swagger.json`
- **Request-Beispiele für alle Endpunkte:** `CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.http` (in Visual Studio direkt ausführbar)
- **HTTPS-Redirection** ist aktiviert.

### 3.2 Endpunkte (Übersicht)

| Route | Methode | Auth | Zweck |
|---|---|---|---|
| `GET /api/info` | GET | keine | API-Name/-Version, Python-Runtime-Readiness |
| `GET /api/info/python-environment` | GET | keine | Python/cuda-python-Version, Lock-Zähler, Update-Tool-Verfügbarkeit, Pending-Flag |
| `GET /api/info/python-environment/validation` | GET | keine | `pip check`, Lock-vs-`pip freeze --all`-Vergleich, `import cuda.bindings.driver` in Subprocess |
| `GET /api/info/cuda/driver` | GET | keine | CUDA-Treiber-Version + `cuInit`-Status |
| `GET /api/info/cuda/devices` | GET | keine | Name/UUID/Compute-Cap/Speich/Attribute + Status pro Query |
| `GET /api/cuda-python/functions` | GET | keine | Discovery (Filter + Paginierung, s. u.) |
| `POST /api/cuda-python/invoke` | POST | **Admin** | Generischer JSON-Invoke auf `cuda.*` |
| `POST /api/maintenance/python-environment/update` | POST | **Admin**, **nur Development** | Stages neue Python-Umgebung in `python-env.pending` (202) |
| `POST /api/maintenance/nuget/build` | POST | **Admin**, **nur Development** | `dotnet pack -c Release` aus lokalem Checkout; liefert id/version/size/SHA-256 |

### 3.3 Invoke-Beispiel (curl)

```bash
curl -X POST "http://localhost:5248/api/cuda-python/invoke" \
  -H "Content-Type: application/json" \
  -H "X-API-Key: <dein-Key>" \
  -d '{
		"moduleName": "cuda.bindings.driver",
		"attributePath": "cuDriverGetVersion",
		"arguments": []
	  }'
```

> **Auth (Kurzform):** In **Development + Loopback** (IPv4 bzw. IPv4-mapped-IPv6) ist **kein** Key nötig. Andernfalls muss das Header `X-API-Key` dem konfigurierten Key (`CudaPython:AdminApiKey`-Konfig oder `CUDAPYTHON_ADMIN_KEY`-Env) per **Constant-Time-Compare** entsprechen. Kein Key konfiguriert → **503** „Administrative API is disabled".

### 3.4 Discovery-Beispiel

```bash
curl "http://localhost:5248/api/cuda-python/functions?modulePrefix=cuda.bindings&search=cuInit&limit=10"
```

Query-Parameter: `modulePrefix`, `search`, `offset`, `limit` (1–500, Default 100).

---

## 4. Verfügbarkeit & Voraussetzungen

| Komponente | Anforderung |
|---|---|
| **Plattform** | **Windows x64** (`win-x64`) – strikt geprüft (Build-Target wirft sonst einen Fehler) |
| **.NET** | **.NET 10** (alle Projekte targeten `net10.0`) |
| **GPU/Treiber** | NVIDIA-Treiber + CUDA-fähige GPU **nur für Device-Operationen** – weder vom Paket installier- noch bei Build/Pack erforderlich |
| **Python** | **Keine** Python-Installation beim Verbraucher erforderlich (bündelte Standalone-CPython 3.11.18) |
| **Netzwerk** | **Build & Pack** brauchen Netzwerk (pinne CPython-Release + PyPI); **deployte Anwendung nicht** |
| **Python.NET** | 3.2.1 – erfordert **Python ≥ 3.11** (deshalb 3.11 als kleinste kompatible Minor-Version) |

**NuGet-Paket:** `CUDAPython.Dotnet`, Version **0.2.0**, Target `net10.0`, MIT-Lizenz. Enthält RID-scoped `contentFiles` für **win-x64** und **linux-x64** – NuGet kopiert nur das zum Verbraucher-RID passende Verzeichnis.

---

## 5. Funktionen im Detail – API-Endpunkte

### 5.1 `InfoController` (`/api/info`, keine Auth)

| Endpunkt | Liefert |
|---|---|
| `GET /api/info` | API-Name & -Version, Python-Runtime-Readiness |
| `GET /api/info/python-environment` | Python-Version, cuda-python-Version, Lock-Zähler (Requirements-Hash), ob das Update-Tool verfügbar ist, ob ein Update pending ist |
| `GET /api/info/python-environment/validation` | Ergebnis von `pip check`, Vergleich des Locks mit `pip freeze --all`, und `import cuda.bindings.driver` in einem Subprocess |
| `GET /api/info/cuda/driver` | CUDA-Treiber-Version + Status von `cuInit(0)` |
| `GET /api/info/cuda/devices` | Pro Device: Name, UUID, Compute-Cap, Gesamt-Speicher, Attribute (Multiprocessor-Anzahl, max Threads/Block, Clock-Rate) + Status-Code pro Query |

> Die Validierung nutzt den **Subprocess** des gebündelten `python.exe` (via `ExternalProcessRunner`), nicht den in-process-Runtime.

### 5.2 `CudaPythonController` (`/api/cuda-python`)

#### `GET /api/cuda-python/functions` (keine Auth)

Discovery über den gesamten `cuda.*`-Snapshot. Query-Parameter:

| Param | Beschreibung |
|---|---|
| `modulePrefix` | Filtert Module nach Präfix (z. B. `cuda.bindings`) |
| `search` | Textsuche in Name/Signatur/Doku |
| `offset` | Paginierungs-Offset (Default 0) |
| `limit` | Ergebnis-Limit, **1–500**, Default **100** |

#### `POST /api/cuda-python/invoke` (**Admin-Auth**, Request-Size-Limit 131072 Byte)

Request-Contract (`CudaPythonInvokeRequest`):

| Feld | Regel |
|---|---|
| `moduleName` | Regex `^cuda(?:\.[A-Za-z][A-Za-z0-9_]*)*$` – nur `cuda` oder `cuda.*` |
| `attributePath` | Regex `^[A-Za-z][A-Za-z0-9_]*(?:\.[A-Za-z][A-Za-z0-9_]*)*$` – public Member-Pfad |
| `arguments` | JSON-Array, **≤ 64** positional Argumente |
| `keywordArguments` | JSON-Objekt, **≤ 64** Keyword-Argumente |

> **JSON-Grenze:** CUDA-Pointer, Buffers, Handles und Python-Objekte mit custom Lifetimes sind **nicht** darstellbar – nicht-JSON-Werte werden als Typ-Metadaten (z. B. `{$type, repr}`) zurückgegeben, nicht als lebende Objekte (Details in Abschnitt 8).

### 5.3 `MaintenanceController` (`/api/maintenance`, **Admin-Auth + nur Development**)

In anderen Profilen: **404** (zusätzlich zur Admin-Auth).

| Endpunkt | Verhalten |
|---|---|
| `POST /api/maintenance/python-environment/update` | **202** – führt das Provisions-Skript aus und **stages** die neue Umgebung in `python-env.pending`. Aktivierung passiert beim **nächsten Start** (Promotion, s. Abschnitt 10). |
| `POST /api/maintenance/nuget/build` | Führt `dotnet pack -c Release` aus einem **lokalen Checkout** (Pfad via `CUDAPYTHON_REPOSITORY_ROOT` bzw. `CudaPython:RepositoryRoot`) und liefert id, Version, Größe, SHA-256 des `.nupkg`. |

---

## 6. Python-Umgebung & Provisions-Pipeline

### 6.1 Aufbau von `python-env/`

```
python-env/
├── runtime.json             # pythonDllPath=python/python311.dll
│                            # pythonHome=python
│                            # pythonPath=python/python311.zip;python/DLLs;python/Lib;python;python/Lib/site-packages
│                            # pythonVersion=3.11.18, cudaPythonVersion=13.5.0, runtimeId=win-x64
├── provisioning.json        # Kopie von scripts/python-runtime-win-x64.json (Manifest)
├── requirements.lock.txt    # pip freeze --all (UTF-8, no BOM)
└── python/
	├── python.exe, pythonw.exe
	├── python311.dll, python3.dll
	├── vcruntime140*.dll
	├── python311.zip
	├── Lib/  (→ Lib/site-packages/  mit den cuda-python-Wheels)
	├── DLLs/, include/, libs/, Scripts/, tcl/
```

> **Kein venv!** Das ist ein **Standalone-CPython-Install-Tree** (aus [astral-sh/python-build-standalone](https://github.com/astral-sh/python-build-standalone)) mit pip-installierten Wheels in `Lib/site-packages`. `runtime.json` nutzt **relative, relocatable Pfade** (gegen das JSON-Verzeichnis aufgelöst), sodass der ganze Ordner verschiebbar ist.

### 6.2 Pin-Manifests

**`scripts/python-runtime-win-x64.json`:**

```json
{
  "runtimeId": "win-x64",
  "pythonVersion": "3.11.18",
  "pythonBuildTag": "20261026",
  "pythonArchiveUrl": "https://github.com/astral-sh/python-build-standalone/releases/download/20261026/cpython-3.11.18%2B20261026-x86_64-pc-windows-msvc-install_only.tar.gz",
  "pythonArchiveSha256": "75cb501dc11c7b55c6b447c58e4910bce5b1f609f1017328b4f3a858dfaf5428",
  "cudaPythonRequirement": "cuda-python",
  "pythonExecutable": "python/python.exe",
  "pythonSharedLibrary": "python/python311.dll"
}
```

**`scripts/python-runtime-linux-x64.json`:**

```json
{
  "runtimeId": "linux-x64",
  "pythonVersion": "3.11.18",
  "pythonBuildTag": "20261110",
  "pythonArchiveUrl": "https://github.com/astral-sh/python-build-standalone/releases/download/20261110/cpython-3.11.18%2B20261110-x86_64-unknown-linux-gnu-install_only.tar.gz",
  "pythonArchiveSha256": "ac0b758b6ae4ba1cb87f7091eb6bb17c9a383a9bcdb5c18493cdd4f34e35ae8c",
  "cudaPythonRequirement": "cuda-python",
  "pythonExecutable": "python/bin/python3",
  "pythonSharedLibrary": "python/lib/libpython3.11.so.1.0"
}
```

> **Linux-Soname:** Das exakte `.so`-Suffix (z. B. `.so.1.0` vs. `.so.3.11`) muss auf dem Linux-Host verifiziert werden; das Provisions-Skript prüft den Manifest-Pfad und fällt per Inline-Python-Skript auf `libpython3.11.so` zurück, falls der primäre Pfad nicht existiert.

**Versionen (R成果 aus `requirements.lock.txt`):**

| Paket | Version |
|---|---|
| `cuda-python` | **13.5.0** |
| `cuda-bindings` | 13.5.0 |
| `cuda-core` | 1.2.2 |
| `cuda-pathfinder` | 1.8.6 |
| `numpy` | 2.4.6 |
| `pip`, `setuptools` | (mitgeliefert) |

### 6.3 Das Provisions-Skript `scripts/Update-CudaPythonEnvironment.ps1`

Ablauf (idempotent, kein Host-Python nötig):

1. **Prüfung:** `OS=Windows_NT` **und** `PROCESSOR_ARCHITECTURE=AMD64` (sonst Fehler).
2. **Idempotenz:** Wenn `runtime.json` + `python.exe` existieren und Manifest-Version/RID matchen (und kein `-Upgrade`): **No-Op**.
3. **Download** nach `%TEMP%\CUDAPython.Dotnet\downloads` mit `.partial`-Temp-Datei + **SHA-256-Verifikation** (gecacht, wenn bereits valid).
4. **Extraktion** per `tar.exe` in ein Staging-Verzeichnis `python-env.staging-<guid>`; Assert auf `python/python.exe` **und** `python/python311.dll`.
5. `python -m pip install --upgrade <cudaPythonRequirement>` (persistent pip-Cache in `%TEMP%`) → `pip check` (muss bestehen) → `pip freeze --all` → `requirements.lock.txt` (UTF-8, no BOM).
6. Ein Inline-Python-Skript (`_write_runtime_metadata.py`) **schreibt `runtime.json`** aus `sys.prefix`/`sys.path` (relative, relocatable) + `importlib.metadata.version("cuda-python")`.
7. Manifest → `provisioning.json`, dann **atomarer Swap**: alte Umgebung → `python-env.backup-<guid>`, Staging → Final-Pfad; **Rollback** bei Fehler; Backup wird danach gelöscht.

**Aufruf-Szenarien** (s. Abschnitt 7): Library-Build, NuGet-Pack, API-Runtime-Update.

---

## 7. Build- & Pack-Pipeline

### 7.1 MSBuild-Targets (Projekt `CUDAPython.Dotnet.Python`)

| Target | Timing | Verhalten |
|---|---|---|
| `ValidateBundledPythonRuntime` | `BeforeTargets=Build` | **Fehler** bei `RID` außerhalb von `win-x64`/`linux-x64` |
| `ProvisionBundledPythonRuntimeWin` | `AfterTargets=Build` | (nur `OS=Windows_NT`, RID leer oder `win-x64`) Führt das Provisions-Skript aus → `$(TargetDir)python-env-win-x64` |
| `ProvisionBundledPythonRuntimeLinux` | `AfterTargets=Build` | (nur `RID=linux-x64`) Führt das Provisions-Skript aus → `$(TargetDir)python-env-linux-x64` – **muss auf einem Linux-Host (oder WSL) laufen** |

**MSBuild-Properties:**

| Property | Default | Wirkung |
|---|---|---|
| `CUDAPythonProvisionEnvironment` | `true` | `false` = Opt-out (nur dann, wenn man *bewusst* ohne gebündelte Runtime bauen will) |
| `CUDAPythonUpdateEnvironment` | `false` | `true` = hängt `-Upgrade` an (pip refresh) |

### 7.2 API-Kopie-Targets (`CUDAPython.Dotnet.Api`)

| Target | Timing | Ziel |
|---|---|---|
| `CopyBundledPythonRuntimeToApiOutput` | `AfterTargets=Build` | `..\CUDAPython.Dotnet.Python\bin\$(Config)\net10.0\python-env\**` → `$(TargetDir)python-env\` |
| `CopyBundledPythonRuntimeToApiPublish` | `AfterTargets=Publish` | … → `$(PublishDir)python-env\` |

Zusätzlich werden `scripts\*.ps1`/`*.json` als `tools\…` in die API-Output kopiert (für das Update-Endpoint zur Laufzeit).

### 7.3 NuGet-Pack-Target (`CUDAPython.Dotnet.Nuget`)

| Eigenschaft | Wert |
|---|---|
| `PackageId` | `CUDAPython.Dotnet` |
| `Version` | `0.2.0` |
| `IsPackable` | `true` |
| `IncludeBuildOutput` | `false` |
| `PackageLicenseExpression` | `MIT` |
| ProjectReference | `CUDAPython.Dotnet.Python` mit `PrivateAssets="all"` |

**Gebundelte Inhalte:** `..\README.md`, `..\LICENSE`, `..\NOTICE` (Root), das gebaute `CUDAPython.Dotnet.Python.dll` + `.xml` nach `lib/net10.0/`, **und** die Python-Umgebung(en) als **RID-scoped contentFiles**.

**Target `IncludeBundledPythonEnvironmentWin`** (`BeforeTargets=_GetPackageFiles`, nur `OS=Windows_NT`):

1. Führt das Provisions-Skript **mit `-Upgrade`** in die Library-`bin`-`python-env-win-x64` neu aus (pip refresh beim Packen).
2. Fügt `python-env-win-x64\**\*` (Dotfiles ausgenommen) als `Content` mit
   `PackagePath=contentFiles/any/net10.0/win-x64/python-env/…`,
   `PackageCopyToOutput=true`, `BuildAction=Content` hinzu.

**Target `IncludeBundledPythonEnvironmentLinux`** (`BeforeTargets=_GetPackageFiles`, nur wenn `python-env-linux-x64/runtime.json` existiert und `CUDAPythonWindowsOnlyPackage` nicht `true` ist):

1. Fügt `python-env-linux-x64\**\*` als `Content` mit
   `PackagePath=contentFiles/any/net10.0/linux-x64/python-env/…` hinzu.
2. **Kein Provisions-Lauf** – die Linux-Umgebung muss bereits auf einem Linux-Host (oder WSL) provisioniert worden sein; das Target wird bei Windows-Builds übersprungen.

→ **Die ready-to-use-Umgebung wird als NuGet-contentFiles ausgeliefert und neben die Assemblies des Verbrauchers kopiert** – NuGet wählt automatisch das zum RID des Verbrauchers passende Verzeichnis. Keine Install-Skripte, kein Host-Python, kein Netzwerk zur Laufzeit.

### 7.4 Wichtige Befehle

```powershell
# Tests
dotnet test CUDAPython.Dotnet.Tests\CUDAPython.Dotnet.Tests.csproj

# Self-contained NuGet-Paket (Release, mit pip-Refresh)
dotnet pack CUDAPython.Dotnet.Nuget\CUDAPython.Dotnet.Nuget.csproj -c Release

# Windows-x64-only Paket CUDAPython.Dotnet.win-x64 (Version 0.3.0)
dotnet pack CUDAPython.Dotnet.Nuget\CUDAPython.Dotnet.Nuget.csproj -c Release -p:CUDAPythonWindowsOnlyPackage=true

# REST-API starten
dotnet run --project CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.csproj
```

> Build & Pack brauchen **Netzwerk** (pinne CPython-Release + PyPI). Die deployte Anwendung **nicht**.
>
> **Linux-Hinweis:** Auf Linux muss `LD_LIBRARY_PATH` den Pfad zur CPython-Shared-Library enthalten (z. B. `LD_LIBRARY_PATH=.../python-env/python/lib`), da pythonnet per `dlopen` die `libpython3.11.so` lädt. In der Praxis übernimmt das meist der Host-Shell-Context oder die `runtime.json`-Konfiguration.

### 7.5 Test-Kopie-Target

`CUDAPython.Dotnet.Tests`: Target `CopyBundledPythonRuntimeForTests` kopiert `python-env` in die Test-Output (damit die Integrationstests die gebündelte Runtime nutzen).

---

## 8. Technische Architektur

### 8.1 `CudaPythonRuntime.cs` (Kern, ~511 Zeilen)

**Mechanismus:** Ein **einziges** eingebettetes Python-Helper-Skript (C# raw string) wird **einmalig** bei `Initialize()` in ein privates `PyDict` ausgeführt (`PythonEngine.Exec(DiscoveryScript, pythonGlobals, pythonGlobals)`). Es definiert:

| Python-Funktion | Funktion |
|---|---|
| `_cuda_dotnet_discover(root, include_submodules)` | `importlib` + `pkgutil.walk_packages` über `cuda` und Submodule; `dir(module)`; fügt öffentliche **Funktionen** **und** öffentliche **Class-Methoden** (Name = `Class.Method`) hinzu; erfasst `inspect.signature` + `inspect.getdoc`; liefert JSON-Array `{moduleName, name, signature, documentation}` |
| `_cuda_dotnet_invoke_json(module, path, args_json, kwargs_json)` | Validiert, dass das Modul `cuda`/`cuda.*` ist (keine `_`-Prefix-Segmente); `importlib.import_module` + `operator.attrgetter(path)`; `function(*args, **kwargs)`; serialisiert das Ergebnis |
| `_cuda_dotnet_cuda_driver_info()` | `cuDriverGetVersion` + `cuInit(0)` |
| `_cuda_dotnet_cuda_devices()` | `cuDeviceGetCount` + pro Device: Name/Compute-Cap/Total-Mem/UUID + Attribute (Multiprocessor-Anzahl, max Threads/Block, Clock-Rate) |

**JSON-Serialisierung des Ergebnisses** (`_cuda_dotnet_json_value`):

| Python-Typ | JSON-Darstellung |
|---|---|
| `IntEnum` | `int` |
| `Enum` | `{"$type", name, value}` |
| Primitives (int/float/str/bool/None) | direkt |
| `bytes` | `{"$type": "bytes", "base64": …}` |
| `list`/`tuple` | JSON-Array |
| `dict` | JSON-Objekt |
| numpy-artig (hat `.item()`) | `.item()`-Wert |
| alles andere | `{"$type", repr}` (nur Metadaten, kein Live-Objekt) |

### 8.2 Öffentliche C#-API (alles GIL-scoped)

| Methode | Verhalten |
|---|---|
| `CudaPythonRuntime.Initialize(options = null)` | **Einmalig pro Prozess**, nicht neu konfigurierbar (weder, wenn `PythonEngine.IsInitialized` true ist, noch bei abweichenden Options). Standard: lädt `python-env-<rid>/runtime.json` (RID = `win-x64`/`linux-x64` via `OperatingSystem.IsWindows()`/`IsLinux()` + `ProcessArchitecture == X64`), mit Fallback auf `python-env/runtime.json` neben `AppContext.BaseDirectory`. Setzt `Runtime.PythonDLL` (oder `PYTHONNET_PYDLL`), `PythonEngine.PythonHome`, `PythonEngine.PythonPath`, dann `PythonEngine.Initialize()` + `BeginAllowThreads()` |
| `DiscoverFunctions()` | Ruft `_cuda_dotnet_discover` auf; **cacht** einen read-only-Snapshot + ein name→Descriptor-Dictionary |
| `Invoke(descriptor, positional, keyword)` | Prüft, dass der Descriptor im aktuellen Snapshot ist (Referenz-Identität), delegiert an path-based Invoke |
| `Invoke(moduleName, attributePath, positional, keyword)` | `Py.Import(module)` + `operator.attrgetter(path)`, `PyDict` für kwargs, Argumente via `ConvertArgument` (`null`→`None`, `PyObject`→`NewReference`, sonst `PyObject.FromManagedObject`), `callable.Invoke(args, kwargs)`; liefert ein **owned `PyObject`**, das der Aufrufer unter GIL dispen muss |
| `InvokeJson(module, path, argsJson, kwargsJson)` | Der REST-Pfad (JSON-Grenze); validiert die `cuda.*`-Regel **zusätzlich auf der C#-Seite** |
| `GetCudaDriverInfoJson()` / `GetCudaDevicesJson()` | Fertige JSON-Infos |

### 8.3 `CudaPythonRuntimeOptions` (record)

- `LoadFromFile` deserialisiert `runtime.json` und **löst relative Pfade gegen das Verzeichnis der Datei** (relocatable).
- `PackageRootModule` Default `"cuda"`, `IncludeSubmodules` Default `true`.
- `PYTHONNET_PYDLL`-Env-Variable ist Fallback für den DLL-Pfad.
- Unbekannte Properties werden ignoriert.

### 8.4 Design-Anmerkungen (wichtig)

- **Dynamic Reflection ≠ Code-Generierung:** Es wird **keine** statisch typisierte C#-Methode pro Python-Member emittiert. Stattdessen: Runtime-Discovery-Snapshot + generischer Invoke.
- **Native Hot Path = Zukunft:** Der Python.NET-Bridge bietet breitere dynamische Abdeckung, aber **keine** Low-Latency-Garantie.
- **JSON-Grenze:** CUDA-Pointer, Buffers, Handles und Python-Objekte mit custom Lifetimes brauchen spezialisierte managed Bindings; nicht-JSON-Werte werden als Typ-Metadaten zurückgegeben, nicht als lebende Objekte.
- **GIL:** In-process eingebettete CPython → GIL-Implications; Zugriff/Disposal von `PyObject` **nur** unter `Py.GIL()`.

### 8.5 API-Interna (Services/Middleware)

| Komponente | Rolle |
|---|---|
| `CudaPythonService` | Wrapper um `CudaPythonRuntime` für die Controller (Invoke + Discovery) |
| `PythonEnvironmentService` | Runtime.json + `pip check`/`pip freeze`/Import-Check als **Subprocess** des gebündelten `python.exe` (via `ExternalProcessRunner`) |
| `MaintenanceService` | Führt das Provisions-Skript (→ `python-env.pending`) bzw. `dotnet pack` aus (nur Development) |
| `PythonEnvironmentPromotion` | **Beim Start** (vor Web-App-Start) swappt `python-env.pending` → `python-env` (mit Backup/Rollback) – so wird eine geladene CPython-DLL nie während der Laufzeit ersetzt |
| `ExternalProcessRunner` | `ProcessStartInfo` mit `UseShellExecute=false`, `CreateNoWindow=true`, redirected stdout/stderr; **Cancellation killt den ganzen Process-Tree**; nutzt für pip check/freeze, Import-Checks, Provisions-Skript, `dotnet pack` |
| `AdminApiKeyAuthorizationFilter` | `[ServiceFilter]` auf Invoke + Maintenance (Details in Abschnitt 10) |
| `ApiExceptionHandler` | `IExceptionHandler` → RFC-7807 ProblemDetails (Details in Abschnitt 10) |

---

## 9. Konfiguration

### 9.1 Umgebungsvariablen

| Variable | Zweck | Priorität |
|---|---|---|
| `CUDAPYTHON_ADMIN_KEY` | Admin-API-Key (Fallback) | Konfig-Key `CudaPython:AdminApiKey` hat **höhere** Priorität |
| `CUDAPYTHON_REPOSITORY_ROOT` | Repo-Root für das NuGet-Build-Endpoint | Konfig-Key `CudaPython:RepositoryRoot` hat höhere Priorität |
| `PYTHONNET_PYDLL` | Fallback für den Python-DLL-Pfad | `runtime.json` hat Priorität |

> **Keine Secrets in `appsettings.json`** – dort sind nur Logging-Settings. Der Admin-Key kommt aus Env/Konfig-Key.

### 9.2 MSBuild-Properties

| Property | Default | Zweck |
|---|---|---|
| `CUDAPythonProvisionEnvironment` | `true` | Opt-out: Build *ohne* gebündelte Runtime |
| `CUDAPythonUpdateEnvironment` | `false` | `true` = hängt `-Upgrade` an (pip refresh) |

### 9.3 `runtime.json` (Single Source of Truth zur Laufzeit)

```json
{
  "pythonDllPath": "python/python311.dll",
  "pythonHome": "python",
  "pythonPath": "python/python311.zip;python/DLLs;python/Lib;python;python/Lib/site-packages",
  "pythonVersion": "3.11.18",
  "cudaPythonVersion": "13.5.0",
  "runtimeId": "win-x64"
}
```

- `CudaPythonRuntime.Initialize()` (Default) lädt es aus `python-env-<rid>/runtime.json` (RID-gesteuert: `win-x64` auf Windows x64, `linux-x64` auf Linux x64), mit **Fallback** auf `python-env/runtime.json` neben `AppContext.BaseDirectory`.
- **Relative Pfade werden gegen das Verzeichnis der JSON-Datei** aufgelöst → der `python-env*`-Ordner ist **relocatable** (frei verschiebbar).
- `pythonPath`-Einträge werden mit dem OS-`PathSeparator` gejoined.

### 9.4 `appsettings.json` / `appsettings.Development.json`

Nur **Logging-Settings**. Keine Secrets.

---

## 10. Fehlerbehandlung & Sicherheit

### 10.1 ProblemDetails (RFC 7807)

`ApiExceptionHandler` (`IExceptionHandler`, registriert via `AddExceptionHandler` + `AddProblemDetails` + `UseStatusCodePages`):

| Exception | HTTP |
|---|---|
| `KeyNotFoundException` | **404** |
| `ArgumentException` | **400** |
| `CudaPythonException` (wraps `PythonException`) | **422** |
| `MaintenanceOperationException` | **502** |
| `FileNotFoundException` (Umgebung fehlt) | **503** |
| `PlatformNotSupportedException` | **501** |
| `InvalidOperationException` | **409** |
| unhandled | Pass-Through (Default-Handler) |

- `CudaPythonException` (in der Library) wrappet jeden Python-side-Fehler, `PythonException` als inner Exception.
- `MaintenanceOperationException` wrappet External-Process-Fehler (powershell/dotnet).

### 10.2 Authorization (`AdminApiKeyAuthorizationFilter`, `IAsyncActionFilter`)

Angewendet via `[ServiceFilter]` auf **Invoke** + **Maintenance**-Endpoints:

| Bedingung | Ergebnis |
|---|---|
| **Development** **und** **Loopback** (IPv4 bzw. IPv4-mapped-IPv6) | **Erlaubt, ohne Key** |
| Sonst: kein Key konfiguriert | **503** „Administrative API is disabled" |
| Sonst: `X-API-Key` ≠ konfiguriertem Key | **401** |
| Sonst: Match (per **Constant-Time-Compare** `CryptographicOperations.FixedTimeEquals`) | Erlaubt |

**Konfigurierter Key:** `CudaPython:AdminApiKey`-Konfig **oder** `CUDAPYTHON_ADMIN_KEY`-Env (Konfig hat höhere Priorität).

**Maintenance-Endpoints** sind zusätzlich **Development-gated** (sonst **404**) und der Service prüft `IsDevelopment()` erneut.

### 10.3 Staging-Promotion (wichtig für Updates)

- `PythonEnvironmentPromotion.PromotePendingUpdate()` läuft **beim Start, bevor** die Web-App startet und **bevor** CPython initialisiert wird.
- Swappt `python-env.pending` → `python-env` (mit Backup/Rollback).
- **Ziel:** eine **geladene CPython-DLL wird nie während der Laufzeit ausgetauscht** – Updates aktivieren erst beim nächsten Restart.

### 10.4 HTTPS

`UseHttpsRedirection` ist im Pipeline aktiviert.

---

## 11. Tests

**Projekt:** `CUDAPython.Dotnet.Tests` (MSTest 4.5.1, net10.0, ProjectReference zur Library).

### 11.1 `CudaPythonRuntimeIntegrationTests` (der kanonische Verbraucher-Beispiel)

- `CudaPythonRuntime.Initialize()` (Default → `python-env/runtime.json` neben der Test-Assembly)
- `DiscoverFunctions()` – assert, dass `cuda.bindings.driver.cuInit` vorhanden ist
- `runtime.Invoke("math", "sqrt", [9d])` → **3.0** unter `Py.GIL()`
- **Läuft ohne GPU** (nur gebündelte CPython 3.11)

### 11.2 `CudaPythonRuntimeOptionsTests`

- `LoadFromFile` mit **absoluten** und **relativen** Pfaden (relative werden gegen das Konfig-Verzeichnis aufgelöst; `pythonPath`-Einträge werden mit `PathSeparator` gesplittet/gejoined)
- Unbekannte Properties werden ignoriert
- `PackageRootModule` Default ist `cuda`

### 11.3 Request-Beispiele

`CUDAPython.Dotnet.Api\CUDAPython.Dotnet.Api.http` – direkt in Visual Studio ausführbare Request-Beispiele für **alle** Endpunkte. Base URL: `http://localhost:5248`.

Beispiel (Invoke):

```http
### Invoke cuDriverGetVersion
POST http://localhost:5248/api/cuda-python/invoke
Content-Type: application/json
X-API-Key: {{adminKey}}

{
  "moduleName": "cuda.bindings.driver",
  "attributePath": "cuDriverGetVersion",
  "arguments": []
}
```

---

## 12. Bekannte Einschränkungen

| Thema | Einschränkung |
|---|---|
| **Plattform** | **win-x64 + linux-x64** (x64 only, kein x32/arm64). `ValidateBundledPythonRuntime` wirft bei `RID` außerhalb von `win-x64`/`linux-x64`. Die **REST-API** ist **Windows-only** (nutzt `python-env\python\python.exe` als Subprocess); die **NuGet-Bibliothek** ist cross-platform. |
| **.NET-TFM** | `net10.0` (alle Projekte) |
| **JSON-Grenze** | CUDA-Pointer, Buffers, Handles, Python-Objekte mit custom Lifetimes sind nicht darstellbar – werden als `{$type, repr}`-Metadaten zurückgegeben, nicht als Live-Objekte. Für solche Fälle sind **spezialisierte managed Bindings** nötig. |
| **GIL** | In-process eingebettete CPython → GIL-Implications; `PyObject`-Zugriff/Disposal nur unter `Py.GIL()`. |
| **Python.NET** | 3.2.1 erfordert **Python ≥ 3.11** (deshalb 3.11 als kleinste Minor-Version; CPython-Patch + Source-Archiv sind im Manifest gepinnt) |
| **GPU/Treiber** | NVIDIA-Treiber + CUDA-fähige GPU sind **Host-Voraussetzungen** für Device-Operationen – **nicht** vom Paket/Python-Wheel/NuGet installierbar. |
| **Keine per-Method-C#-Bindings** | Bewusste Design-Entscheidung: Dynamic Reflection (Discovery + generischer Invoke), **keine** statisch typisierte C#-Methode pro Python-Member. |
| **Latency** | Der Python.NET-Bridge bietet breitere Abdeckung, aber **keine** Low-Latency-Garantie (Native Hot Path = Zukunft). |
| **Netzwerk** | Build & Pack brauchen Netzwerk (pinne CPython + PyPI); die deployte Anwendung **nicht**. |

---

*Erstellt als progressive Dokumentation: oben kompakt/nutzerorientiert, unten technisch/detailliert. Bei Fragen: `README.md`, `PLAN.md` und die Code-Kommentare konsultieren.*
