# Central simulation pause and resume

In **Plant overview**, enter a reason in the central simulation field and choose
**Pause central simulation** or **Resume central simulation**. A configured
Supervisor account is required. The control is disabled when the service is
unreachable, the runtime is disabled or stopped, or the account lacks permission.
The status distinguishes a paused simulation from a stopped service. All connected
workstations refresh that status with their normal polling.

Pause waits for the current simulation event to finish. After the pause response,
no new automatic scheduler event or simulated PLC processing starts. The service
keeps ownership of automatic scheduling; desktop Run demo and Next step cannot
bypass the pause. Explicit authorized operator actions, reads, alarm monitoring
and backups remain available. This is a simulation control, not a physical machine
stop. Closing WPF does not pause the service.

Pause state is stored in SQLite and survives service restarts. Resume continues
the persisted scenario; it does not reset positions or advance oven treatment by
the elapsed shutdown or pause time. Existing databases receive an additive
`CentralRuntimeControl` migration, initially unpaused, retaining their prior
automatic behavior. Commissioning remains read-only.

The authenticated API exposes:

| Route | Behavior |
| --- | --- |
| `GET /api/runtime` | Includes `paused` and the current control `version`; `running` means the runtime owns scheduling, including while paused. |
| `POST /api/runtime/control` | Requires `simulation.runtime`, granted only to configured Supervisors, including Supervisor groups. Legacy operator allowlists do not grant it. |
| `GET /api/runtime/history` | Latest 100 control events, newest first, with UTC time, authenticated actor, reason, resulting paused state and version. |

Example POST body (read the plant ID and version from the same service first):

```json
{
  "expectedPlantId": "your-installation-id",
  "expectedVersion": "current-control-version-guid",
  "expectedDatabasePath": "",
  "paused": true,
  "reason": "Prepare the operator demonstration"
}
```

Missing/invalid versions and blank or overlong reasons return 400. A wrong plant,
stale version or unavailable runtime returns 409. Refresh before retrying a
conflict; if a reason was already entered, clear and re-enter it to start a fresh
control draft. Requests do not assert an actor. State and audit events commit
atomically. Repeating the already-current state with its current version creates
no duplicate event; older versions still conflict. The full audit is retained in
the database; the history endpoint limits its response to 100 entries.
