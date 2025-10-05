# JSAGROSyncServices

> 💼 **Commercial Project** — part of a private or client-facing initiative.

## Overview

**JSAGROSyncServices** is a set of Windows Services designed for the JSAGRO client, automating product and offer synchronization across multiple platforms.  
Each service is independent and handles a specific data source or target system, ensuring reliable data transfer and robust logging.

## Services

### JSAGROSyncServices.GaskaToAllegro

- Fetches products from the **Gaska API** (JSON format)
- Enriches products with Allegro categories, parameters, and images
- Creates new offers or updates existing ones in **Allegro**
- Handles error reporting and daily summary logs

### JSAGROSyncServices.AllegroToErli

- Fetches product listings from **Allegro**
- Maps Allegro data to the Erli schema
- Synchronizes products into **Erli**
- Maintains logs of all synchronization actions and errors

## Features

### GaskaToAllegro

- Automated product fetching and enrichment
- Allegro offer creation and updates
- Image management and upload
- Real-time and daily summary logging
- Configurable API and service settings

### AllegroToErli

- Allegro product retrieval and transformation
- Reliable data mapping to Erli
- Incremental synchronization with database logging
- Error tracking and notification
- Configurable service parameters

## Screenshots

### Configurator - Log View

![Configurator Log](./Screenshots/log_view.png)

### Configurator - Settings

![Configurator Settings](./Screenshots/settings_view.png)

## Technologies Used

- **Frameworks:** .NET Framework
- **Languages:** C#
- **Data Sources & Targets:** REST APIs (Gaska, Allegro, Erli)
- **Database:** SQL Server
- **Logging:** Serilog

## Installation & Setup

1. Clone the repository:
   ```bash
   git clone https://github.com/calKU0/JSAGROAllegroSync.git
   ```
2. Open the solution in Visual Studio and build the project.
3. Configure the service using the WPF configurator or by editing `app.config`.
4. Install the Windows Service via PowerShell or `sc.exe`.
5. Start the service and verify logs for successful operation.

## License

This project is licensed under the [MIT License](LICENSE).

---

© 2025-present [calKU0](https://github.com/calKU0)
