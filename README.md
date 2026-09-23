# TransportOrders

A small WPF desktop application for managing internal transport orders in an aluminium extrusion plant
(billets to the presses, profiles to the aging oven, packing and warehouse).
Built as a WPF warm-up project.

The single window lists all orders in a read-only grid, filters them by status and by order number
(case-insensitive, both filters combined), and moves the selected order through its lifecycle
with **Start**, **Complete** and **Cancel**. Every change is saved to SQLite immediately.
The status bar shows the total and per-status counts of all stored orders, independent of the filter.

![Main window](docs/screenshot.png)

## Using the window

- **Filter** by status with the segmented control and **search** by order number (case-insensitive); both combine.
- Select an order: the hint line tells you which actions its status allows, and only those buttons are enabled.
- **Cancel** asks for confirmation. When nothing matches, a *Clear filters* button resets filter and search.
- A save or load error shows a message; after a failed save the list is reloaded from the database.

| Shortcut | Action |
| --- | --- |
| `Ctrl+F` | Focus the search box |
| `Esc` (in search) | Clear the search |
| `F5` | Reload orders from the database |
| `Alt+S` / `Alt+C` / `Alt+N` | Start / Complete / Cancel the selected order |

## Technologies

.NET 8, C#, WPF, CommunityToolkit.Mvvm, Entity Framework Core 8 with SQLite,
Microsoft.Extensions.DependencyInjection, xUnit.

## Solution structure

| Project | Content |
| --- | --- |
| `src/TransportOrders.Core` | Domain: `TransportOrder`, `OrderStatus`, transition rules, `IOrderRepository`. No WPF, EF Core or SQLite reference. |
| `src/TransportOrders.Data` | `AppDbContext`, EF Core repository, database creation and demo data. References Core. |
| `src/TransportOrders.App` | WPF app: `MainWindow`, `MainViewModel`, DI setup in `App.xaml.cs`. References Core and Data. |
| `tests/TransportOrders.Tests` | xUnit tests for the business rules. References Core only. |

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

## Data storage

The SQLite database lives in `%LOCALAPPDATA%\TransportOrders\transportorders.db`
(the folder is created on start-up, whatever the working directory).
The schema is created with `EnsureCreated()` (no migrations). The 12 demo orders
(`TO-2026-0001` to `TO-2026-0012`) are inserted only when the database contains no order,
so restarting never duplicates them or resets status changes.
Delete the file to start again from the demo data.

Quantities are expressed in pieces.

## Status transitions

```
Planned ──Start──▶ InTransit ──Complete──▶ Done
   │                   │
   └──────Cancel───────┴──────────────────▶ Cancelled
```

- Allowed: `Planned → InTransit`, `InTransit → Done`, `Planned → Cancelled`, `InTransit → Cancelled`.
- `Done` and `Cancelled` are final; a transition to the current status is also rejected.
- Invalid transitions throw an `InvalidStatusTransitionException` naming both statuses;
  a successful transition updates `UpdatedAt` (UTC).
- The rules live in `TransportOrder.TransitionTo` / `CanTransitionTo`; the view model only calls them,
  and the buttons are enabled through the commands' `CanExecute`.
