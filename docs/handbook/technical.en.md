# TransportOrders - Technical reference

Edition 1.1 | 12 October 2026 | English

## 1. Scope and release baseline

This reference describes the current source code and the `recipes-api` / `recipes-client` packages. A running service may use an older package; inspect its executable path before assuming that alarms or backups are enabled. The project is an independent aluminium-extrusion material-flow demonstrator. It has no certified compatibility with MCVIS.

Implemented: a persistent physical plant model, transport scheduling, simulated oven treatment, packing and basket reuse, durable simulated PLC exchanges, Windows-authenticated operator commands, EN/DE WPF clients, roles, alarms, online backups and a read-only commissioning profile. A real PLC driver, approved process recipes, ERP integration and integrated barcode/label printing are not implemented. A barcode input field and printable label text do exist. French is a documentation language, not an application language.

The validated baseline is 338 tests: 89 domain, 58 desktop and 191 integration tests. A test result describes the tested source version, not acceptance of a physical installation.

## 2. Architecture and responsibilities

| Component | Responsibility |
| --- | --- |
| Core | Entities, transitions, constraints, scheduler, PLC protocol model and alarm detection; no WPF or SQLite reference. |
| Data | EF Core / SQLite repositories, migrations, demo initialization, PLC inbox/outbox and online backup. |
| API | ASP.NET Core service, Windows authentication, authorization, serialized writes and background runtime, backup and alarm workers. |
| App | .NET 8 WPF client using API snapshots and commands; references Core, not Data. |

The client reads `/api/client-state` approximately every two seconds, except while its main operation is busy. It never opens the server database. `PlantConnection` holds one server URL and pins its `PlantId`; `RemotePlantRepository` permits reads and rejects local writes. Windows default credentials are used. Automatic HTTP redirects and certificate-validation bypasses are not enabled.

`PlantOperationGate` serializes operator changes, runtime actions and alarm synchronization inside one API process. `PlantDriverLease` holds `<database>.driver.lock` for the runtime lifetime, preventing two automatic schedulers for the same local database. A remaining lock file alone does not mean that a process owns the lease. This is local-process coordination, not a distributed cluster lock.

## 3. Domain model and invariants

| Entity | Key purpose / invariant |
| --- | --- |
| Basket / MaterialLot / PlantLocation | Physical identity, load contents, position and destination reservation; position is not reconstructed from the latest order on every refresh. |
| TransportOrder / Transport | One material movement leg and its loaded transport; history records identity, time and reason. |
| PlantEquipment | Mode, condition, current operation and version. Fault, maintenance or manual mode can prevent automatic actions. |
| OvenRecipe / OvenCycle / OvenCycleLoad | Alloy compatibility and a recipe snapshot for the basket load sequence being treated. |
| PackedPackage / BasketReturn | Package identity and quality status remain independent of the reusable basket and its return movement. |
| PlcCommand / PlcInbox / PlcSimulatorExecutions | Durable command, validated received events and deduplicated simulated execution. |
| PlantAlarm / AlarmEvent | One condition occurrence, its clearance, acknowledgement and audit trail. |

A basket reload increments `ContentSequence`. An old treatment or package cannot validate the new load. One current basket load contains one material lot and produces one package; splitting and mixing are not supported. Dispatch checks source position, destination capacity, reservations, equipment and applicable oven compatibility. Arrival commits the physical update and command application atomically.

Legacy field names remain: `HeatNumber` carries the material batch, `PieceNumber` the basket identity, `ParentPieceNumber` its billet batch and `BasketContentSequence` the load sequence. Weight is in tonnes, loading length in metres, temperature in degrees Celsius and cycle time in simulated minutes. Persisted operational timestamps use UTC; shifts use `Plant:TimeZone`, while alarm dates are displayed in the client's local Windows time.

Transport loading length must reach 90% for a full load. Dispatch may never exceed length or payload capacity, depart empty or combine different routes. A partial load requires a recorded permitted reason. Order states are `Planned`, `OnHold`, `InTransit`, `Done`, `Cancelled`. Dispatch is the route into `InTransit`; `Done` and `Cancelled` are final.

Oven phases are heating, holding, cooling and completed. Cycles copy the recipe parameters and basket load identities at start. An event advances one simulated minute; shutdown time does not advance treatment. Temperatures are interpolated demo values, not sensor readings. Packing visual/quantity checks gate approval, label text and shipping. Demo automatic QA is simulated.

Recipe management: GET /api/recipes and POST /api/recipes/{id}/create, revise, validate or archive. Writes require the Supervisor role, installation identity, a reason and the expected recipe version for existing recipes. The shared operation gate and EF concurrency token prevent stale writes. RecipeEvents retain full parameter snapshots; cycles persist RecipeRevision. Migration 20261012172347_VersionedOvenRecipes retains existing recipes as validated revision 1 and imports their baseline once. No recipe is deleted. Parameters remain illustrative simulation values.

## 4. PLC exchanges and concurrency

The command and movement reservation are saved together. Each command carries a stable GUID, contract version, equipment, operation, source, target, immutable payload and payload hash. The simulator stores execution by command GUID. A retry uses the same GUID and payload.

| State | Meaning |
| --- | --- |
| Pending | Queued, not yet sent. |
| AwaitingAck | A send attempt was recorded; a response is awaited. |
| Accepted | Acceptance received; arrival has not been proven. |
| Completed | Completion received; physical application may still be deferred. |
| Applied | Arrival and acknowledgement were committed together. |
| TimedOut | Response deadline expired; reservation is retained. |
| Rejected | PLC rejection; operator review is required. |

The timeout is 15 seconds and the maximum is three send attempts. Timeout does not automatically resend. Duplicate event IDs, mismatched route/hash/equipment and stale sequences are checked. A late valid response may recover a timed-out command without a resend. A fault can defer physical application of a stored completion. Manual arrival is a separately audited reconciliation, not a fabricated machine acknowledgement.

GUID entity versions and `OrderRevision` protect against stale clients. The order revision is a SHA-256 representation of relevant order state and history. `ExpectedPlantId` protects commands against a different installation; the legacy database-path check remains for older command clients. A lost response may follow a committed action: refresh state and history before deciding on another action.

## 5. Identity, roles and operating modes

Negotiate authenticates the Windows principal. The server derives the actor from that principal, never from a JSON username. Business reads are authenticated; `/health` is anonymous HTTP liveness, not proof of healthy production.

| Role | Authorized changes |
| --- | --- |
| Quality | Package inspection; order hold/release. |
| Logistics | Basket reload; cancellation; dispatch and manual transport arrival. |
| Maintenance | Equipment control; PLC settings, retries and reconciliation; alarm acknowledgement. |
| Supervisor | All operator commands, including demo steps. |

Roles can be combined. `Access:Roles` maps existing users/groups; it does not create accounts or groups. With `UseRoles=true`, legacy allowlists do not add rights. Alarm acknowledgement uses `equipment.control`, including legacy equipment authorization where retained. An authenticated account with no permissions can consult only. Losing access disables client commands; it does not stop an independent central runtime.

`Plant:Mode=Demo` is the default. `Commissioning` requires explicit `Database:Path` and `Plant:Id`, rejects an enabled runtime, initializes schema without demo data and refuses modifying HTTP requests. Permissions returned to the client are empty. `Live` and unknown modes are rejected: physical operation is not implemented. Commissioning must use a separately reviewed database/profile.

## 6. API contract

JSON uses text enums. Snapshot contract version is 1; optional alarm and operating-mode fields extend it. Standard outcomes include 400 invalid input, 401 unauthenticated, 403 unauthorized/read-only, 404 unknown reference and 409 stale state, wrong installation or blocked operation.

| Read endpoint | Purpose |
| --- | --- |
| /api/client-state | Physical snapshot, access, runtime, alarms and operating mode. |
| /api/access; /api/runtime; /api/backups/status | Effective rights, runtime diagnostics, backup outcome. |
| /api/orders; /api/orders/{id}; /api/orders/{id}/history | Orders and audit trail; numeric ID for these item routes. |
| /api/orders/by-number/{number}; /api/transports; /api/load-plan | Number lookup, movements and calculated proposals. |
| /api/plant; /api/schedule; /api/plc | Plant state, scheduled candidates and durable exchanges. |
| /api/packages/{number}/label | Approved label text. |
| /api/heats/{heatNumber}/genealogy; /api/shift-summary | Material genealogy and shift figures. |

POST commands: `/api/equipment/{id}/control`, `/api/packages/{id}/inspection`, `/api/baskets/{id}/reload`, `/api/orders/{id}/state` (order number), `/api/transports/dispatch`, `/api/transports/{id}/manual-arrival`, `/api/plc/simulation/settings`, `/api/plc/commands/{id}/retry`, `/api/plc/commands/{id}/manual-arrival`, `/api/simulation/step`, `/api/alarms/{id}/acknowledge`. Commands require the applicable role, current version/revision and installation identity; reasons are required where the operation demands them. Refer to the request records in Core and API for exact payloads. Swagger is enabled in Development only.

## 7. Installation and configuration

Build requirements: Windows and .NET 8 SDK. Published `win-x64 --self-contained true` packages include their runtime. The installed service is `TransportOrders.Runtime`, normally under `NT AUTHORITY\LocalService`, with delayed automatic startup and configured recovery restarts. Service changes require elevated Windows rights.

Use PowerShell 7 for the initial `Install-WindowsService.ps1`: it calls `Path.IsPathFullyQualified`, which Windows PowerShell 5.1 does not provide. Tested update/diagnostic scripts can use Windows PowerShell 5.1. For a fresh demo, create the chosen database directory, start the API once with an explicit `--Database:Path`, then stop it with Ctrl+C after initialization. Do not point this demo initialization at an unreviewed operational database.

```powershell
dotnet build TransportOrders.sln
dotnet test TransportOrders.sln
dotnet publish src/TransportOrders.Api -c Release -r win-x64 --self-contained true -o artifacts/recipes-api
dotnet publish src/TransportOrders.App -c Release -r win-x64 --self-contained true -o artifacts/recipes-client
```

For an already installed service, use `scripts/Deploy-EquipmentApi.ps1` with the operator, role and published path. It preserves configuration and attempts to revert the executable path if readiness fails. It does not roll back database migrations. Take a verified backup before an upgrade. Initial installation requires an existing initialized database; use `Install-WindowsService.ps1` after reviewing the intended path.

```powershell
# Elevated shell; replace the example account with the intended Windows identity.
.\scripts\Deploy-EquipmentApi.ps1 -OperatorName 'DORNBIRNTATA\moham' -Role Supervisor -PublishPath '.\artifacts\recipes-api'
.\scripts\Check-PlantBackup.ps1
```

| Setting | Meaning |
| --- | --- |
| Database:Path | Explicit absolute server database path for Windows service operation. |
| Plant:Id / Name / Mode / TimeZone | Stable installation identity, display name, mode and shift time zone. |
| Runtime:Enabled / LogDirectory | Enable simulated runtime and choose server logs. |
| Access:UseRoles / Roles | Windows user/group assignments. |
| Backup:Enabled / Directory / IntervalHours | Startup backup plus periodic backups; interval 1-168 hours, default 24. |
| Kestrel:Endpoints | Local HTTP and remote HTTPS bindings/certificate. |

Client settings: `TRANSPORTORDERS_SERVICE_URL` (default `http://localhost:5080`) and optional `TRANSPORTORDERS_PLANT_ID` to pin the installation before first contact. Remote addresses require HTTPS. Certificates must be trusted and valid for the server name. LocalService needs read access to its specific private key. Demo HTTPS uses DORNBIRNTATA:5443; its firewall rule is restricted to Private profile/local subnet. No network-profile change is automatic. A second workstation still requires certificate trust, working Windows authentication and a connectivity test.

## 8. Storage, alarms, backup and recovery

The configured installed database is `C:\ProgramData\TransportOrders\extrusion.db`; development fallback paths are chosen by `DatabasePaths`, not by WPF. The client stores language preferences and daily logs in `%LOCALAPPDATA%\TransportOrders.Client`. Server logs are daily, with 14 retained files by default. Protect the database, configuration, binaries, private keys and backup directory with Windows permissions.

Alarm occurrences are deduplicated while active. Acknowledgement records actor/comment and never clears the condition. Clearance and subsequent recurrence create distinct audit events/occurrences. Runtime or backup errors are visible when their workers report them. An unreachable service is indicated locally by WPF; it cannot record its own outage in an unreachable database.

Online backup uses the SQLite backup API, includes committed WAL data and produces a standalone `.db` plus `.db.json` manifest. Verification checks SHA-256, size, installation identity, SQLite integrity, foreign keys and supported migration sequence. Backups run at startup and every configured interval; old backups are retained without automatic pruning. Copy them to separate storage for protection against disk loss.

`Backup-Plant.ps1` supports backup, verify and `restore-copy`. Restore requires an unused destination without existing SQLite journals; it does not replace the active database. Examine a restored copy with runtime disabled and reconcile physical positions before selecting it for operation. Migration compatibility is not universal: historical legacy migrations include demo resets, and old `EnsureCreated` databases have special handling. Do not treat arbitrary operational legacy databases as supported imports.

## 9. Verification and maintenance

Run the solution tests, `Check-PlantBackup.ps1` and the isolated `Test-RuntimeRecovery.ps1`. The recovery script uses its own database and port 5099, exercises abrupt restarts, then checks one send attempt and one arrival application. It never stops the installed service. `New-CommissioningProfile.ps1` prepares an empty read-only profile on localhost:5096.

Useful diagnosis: inspect service status/path, authenticated `/api/runtime`, `/api/backups/status`, client/server logs, `PlantId`, certificate expiry and effective role mappings. Error 1053 is a service startup failure to diagnose from configuration, executable access, database access and logs; raising a timeout is not a diagnosis.

Source entry points: `Api/Program.cs`, `PlantRuntimeWorker`, `PlantAlarmWorker`, `PlantBackupWorker`, Core `PlantCommands`, `PlcExchange`, `ProductionSchedule`, Data `AppDbContext`, repositories and `DatabaseBackup`, App `PlantConnection` and view models. See the repository handbook index for specialized guides. Physical PLC acceptance, approved oven recipes, ERP interfaces, printer integration and long-running on-site acceptance remain separate work.
