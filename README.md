<div align="center">

# DroneFlasher

**A fast, reliable Windows desktop utility for flashing STM32-based flight controllers, ExpressLRS receivers, and rapidly restoring CLI configurations via USB.**

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011-blue.svg?style=flat-square)](#)
[![Framework](https://img.shields.io/badge/.NET-10.0-512BD4.svg?style=flat-square)](#)
[![License](https://img.shields.io/badge/License-MIT-green.svg?style=flat-square)](#)

</div>

## 📝 Overview

**DroneFlasher** is a lightweight, C# (WPF) alternative to heavy drone configurators. It allows you to easily update device firmware using the native STM32 DFU Bootloader mode, and seamlessly restore flight controller configurations (dumps/diffs) without dealing with manual CLI copy-pasting.

## ✨ Key Features

- **Dual Operation Modes:** 
  - *Manual Mode:* Standard on-demand execution for single devices.
  - *Automatic Mode:* Continuous USB tracking for batch processing. Simply plug in a device, let it process automatically, unplug, and connect the next one.
- **Configuration Restoring:** Apply CLI backups (dumps or diffs) directly over the serial port. The app utilizes `batch` mode for fast transmission, automatically filters out redundant commands (like `reboot` or duplicate `save`), and provides real-time error tracking.
- **Automatic DFU Transition:** Automatically reboots devices from virtual COM port mode (Betaflight/INAV) into the STM32 DFU bootloader (`#` and `bl` commands).
- **Format Support:** Built-in custom parser exclusively for `.hex` firmware files to ensure precise memory address mapping.
- **Full Flashing Cycle:** Safely handles interface claiming, status clearing (CLRSTATUS), Mass Erase, chunked memory writing, and automatic device reboot.
- **Truly Portable (Single Executable):** Packaged as a single standalone `.exe` file with embedded native USB libraries. No installations or `.dll` clutter required.

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

1. Download the latest `DroneFlasher.exe` from the [Releases](#) page.
2. Run it directly from anywhere on your PC (no installation required!).

### Building from Source

The project is pre-configured to embed all native libraries directly into the output file. To build the standalone executable yourself, run this command in the project root:

```bash
dotnet publish -c Release -r win-x64 -p:PublishSingleFile=true --self-contained true
```

## 📖 Usage Guide

DroneFlasher handles both firmware updates and configuration restoration. You can choose how the application interacts with your devices based on your workflow:

### 1. Choose Your Workflow (Auto vs. Manual)
- **Automatic Mode (Batch Processing):** Select this mode and load your target file *before* plugging anything in. DroneFlasher will actively listen for USB connections. Once you plug a device in, it processes it immediately. When finished, unplug it and connect the next board — perfect for configuring multiple drones in a row.
- **Manual Mode:** Connect your device first, let it appear in the port list, select your file, and manually click the Start button.

### 2. Flashing Firmware (.hex)
1. Select your `.hex` firmware file.
2. If the device is running normally, DroneFlasher will automatically reboot it into DFU mode. If it's already in DFU mode (via the physical `BOOT` button), it is detected instantly.
3. The application will perform a Mass Erase, write the new memory blocks, and reboot the flight controller.

### 3. Restoring Configuration (Dump / Diff)
1. Select your text-based configuration backup file (e.g., `dump.txt`).
2. DroneFlasher connects to the active COM port and executes the commands.
3. It automatically wraps the process in `batch start` / `batch end` for faster writing, ignores invalid structural commands, logs any CLI syntax errors, and finalizes the setup with a `save` command.

## ⚠️ Troubleshooting

| Issue | Solution |
| :--- | :--- |
| **Device not found (DFU Timeout)** | Try a different USB cable (ensure it has data lines, not charging-only). Check Device Manager to verify the STM32 Bootloader is using the **WinUSB** driver. |
| **Flashing stops or fails mid-way** | The STM32 bootloader is stored in ROM and cannot be bricked via USB. Simply hold the physical `BOOT` button on your board, reconnect the USB, and try again. |
| **CLI Errors during Configuration** | Check the log window. The app isolates and displays specific commands that the flight controller rejected (e.g., due to firmware version mismatches). |

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.
