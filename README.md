# JSAGROAllegroSync

> 💼 **Commercial Project** — Part of a private or client-facing initiative.

## Overview

JSAGROAllegroSync is a Windows Service that automates the management of product listings on Allegro. It fetches products from a supplier database via API, updates the local database, enriches products with category suggestions, parameters, and images, and synchronizes listings with an Allegro account.

The project also includes a WPF-based configurator, enabling administrators to monitor logs, review daily warnings and errors, and manage service settings effortlessly.

## Key Features

- **Automated Product Management:** Fetch products from supplier API and update the local database.
- **Category and Parameter Enrichment:** Retrieve suggested Allegro categories, category parameters, and applicable product values.
- **Image Management:** Upload product images to Allegro automatically.
- **Offer Synchronization:** Update existing Allegro offers or create new offers based on database products.
- **WPF Configurator:**
  - Real-time log monitoring with daily summary of warnings and errors.
  - Edit all service configuration parameters from a user-friendly interface.
- **Robust Logging:** Track all service actions and database updates.

## Screenshots

### Configurator - Log View

![Configurator Log](./Screenshots/log_view.png)

### Configurator - Settings

![Configurator Settings](./Screenshots/settings_view.png)

## Technologies Used

- **Frameworks:** .NET Framework, WPF
- **Languages:** C#
- **Technologies:** REST API, SQL Server, Allegro API

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

This project is proprietary and confidential. See the [LICENSE](LICENSE) file for more information.

---

© 2025-present [calKU0](https://github.com/calKU0)
