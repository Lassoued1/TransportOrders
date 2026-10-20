# TransportOrders - User manual

Edition 1.1 | 12 October 2026 | English

## 1. Before you begin

TransportOrders follows aluminium-extrusion baskets from press outlets through aging ovens and packing. Approved packages move to shipping; empty baskets return for reuse. The current Demo mode simulates equipment, oven time and PLC responses. It is not connected to a real machine. Commissioning mode is read-only and creates no demo data.

This manual describes the latest `recipes-client` and matching API. If the service has not been upgraded, newer functions may be unavailable. The application supports English and German. This English manual and its German/French translations do not add a French interface.

You need the running central service and a Windows account recognized by it. Start the client from `scripts/Start-NetworkClient.ps1` for the configured DORNBIRNTATA HTTPS server, or use the executable with the connection supplied by the administrator. On a remote workstation, the server certificate must be trusted. No database file is needed on that workstation.

## 2. Start, language and access

1. Start the client and wait for the first refresh.
2. Check the displayed Windows identity/roles and central-service status.
3. Choose **English** or **Deutsch** in the language selector. The selection is remembered per user.
4. If the connection is unavailable, displayed data may be old and commands are disabled. Restore the connection, then review the refreshed state before acting.

| Role | Main actions |
| --- | --- |
| Quality | Inspect packages; hold and release orders. |
| Logistics | Reload baskets; cancel orders; dispatch and confirm transport arrival. |
| Maintenance | Equipment settings; PLC diagnosis/retry/reconciliation; acknowledge alarms. |
| Supervisor | All actions, including demo steps. |
| No effective permissions | Consultation only. |

A disabled button can mean missing rights, wrong material state, an action already running or read-only Commissioning mode. Roles do not override process constraints. Request rights from the administrator; the application has no self-service role editor.

## 3. Navigation and reading the plant

| English tab | German tab | What to inspect |
| --- | --- | --- |
| Plant overview | Anlagenübersicht | Stations, capacity, baskets, equipment and oven cycles. |
| Packing & basket reuse | Verpackung & Korbrücklauf | Package checks, label text and empty basket reload. |
| Production schedule | Produktionsplanung | Next eligible action and reasons for blocked work. |
| PLC exchanges | SPS-Kommunikation | Command status, retries, deadlines and history. |
| Alarms | Alarme | Active/cleared occurrences and acknowledgements. |
| Oven recipes | Ofenrezepte | Drafts, simulation validation, revisions and archive. |
| Orders & traceability | Aufträge & Rückverfolgung | Order state, history, load planning and batch traceability. |

A reserved destination is an incoming allocation, not proof that the basket is already there. A moving basket occupies the shared transfer resource. Check the basket identifier, material lot and current load sequence together: reuse gives the same basket a new load. A package retains the identity of the load from which it was made.

## 4. Running the demonstration

Only use these controls in Demo mode. **Next step** executes one eligible simulated action when the central runtime is not already running. If it owns automatic execution, the step request reports that the central service is driving the demo. Closing or pausing the desktop does not stop that independent service.

For a demonstration controlled by the desktop, **Run demo** requests steps periodically; stop it before editing controls or performing manual quality work. To stop an independent service runtime for a supervised test, ask the administrator. Do not interpret desktop pause as a machine stop.

The example route is press -> oven -> packing. Heating, holding and cooling must complete before material can leave the oven. Simulated temperatures are not actual measurements. In the seeded example, held and cancelled orders do not proceed normally. Releasing the held order permits its subsequent processing; a cancelled order is final.

## 5. Equipment and ovens

1. In Plant overview, select the intended equipment.
2. Review mode, condition, current operation and oven phase.
3. If authorized, choose automatic/manual and operational/fault/maintenance as appropriate for the demo.
4. Enter a meaningful reason and choose **Apply control**.
5. Review the refreshed status and history.

Fault, maintenance or manual mode can block automatic processing. Restore operational/automatic status only after reviewing the situation. The application preserves cycle progress after restart; it does not add treatment time while the service was stopped.

In Oven recipes, a Supervisor creates a recipe with an immutable identifier, four-digit alloy, temperature and positive heating/holding/cooling durations. Enter a reason and save a draft. Select the saved draft, enter a review reason and validate it for simulation. Only validated recipes can start new cycles. Editing creates another draft revision that requires validation again. Save or reload unsaved parameter edits before validation or archiving. Archived recipes remain in history and cannot be edited. Running cycles retain their original recipe revision and parameters. History records the Windows account, time, reason and full parameter snapshot. A conflict requires reloading the selected recipe before retrying; polling preserves unsaved inputs.

If a version conflict is reported, your selection changed on the server. Refresh the selected control/version, review the latest state and enter the intended action again. Do not repeatedly press Apply to force the old request through.

## 6. Quality, packing and basket reuse

For manual package inspection, stop desktop automatic requests first. Select a package awaiting inspection or rejected after an earlier inspection. Check the visual and quantity results, enter a reason and choose **Record inspection**. Both checks must pass for approval. A rejection prevents label approval and shipping; a rejected package requires explicit reinspection after corrective work. Automatic checks in Demo mode are simulated and do not replace a physical inspection.

An approved package displays label text with its traceability information. This is not an integrated label printer or graphical barcode generator. The package can proceed to shipping independently of the empty basket.

To reload, select an empty basket that has completed its return to a press, choose an available lot and target oven, and enter quantity, weight and loading length. Choose **Reload basket** and inspect the new load sequence and order. Values are demo input, without material-stock reservation. A previous oven cycle never validates the newly reloaded basket.

## 7. Orders, dispatch and traceability

Use Orders & traceability when your role enables it. Search or scan an order number into the barcode field; review the selected order and its history. Trace the batch to see related movements; genealogy groups the associated material identities.

Quality hold requires a reason and excludes the order from planning. Release only after the quality review. Cancellation is final. **Plan loads** calculates proposals; it does not dispatch them by itself. Review route, length and weight before dispatch. A full load needs at least 90% of available loading length. A permitted partial-load reason must be recorded; an empty or overloaded transport is refused.

An arrival confirmation applies to the actual movement and its loaded orders. Manual arrival is a recorded physical reconciliation. In an eventual real installation, use it only after confirming the actual position through the approved procedure. It is not proof that the PLC supplied an acknowledgement.

Useful shortcuts: F5 refresh; Ctrl+F focuses search; Esc in search clears it. Refreshed data can reveal actions committed by another operator or the runtime.

## 8. PLC diagnosis

Pending means queued; AwaitingAck means sent; Accepted does not mean arrived. Completed means a completion was received, possibly waiting for equipment availability. Applied means the arrival is committed. TimedOut and Rejected require review.

In Demo mode, authorized maintenance staff can suppress responses or disconnect the simulated channel. A timeout is 15 seconds and retains the destination reservation. Restore responses to test a late acknowledgement. Retry is explicit, requires a reason, uses the same command identifier and is limited to three send attempts. A rejected command needs review, not a blind repeat. Manual reconciliation requires a reason and is separately traced.

If no PLC is configured, the interface says so. These settings operate the simulator; they do not connect to a physical PLC.

## 9. Alarms and acknowledgement

Select an occurrence in Alarms. Review the source, condition, active/cleared state, timestamp and history. Maintenance or Supervisor can enter a review comment and acknowledge. Acknowledgement records the Windows user and comment; it neither repairs the fault nor completes a transport. An alarm may remain active after acknowledgement. A recurring condition creates a new, unacknowledged occurrence.

The selected draft version and comment are kept during polling. If acknowledgement conflicts, review the current occurrence and use **Refresh selected version** before trying the revised action. If the service version does not supply alarms or the connection is lost, acknowledgement is disabled. Service/runtime or backup errors can appear here once the corresponding feature is enabled.

## 10. Problems, shutdown and handover

| Symptom | Recommended action |
| --- | --- |
| Connection unavailable | Check server address/service with the administrator; treat displayed data as potentially old. |
| Access denied / disabled command | Check identity, effective role, operation state and Commissioning mode. |
| State conflict | Refresh and review history; another action or runtime step may have changed the selection. |
| No movement | Read schedule blocking reasons; check holds, capacity, compatible treatment and equipment condition. |
| PLC timeout | Keep reservations; review connection and history before a justified retry/reconciliation. |
| Reply lost after submitting | Refresh first: the server may already have committed the action. |
| Backup alarm | Inform administration; check the last successful backup before maintenance. |

Close WPF normally when finished. The independent runtime may continue. Before handover, report active unacknowledged alarms, held/rejected material, pending PLC commands and unresolved manual actions. Administrative backup/restore procedures belong in the technical reference; operators must not delete or replace the database to reset the plant.
