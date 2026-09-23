# TransportOrders

[![CI](https://github.com/Lassoued1/TransportOrders/actions/workflows/ci.yml/badge.svg)](https://github.com/Lassoued1/TransportOrders/actions/workflows/ci.yml)

A WPF desktop application for managing internal transport orders in an aluminium extrusion plant
(billets to the presses, profiles to the aging oven, packing and warehouse), with a small read-only
ASP.NET Core API over the same domain and data layer. Built as a WPF warm-up project.

The window lists every order, filters them by status, order number and heat, and moves the selected
order through its lifecycle with **Start**, **Complete** and **Cancel**. Each change is saved to SQLite
immediately and recorded in the order's history (who, when, from which status).

![Main window](docs/screenshot.png)

## Features

- **Status workflow** enforced by the domain: only the actions allowed for the selected order are enabled,
  and a hint line says which ones and why. Cancelling asks for confirmation.
- **Traceability**: every order carries the heat (melt) number of its material and a full status history.
  *Trace heat* shows all orders made from the same melt, e.g. the billet and the profiles pressed from it.
- **Priorities and due times**: urgent orders are flagged, overdue open orders are shown in red.
- **Shift figures** for the current shift (Early 06–14, Late 14–22, Night 22–06): orders started and
  completed, average transit time, open, overdue and urgent orders.
- **Barcode scan field**: a scanner types the order number and Enter; the order is selected even if the
  current filters hide it.
- **English / German** UI, switchable at runtime; the choice is remembered.
- **Robust**: load and save errors show a clear message, a failed save is rolled back by reloading from
  the database, and the UI stays responsive (database work runs off the UI thread).
- **Logging** with Serilog to daily files, kept for 14 days.

![German UI](docs/screenshot-de.png)

| Shortcut | Action |
| --- | --- |
| `Ctrl+F` | Focus the search box |
| `Esc` (in search) | Clear the search |
| `F5` | Reload orders from the database |
| `Alt+S` / `Alt+C` / `Alt+N` | Start / Complete / Cancel (German UI: `Alt+S` / `Alt+A` / `Alt+O`) |

## Technologies

.NET 8, C#, WPF, CommunityToolkit.Mvvm, Entity Framework Core 8 with SQLite (migrations),
ASP.NET Core minimal APIs with Swagger, Microsoft.Extensions.DependencyInjection, Serilog, xUnit,
GitHub Actions.

## Solution structure

| Project | Content |
| --- | --- |
| `src/TransportOrders.Core` | Domain: `TransportOrder` (status rules, history, overdue), `StatusChange`, `ShiftSummary`, `IOrderRepository`. No WPF, EF Core or SQLite reference. |
| `src/TransportOrders.Data` | `AppDbContext`, EF Core repository, migrations, database initialization and demo data. References Core. |
| `src/TransportOrders.App` | WPF app: `MainWindow`, `MainViewModel` (no WPF dependency), localization, DI setup in `App.xaml.cs`. References Core and Data. |
| `src/TransportOrders.Api` | Read-only ASP.NET Core minimal API. References Core and Data. |
| `tests/TransportOrders.Tests` | Domain tests: transitions, history, heat numbers, overdue, shifts. References Core only. |
| `tests/TransportOrders.App.Tests` | `MainViewModel` tests with a fake repository, clock and dialogs. |
| `tests/TransportOrders.IntegrationTests` | Repository and API tests against real temporary SQLite files. |

## Prerequisites

- Windows 10 or 11
- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Build, test and run

```bash
dotnet restore TransportOrders.sln
dotnet build TransportOrders.sln
dotnet test TransportOrders.sln
dotnet run --project src/TransportOrders.App
```

Run the API (Swagger UI at http://localhost:5080/swagger):

```bash
dotnet run --project src/TransportOrders.Api
```

## API

Read-only on purpose: status changes stay in the desktop app, where the domain rules are applied,
so the API offers no way around them. Enums are returned as text.

| Endpoint | Returns |
| --- | --- |
| `GET /api/orders?status=&heat=&overdue=` | Orders, optionally filtered (`400` for an unknown status) |
| `GET /api/orders/{id}` | One order with its history (`404` if unknown) |
| `GET /api/orders/{id}/history` | The order's audit trail |
| `GET /api/orders/by-number/{orderNumber}` | Case-insensitive lookup, e.g. from a scanned barcode |
| `GET /api/shift-summary` | Figures of the current shift in the plant time zone (`Plant:TimeZone`) |
| `GET /health` | Health check |

```bash
curl "http://localhost:5080/api/orders?status=InTransit&overdue=true"
```

## Data storage

- SQLite database: `%LOCALAPPDATA%\TransportOrders\transportorders.db`, shared by the app and the API
  (override the API's path with `Database:Path`). The folder is created on start-up.
- The schema is managed with **EF Core migrations**, applied at start-up. A database created by
  version 1 (with `EnsureCreated`) is kept as `transportorders.db.v1-<timestamp>.bak` and replaced.
- The 12 demo orders (`TO-2026-0001` to `TO-2026-0012`) are inserted only when the database contains
  no order, with times relative to the first launch, so restarting never duplicates them or resets changes.
  Delete the file to start again from the demo data.
- Also in that folder: `settings.json` (UI language) and `logs\app-<date>.log`.
- Quantities are expressed in pieces.

To add a migration: `dotnet tool restore`, then
`dotnet ef migrations add <Name> --project src/TransportOrders.Data --startup-project src/TransportOrders.Data`.

## Status transitions

```
Planned ──Start──▶ InTransit ──Complete──▶ Done
   │                   │
   └──────Cancel───────┴──────────────────▶ Cancelled
```

- Allowed: `Planned → InTransit`, `InTransit → Done`, `Planned → Cancelled`, `InTransit → Cancelled`.
- `Done` and `Cancelled` are final; a transition to the current status is also rejected.
- Invalid transitions throw an `InvalidStatusTransitionException` naming both statuses;
  a successful transition updates `UpdatedAt` (UTC) and adds a history entry with the user.
- The rules live in `TransportOrder.TransitionTo` / `CanTransitionTo`; the view model only calls them,
  and the buttons are enabled through the commands' `CanExecute`.

## Possible next steps

- **ERP/MES integration** (e.g. SAP): orders would come from the ERP through a service behind
  `IOrderRepository` or a write endpoint that calls the same domain methods.
- **Several users at once**: SQL Server instead of SQLite, optimistic concurrency with a row version,
  and a conflict message when two people change the same order.
- **Machine events** via OPC UA, e.g. marking an order *Done* when the press confirms receipt.
- **Authentication** for the API and roles in the app (planner, driver, supervisor).
