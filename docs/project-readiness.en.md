# Project readiness and remaining work

Assessment date: 12 October 2026. Scope: the current local source tree and the deployed `recipes-api` service. Effort estimates below are planning judgments, not measured completion percentages or delivery commitments.

Release follow-up, 20 October 2026: implementation source and tests are now tracked
in Git; only generated `output/` files were untracked when this work began.
The [release build procedure](release-baseline.en.md) now provides a shared local/CI
verification and packaging script with revision metadata and package checksums.
The untracked-source findings below describe the earlier assessment. Remote CI
and clean-checkout release acceptance still require a successful run of the
updated workflow on the committed revision.

## Current state

TransportOrders has a working aluminium-extrusion simulation: physical baskets, locations and reservations; scheduling; oven phases and versioned recipes; packaging and quality; package shipping and empty-basket reuse; durable simulated PLC exchanges; alarms; operator roles; traceability; backups and crash recovery. The Windows service runs independently of WPF. WPF uses authenticated API reads and commands. EN/DE interfaces, EN/DE/FR documentation and a German guided demo video are available.

The installed service was checked during this review: Demo mode, active runtime, no reported runtime error, three recipes, twenty orders, ten packages and no alarms in the observed snapshot. These observations are point-in-time diagnostics, not a long-duration acceptance test.

The full solution test suite was rerun during this assessment: **338 passed, zero failed and zero skipped** (89 domain, 58 desktop and 191 integration tests), with compiler warnings treated as errors. Results are recorded in `artifacts/project-analysis-tests.log`.

The architecture separates Core, Data, API and WPF responsibilities. Persistent command identities, immutable cycle parameter copies, audit history and version checks provide a useful foundation for further integration. The only implemented PLC channel is `SqlitePlcSimulator`; startup explicitly rejects Live mode. Commissioning is a read-only preparation profile.

## Remaining work for a polished simulation release

| Priority | Work package | Acceptance criterion | Estimated developer days |
| --- | --- | --- | --- |
| 1 | Consolidate the release baseline | Review the accumulated changes and untracked source/tests, establish a versioned Git baseline, verify CI from a clean checkout, and produce reproducible API/client packages. | 2–3 |
| 2 | Add repeatable scenario controls | Supervisor can select/restart an isolated demonstration, pause/resume the central simulated runtime, and reproduce documented fault/quality/PLC scenarios without replacing the active installation database manually. | 3–5 |
| 3 | Verify multiple workstations and real accounts | Two clients demonstrate Windows authentication, each role, stale-version conflicts, reconnect behaviour and correct installation identity. Network and certificate setup is documented. | 2–4 |
| 4 | Complete operational testing | Run extended simulation and larger-data tests; exercise backup restoration and upgrade recovery; document observed limits and address failures or growth bottlenecks. | 2–5 |
| 5 | Finish usability and documentation consistency | Check laptop layouts and German texts, reconcile historical guides with current behaviour, and simplify first installation and update instructions. | 1–3 |

Total: **10–20 developer days**, approximately **2–4 full-time developer weeks**, assuming the same developer, the present feature scope and ready access to a second workstation. Waiting for equipment/accounts and the elapsed duration of soak tests can extend the calendar schedule. These are proposed release improvements; the current simulation can already be demonstrated.

Concrete issues behind these packages:

- Many implemented features, migrations and tests remain untracked in Git. Existing local executables are usable, but a fresh checkout does not contain the complete current implementation.
- The current Wi-Fi profile is Public; the configured TransportOrders HTTPS firewall rule applies only to Private. Successful HTTPS on this PC does not prove access from a second workstation.
- Only the current real Windows account is configured as Supervisor in the examined installation. Automated role tests do not replace verification with distinct operating-system accounts.
- Desktop pause/closure does not stop the central service. There is no dedicated central pause/resume or scenario-reset UI.
- `GetPlantStateAsync` loads all orders, cycles, PLC commands and associated histories. Clients poll the whole snapshot. This warrants volume measurement, paging/archiving or incremental updates if data volume grows; replacing SQLite alone would not resolve full-history transfers.
- The plant layout and several routes/resources are fixed demonstration definitions. A general site configuration editor is not implemented.
- `Install-WindowsService.ps1` requires PowerShell 7 for its current `Path.IsPathFullyQualified` call. Existing-service deployment is operational.
- Some older specialized notes describe earlier implementation stages and test counts. The consolidated handbook and current functional audit provide the more recent baseline.

## Optional simulation expansion

An ERP simulator/order import, editable plant layouts and routing, stock accounting, temperature-history charts, label printing and broader reporting are separate feature choices. They are not needed to demonstrate the current agreed basket/oven/packing scenario. A selected combination would add several developer weeks; the scope must be agreed before combining these with the release estimate.

French documentation is complete for the current handbook edition; French application localization is an additional feature, not part of the current EN/DE interface requirement. SQL Server is also a deployment choice requiring provider-specific migration and transaction tests, rather than an automatic prerequisite for the present demonstration.

## Work before a real-equipment pilot

1. Obtain machine descriptions, PLC brands/protocols, network endpoints, signal maps, cycle handshakes, approved recipes, interlocks, initial positions and responsibility for manual reconciliation.
2. Separate simulator execution from live driver selection, implement the selected driver, validate payloads and acknowledgement sequencing, and define safe behaviour during disconnects and restarts with the automation engineer.
3. Configure the actual site layout/routes and resource capacities, reconcile physical inventory, and integrate real oven measurements and machine states where required.
4. Add the required production interfaces: order/ERP import, inventory, printer/scanner integration and reporting. The current general-purpose order creation/import workflow is absent; orders are generated by the simulated process and basket reloads.
5. Complete deployment/security arrangements, backup and restore drills, failure procedures, load tests, operator training and staged on-site acceptance.

A limited pilot should provisionally be planned as **another 3–6 months of coordinated software and automation work once machine data and test access are available**. This is a broad planning range; number of machines, protocol, ERP scope and acceptance obligations can substantially change it. A multi-site commercial system, high availability and complete parity with another industrial product require a separate requirements and delivery plan.

Recommended next action while remaining in simulation: consolidate a reproducible release baseline, then implement the isolated scenario controller and central simulation pause/resume controls.
