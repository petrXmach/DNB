# Analysis of `main.cpp` (DNCors_IEC104)

## Overview

`main.cpp` defines the entry point and the main application class `cl_MainApp` for the `DNCors_IEC104` application. This application appears to be a dual-purpose Windows application that can run as a **Windows Service** or a command-line utility for installing/uninstalling the service.

Its primary function acts as a gateway or server, accepting incoming TCP connections and managing multiple IEC 104 client connections (`cl_104_Client`). It uses the **wxWidgets** library for application structure, networking, and threading.

## Key Classes and Functions

### `cl_MainApp`

The central application class, inheriting from `wxApp`.

#### Member Variables

- `m_Config` (`cl_Config`): Handles loading and storing application configuration.
- `m_uServer` (`std::unique_ptr<wxSocketServer>`): The TCP server socket that listens for incoming local client connections.
- `m_Clients` (`std::list<cl_Client_UPtr>`): A list of currently connected local clients (`cl_Client`).
- `m_104_Clients` (`std::list<cl_104_Client_UPtr>`): A list of connections to external IEC 104 devices.
- `m_u104_Connect_Thrd` (`std::unique_ptr<std::thread>`): A thread handle for the connection manager backend.
- `m_Connect_104Srv` (`cl_ConnectServer`): Worker object that manages the connection logic in the separate thread.
- `STATIC` `SERVER_ID`, `SOCKET_ID`: Event IDs for socket handling.

#### Key Methods

- `OnInit()`:
  - Initializes configuration (`m_Config.Open`, `m_Config.Read`).
  - Sets up logging.
  - Parses command line arguments (`--svc`, `--install`, `--uninstall`).
  - **Service Mode (`--svc`)**:
    - Creates and binds `m_uServer` to listen on `TCP_COMM_PORT`.
    - Sets up event handlers for socket events (`OnServerEvent`, `OnSocketEvent`).
    - Calls `Init_Servers()` to load IEC 104 configurations.
    - Starts the connection thread (`m_u104_Connect_Thrd`).
    - Registers as a running service with the Service Control Manager (SCM).
- `OnExit()`:
  - Stops threads and cleans up resources.
  - Notifies SCM that the service is stopped.
- `Init_Servers()`:
  - Scans the `Server/` subdirectory.
  - For each folder found, looks for `IP.txt` and `XChng.cfg`.
  - Creates a `cl_104_Client` object for each valid server found and adds it to `m_104_Clients`.
- `OnServerEvent(wxSocketEvent&)`:
  - Handles `wxSOCKET_CONNECTION`.
  - Accepts new incoming connections.
  - Creates a `cl_Client` instance and adds it to `m_Clients`.
- `OnSocketEvent(wxSocketEvent&)`:
  - Handles I/O events for connected clients.
  - Delegates data reception to `cl_Client::Receive()`.
  - Handles disconnection (`wxSOCKET_LOST`).
- `Service Functionality` (`install`, `uninstall`, `isInstalled`, `ServiceMain`, `ServiceCtrlHandler`):
  - Standard Windows Service boilerplate APIs to register/monitor the service.

### Global Functions

- `WinMain(...)`:
  - The standard Windows application entry point.
  - Checks for `--svc` flag.
  - If present, calls `StartServiceCtrlDispatcher` to run as a service.
  - If not, calls `wxEntry` to initialize wxWidgets (which calls `OnInit`).

## Interactions and Dependencies

### Internal Modules

- **`cl_Config`**: Helper class for parsing `DNCors_IEC104.ini` (or similar) configuration files using `wxFileConfig`.
- **`Log`**: Uses a custom logging wrapper (`log()`, `init_logger()`, `close_logger()`).

### External / Project Modules

- **`cl_Client`**: Represents a "local" client that connects to this service. The `main` app acts as a factory for these, accepting connections and managing the lifecycle.
  - _Called by_: `OnServerEvent` (creation), `OnSocketEvent` (data flow).
- **`cl_104_Client`**: Represents a connection to a remote IEC 104 device (RTU/Server).
  - _Called by_: `Init_Servers` (creation/config), `main` app holds a list of these.
- **`cl_ConnectServer`**: Responsible for the active connection logic (presumably attempting reconnections to the IEC 104 devices) running in a background thread.
- **`wxWidgets`**: Heavily relies on `wxSocketServer`, `wxSocketBase`, `wxThreadEvent`, `wxConfig`, `wxLog`.

## Logic Flow Summary

1.  **Startup**: Checks arguments. If `--svc`, starts as service.
2.  **Initialization**: Reads config. Sets up listening socket (`m_uServer`).
3.  **Discovery**: Scans disk for "Server" definitions (folders with IP/Config files) -> creates `cl_104_Client` objects.
4.  **Connection Loop**: Starts a background thread (`cl_ConnectServer::Run`) to manage the IEC 104 connections.
5.  **Runtime**:
    - Listens for local clients. When one connects, a `cl_Client` is spawned to handle that session.
    - Routes data between the local clients (`cl_Client`) and the IEC 104 devices (`cl_104_Client`) (implied by structure, though specific routing logic is likely in `cl_Client` or `cl_104_Client`).
