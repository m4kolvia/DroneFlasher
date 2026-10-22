> [!NOTE]
> 🧪 **Status:** UI, theme customization, and config handling (Dump/Diff) are fully completed. The firmware flashing module is currently undergoing practical hardware testing.

<div align="center">
  <h1>DroneFlasher</h1>
  <img
    src="https://github.com/user-attachments/assets/0dfcea53-07e5-4a13-b9b5-e77664630f9c"
    width="64"
    alt="DroneFlasher Logo"
  />

  <p>
    <b>A Windows desktop application designed to simplify drone firmware management and device configuration.</b>
  </p>
</div>

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg?style=flat-square)](#)
[![Framework](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square)](#)
[![License](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](#)

</div>

## 📝 Overview

<p align="justify">
  <strong>DroneFlasher</strong> is a lightweight C# (WPF) alternative to bulky drone configurators. It simplifies firmware updates via the native STM32 DFU Bootloader and makes restoring flight controller configurations (dumps/diffs) easy, without manual CLI copy-pasting.
</p>


## ✨ Key Features

- **Dual Operation Modes**
  - **Manual Mode:** Flash and configure individual devices on demand.
  - **Automatic Mode:** Detects USB device connections and processes devices sequentially, enabling convenient batch operations.

- **Configuration Restoration**  
  Restore flight controller configurations from CLI backups (`.dump` / `.diff`) directly over the serial port. Uses Betaflight CLI batch mode for efficient transmission, filters redundant commands such as `reboot` and duplicate `save` instructions, and tracks errors in real time.

- **Automatic DFU Transition**  
  Automatically reboots compatible flight controllers from virtual COM port mode (Betaflight/INAV) into the STM32 DFU bootloader using the `#` and `bl` commands.

- **Intel HEX Firmware Support**  
  Supports `.hex` firmware files with a custom parser for accurate memory address mapping.

- **Complete Flashing Workflow**  
  Handles USB interface claiming, status clearing (`CLRSTATUS`), mass erase, chunked firmware writing, and automatic device reboot.

- **Portable Single-File Executable**  
  Distributed as a standalone `.exe` with embedded native USB libraries. No installation required and no separate `.dll` files to manage.


## 🖼️ Screenshots

> **Note:** Add a screenshot of your application here.
> 
> `![App Screenshot](assets/screenshot.png)`

## 🛠 Tech Stack

- **Language:** C#
- **Framework:** .NET 10.0 (WPF / MVVM)
- **USB Communication:** [LibUsbDotNet](https://github.com/LibUsbDotNet/LibUsbDotNet) (libusb-1.0)

## 🚀 Getting Started

### Prerequisites

- **OS:** Windows 10 or Windows 11.
- **Drivers:** A **WinUSB** driver is required for the STM32 BOOTLOADER (`0483:DF11`). If your device isn't recognized, use [Zadig](https://zadig.akeo.ie/) to replace the STMicroelectronics driver with WinUSB.

### Installation

Download the version that best fits your system from the
[Releases](#) page.

#### Option 1: Framework-Dependent (Smaller File)

- Download the smaller executable.
- Requires the compatible .NET 10 Desktop Runtime to be installed.
- Uses less disk space.
- If the application fails to start, install the [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/10.0).

#### Option 2: Self-Contained (Standalone)

- Download the standalone executable.
- Includes the .NET Runtime required by the application.
- Does not require a separate .NET Runtime installation.
- The executable is larger, but is more convenient for clean Windows computers.

> **Note:** Both versions require any necessary device drivers and native dependencies to be available. Self-contained deployment does not automatically eliminate every external dependency.

### Building from Source

Run the appropriate command from the project root.

**Option 1 — Smaller executable:**

```bash
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false
```
**Option 2 — Self-contained executable:**

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
```

## 🚀 Getting Started

### 1. Choose Your Workflow

**Automatic Mode — Batch Processing**

1. Select **Automatic Mode**.
2. Load the firmware or configuration file before connecting a device.
3. Connect a flight controller. DroneFlasher will detect and process it automatically.
4. Once processing is complete, disconnect the device and connect the next one.

Ideal for configuring multiple devices in sequence.

**Manual Mode — On-Demand Processing**

1. Connect your device via USB.
2. Wait for it to appear in the available port list.
3. Select the firmware or configuration file.
4. Click **Start** to begin processing.

### 2. Flashing Firmware (`.hex`)

1. Select the target `.hex` firmware file.
2. Connect your flight controller:
   - **Normal mode:** DroneFlasher attempts to reboot the device into DFU mode automatically.
   - **DFU mode:** If the device is already in DFU mode, for example via the physical `BOOT` button, it will be detected directly.
3. Wait for the flashing process to complete. DroneFlasher performs a mass erase, writes the firmware to the appropriate memory addresses, and reboots the device.

### 3. Restoring Configuration (`Dump` / `Diff`)

1. Select your configuration backup file, such as `dump.txt` or a `.diff` file.
2. Connect your flight controller and ensure its virtual COM port is available.
3. Start the process and let DroneFlasher execute the configuration commands.
4. The application uses `batch start` / `batch end` to streamline command execution, filters unsupported structural commands, and reports CLI errors in the log.
5. The process is finalized with a `save` command to apply the configuration.

---

## 🛠️ Troubleshooting

| Issue | Possible Solution |
|:------|:------------------|
| **Device not detected (DFU timeout)** | Try a different USB cable that supports data transfer. In Windows Device Manager, check that the STM32 Bootloader is using a compatible **WinUSB** driver. |
| **Flashing fails or stops midway** | Check the USB connection and retry. If necessary, disconnect the board, hold the physical `BOOT` button while reconnecting USB, and start the process again. |
| **CLI errors during configuration** | Review the application log to identify rejected commands. Errors may occur when commands are incompatible with the installed firmware version. |

---

> **Tip:** For reliable results, use a data-capable USB cable and ensure that the selected firmware or configuration file matches your flight controller.


## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
