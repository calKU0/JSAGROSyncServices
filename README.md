# JSAGROSyncServices

> Commercial project - part of a private or client-facing initiative.

## Overview

**JSAGROSyncServices** is a collection of Windows worker services and a WPF configurator that automate product and order synchronization between JSAGRO data sources (Gaska/Rolmar) and marketplace platforms (Allegro, Erli). Each service runs independently, focuses on a single integration flow, and ships with structured logging.

## Solution Layout

### Worker Services (`net10.0`)

- `Allegro.JSAGRO.Gaska.ProductsService` - synchronizes Gaska/JSAGRO products into Allegro.
- `Allegro.JSAGRO.Gaska.OrdersService` - synchronizes Allegro orders into JSAGRO/Gaska flows.
- `Allegro.JSAGRO.Rolmar.ProductsService` - synchronizes Rolmar products into Allegro.
- `Allegro.JSAGRO2.Gaska.ProductsService` - JSAGRO2 variant of the Gaska product sync.
- `Allegro.JSAGRO2.Gaska.OrdersService` - JSAGRO2 variant of the order sync.
- `Allegro.JSAGRO2.Rolmar.ProductsService` - JSAGRO2 variant of the Rolmar product sync.
- `Allegro.Erli.ProductsService` - synchronizes Allegro offers into Erli.

### Shared Libraries

- `JSAGROSyncServices.Shared` - shared models, helpers, and SQL Server migrations.

### Desktop Tooling (`net8.0-windows`)

- `ServiceManager` - WPF configurator and service monitor for runtime settings and log viewing.

## Features

- Product catalog and offer synchronization
- Order import workflows
- Image processing and uploads
- SQL Server-backed state and migrations
- Serilog-based structured logging

## Screenshots

### Configurator - Log View

![Configurator Log](./Screenshots/log_view.png)

### Configurator - Settings

![Configurator Settings](./Screenshots/settings_view.png)

## Technologies Used

- **Frameworks:** .NET 10 Worker Service, .NET 8 WPF
- **Language:** C#
- **Data Sources & Targets:** REST APIs (Gaska, Allegro, Erli)
- **Database:** SQL Server
- **Data Access:** Dapper
- **Logging:** Serilog

## License

This project is licensed under the [MIT License](LICENSE).

---

© 2025-present [calKU0](https://github.com/calKU0)
