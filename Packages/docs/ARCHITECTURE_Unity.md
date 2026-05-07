# Unity CLI Loop Unity Editor-Side Architecture

## 1. Overview

This document details the architecture of the C# code within the `Packages/src/Editor` directory. This code runs inside the Unity Editor and serves as the bridge between the Unity environment and the external `uloop` CLI.

### System Architecture Overview

```mermaid
graph TB
    subgraph "1. CLI Caller"
        Agent[AI Agent or Developer Shell]
        CLI[uloop CLI]
    end
    
    subgraph "2. Unity Editor (Project IPC Server)"
        MB[McpBridgeServer<br/>Project IPC Server<br/>McpBridgeServer.cs]
        CMD[Tool System<br/>UnityApiHandler.cs]
        UI[UnityCliLoopSettingsWindow<br/>GUI<br/>UnityCliLoopSettingsWindow.cs]
        API[Unity APIs]
        SM[McpSessionManager<br/>McpSessionManager.cs]
    end
    
    Agent -->|executes command| CLI
    CLI <-->|Project IPC JSON-RPC| MB
    MB <--> CMD
    CMD <--> API
    MB --> SM
    
    classDef client fill:#e1f5fe,stroke:#01579b,stroke-width:2px
    classDef server fill:#f3e5f5,stroke:#4a148c,stroke-width:2px
    classDef bridge fill:#fff3e0,stroke:#e65100,stroke-width:2px
    
    class Agent,CLI client
    class MB server
```

### Client-Server Relationship Breakdown

```mermaid
graph LR
    subgraph "Communication Layers"
        CLI[uloop CLI<br/>CLIENT]
        Unity[Unity Editor<br/>PROJECT IPC SERVER]
    end
    
    CLI -->|"Project IPC JSON-RPC<br/>Port: 8700-9100"| Unity
    
    classDef client fill:#e1f5fe,stroke:#01579b,stroke-width:2px
    classDef server fill:#f3e5f5,stroke:#4a148c,stroke-width:2px
    classDef hybrid fill:#fff3e0,stroke:#e65100,stroke-width:2px
    
    class CLI client
    class Unity server
```

### Protocol and Communication Details

```mermaid
sequenceDiagram
    participant Agent as AI Agent or Developer
    participant CLI as uloop CLI<br/>(CLIENT)
    participant Unity as Unity Editor<br/>(PROJECT IPC SERVER)<br/>McpBridgeServer.cs
    
    Agent->>CLI: uloop command
    CLI->>Unity: Project IPC JSON-RPC request
    Unity->>CLI: Project IPC JSON-RPC response
    CLI->>Agent: command output
    
    Note over CLI, Unity: Client-Server Roles:
    Note over CLI: CLIENT: opens short-lived command sessions
    Note over Unity: SERVER: accepts project IPC requests
```

### Communication Protocol Summary

| Component | Role | Protocol | Port | Connection Type |
|-----------|------|----------|------|----------------|
| **uloop CLI** | **CLIENT** | Project IPC JSON-RPC | 8700-9100 | Initiates Unity tool requests |
| **Unity Editor** | **SERVER** | Project IPC JSON-RPC | 8700-9100 | Accepts local project IPC connections |

### Communication Flow Details

#### Layer 1: Caller ↔ uloop CLI
- **Protocol**: Command line invocation
- **Transport**: Local process execution
- **Connection**: A caller starts `uloop` commands as needed
- **Lifecycle**: Managed by the caller process

#### Layer 2: uloop CLI ↔ Unity Editor (Project IPC Protocol)
- **Protocol**: Custom TCP with JSON-RPC 2.0
- **Transport**: TCP Socket
- **Ports**: UNITY_TCP_PORT environment variable specified port (auto-discovery)
- **Connection**: `uloop` acts as the project IPC client
- **Lifecycle**: Managed per command invocation

#### Key Architectural Points:
1. **uloop CLI is the external entry point**: Commands are translated into project IPC JSON-RPC requests.
2. **Unity Editor is the project IPC server**: Processes tool requests and executes Unity operations.
3. **Automatic Discovery**: The CLI discovers the matching Unity instance for the current project.

### TCP/JSON-RPC Communication Specification

#### Transport Layer
- **Protocol**: TCP/IP over localhost
- **Default Port**: 8700 (configurable via environment variable)
- **Message Format**: JSON-RPC 2.0 compliant
- **Message Framing**: Content-Length headers (RFC-compliant)
- **Dynamic Buffer Management**: Up to 1MB dynamic buffering capacity
- **Fragmented Message Support**: TCP fragmented message reassembly functionality

#### JSON-RPC 2.0 Message Format

**Framing Format:**
```
Content-Length: <message_size>\r\n\r\n<json_content>
```

**Request Message Example:**
```
Content-Length: 120

{
  "jsonrpc": "2.0",
  "id": 1647834567890,
  "method": "ping",
  "params": {
    "Message": "Hello Unity CLI Loop!"
  }
}
```

**Success Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1647834567890,
  "result": {
    "Message": "Unity CLI Loop bridge received: Hello Unity CLI Loop!",
    "ExecutionTimeMs": 5
  }
}
```

**Error Response:**
```json
{
  "jsonrpc": "2.0",
  "id": 1647834567890,
  "error": {
    "code": -32603,
    "message": "Tool blocked by security settings",
    "data": {
      "type": "security_blocked",
      "command": "find-gameobjects",
      "reason": "GameObject search is disabled"
    }
  }
}
```

#### Connection Lifecycle

1. **Initial Connection**
   - `uloop` CLI connects to Unity `McpBridgeServer`
   - TCP socket established on localhost:8700
   - Connection test with ping command

2. **Command Processing**
   - JSON-RPC requests processed through UnityApiHandler
   - Security validation via McpSecurityChecker
   - Tool execution through UnityCommandRegistry

3. **Connection Lifecycle**
   - Automatic reconnection on connection loss
   - Periodic health checks via ping commands
   - SafeTimer cleanup on process termination

#### Error Handling

- **SecurityBlocked**: Tool blocked by security settings
- **InternalError**: Unity internal processing errors
- **Timeout**: Network timeout (default: 2 minutes)
- **Connection Loss**: Automatic reconnection with exponential backoff

#### Security Features

- **localhost-only**: External connections blocked
- **Tool-level Security**: McpSecurityChecker validates each command
- **Configurable Access Control**: Unity Editor security settings
- **Session Management**: Client isolation and state tracking

Its primary responsibilities are:
1.  **Running a Project IPC Server (`McpBridgeServer`)**: Listens for local `uloop` CLI connections to receive tool requests.
2.  **Executing Unity Operations**: Processes received tool requests to perform actions within the Unity Editor, such as compiling the project, running tests, or retrieving logs.
3.  **Security Management**: Validates and controls tool execution through `McpSecurityChecker` to prevent unauthorized operations.
4.  **Session Management**: Maintains server session state through `McpSessionManager`.
5.  **Providing a User Interface (`UnityCliLoopSettingsWindow`)**: Offers a GUI within the Unity Editor for developers to manage the CLI setup, skills, security settings, and project IPC server state.
6.  **Managing Configuration**: Persists Unity CLI Loop settings and skill installation state for supported AI tools.

## 2. Core Architectural Principles

The architecture is built upon several key design principles to ensure robustness, extensibility, and maintainability.

### 2.1. UseCase + Tool Pattern (DDD Integration)
The system is centered around **Domain-Driven Design** integrated **UseCase + Tool Pattern**. Each action is structured according to DDD principles, with UseCase layer orchestrating business workflows and Application Service layer implementing single functions.

#### UseCase Layer (Domain Workflow Orchestration)
- **`AbstractUseCase<TSchema, TResponse>`**: Base class for all UseCases, orchestrating workflows through a single `ExecuteAsync` method
- **Concrete UseCases**: Manage complex workflows (e.g., `McpServerInitializationUseCase`, `DomainReloadRecoveryUseCase`)
- **Temporal Cohesion Separation**: Following Martin Fowler's refactoring principles, temporal cohesion is separated into UseCase classes

#### Application Service Layer (Single Function Implementation)
- **Single Responsibility Enforcement**: Each Application Service implements only one function
- **Unified Service Results**: All services return results via `ServiceResult<T>`
- **Examples**: `CompilationExecutionService`, `LogRetrievalService`, `TestExecutionService`

#### Tool Layer (CLI Command Interface)
- **`IUnityTool`**: Common interface for all tools exposed through the CLI command dispatcher
- **`AbstractUnityTool<TSchema, TResponse>`**: Provides type-safe handling of parameters and responses
- **`McpToolAttribute`**: Attribute for automatic tool registration
- **Tool Implementation**: Each tool calls UseCases to execute business logic

#### Registry and Security
- **`UnityToolRegistry`**: Central registry that discovers and holds all available tools
- **`UnityApiHandler`**: Receives tool names and parameters, looks up tools in registry and executes them
- **`McpSecurityChecker`**: Validates execution permissions based on security settings

This DDD-integrated architecture provides clear separation between business logic and infrastructure, achieving high extensibility and maintainability.

### 2.2. Security Architecture
The system implements comprehensive security controls to prevent unauthorized tool execution:

- **`McpSecurityChecker`**: Central security validation component that checks tool permissions before execution.
- **Attribute-Based Security**: Tools can be decorated with security attributes to define their execution requirements.
- **Default Deny Policy**: Unknown tools are blocked by default to prevent unauthorized operations.
- **Settings-Based Control**: Security policies can be configured through Unity Editor settings interface.

### 2.3. Session Management
The system maintains robust session management to handle client connections and state:

- **`McpSessionManager`**: Singleton session manager implemented as `ScriptableSingleton` for domain reload persistence.
- **Client State Tracking**: Maintains connection state, client identification, and session metadata.
- **Domain Reload Resilience**: Session state survives Unity domain reloads through persistent storage.
- **Reconnection Support**: Handles client reconnection scenarios gracefully.

### 2.4. DDD-Integrated System Architecture

```mermaid
classDiagram
    class AbstractUseCase {
        <<abstract>>
        +ExecuteAsync(TSchema, CancellationToken): Task~TResponse~
    }

    class CompileUseCase {
        -compilationStateService: CompilationStateValidationService
        -executionService: CompilationExecutionService
        +ExecuteAsync(CompileSchema, CancellationToken): Task~CompileResponse~
    }

    class McpServerInitializationUseCase {
        -configService: McpServerConfigurationService
        -portService: PortAllocationService
        -startupService: McpServerStartupService
        +ExecuteAsync(ServerInitializationSchema, CancellationToken): Task~ServerInitializationResponse~
    }

    class CompilationStateValidationService {
        +ValidateCompilationState(): ServiceResult~ValidationResult~
    }

    class CompilationExecutionService {
        +ExecuteCompilationAsync(bool): Task~ServiceResult~CompilationResult~~
    }

    class IUnityTool {
        <<interface>>
        +ToolName: string
        +Description: string
        +ParameterSchema: object
        +ExecuteAsync(JToken): Task~object~
    }

    class CompileTool {
        -useCase: CompileUseCase
        +ExecuteAsync(CompileSchema): Task~CompileResponse~
    }

    class McpToolAttribute {
        <<attribute>>
        +Description: string
        +DisplayDevelopmentOnly: bool
        +RequiredSecuritySetting: SecuritySettings
    }

    AbstractUseCase <|-- CompileUseCase : extends
    AbstractUseCase <|-- McpServerInitializationUseCase : extends
    CompileUseCase --> CompilationStateValidationService : uses
    CompileUseCase --> CompilationExecutionService : uses
    McpServerInitializationUseCase --> CompilationStateValidationService : uses
    IUnityTool <|.. CompileTool : implements
    CompileTool --> CompileUseCase : delegates to
    CompileTool ..|> McpToolAttribute : uses
```

### 2.5. MVP + Helper Architecture for UI

```mermaid
classDiagram
    class UnityCliLoopSettingsWindow {
        <<Presenter>>
        -model: UnityCliLoopSettingsModel
        -view: UnityCliLoopSettingsWindowUI
        -eventHandler: UnityCliLoopSettingsWindowEventHandler
        -serverOperations: McpServerOperations
        +OnEnable()
        +OnGUI()
        +OnDisable()
    }

    class UnityCliLoopSettingsModel {
        <<Model>>
        -serverPort: int
        -isServerRunning: bool
        -selectedEditor: EditorType
        +LoadState()
        +SaveState()
        +UpdateServerStatus()
    }

    class UnityCliLoopSettingsWindowUI {
        <<View>>
        +DrawServerSection(ViewData)
        +DrawConfigSection(ViewData)
        +DrawDeveloperTools(ViewData)
    }

    class UnityCliLoopSettingsWindowViewData {
        <<DTO>>
        +ServerPort: int
        +IsServerRunning: bool
        +SelectedEditor: EditorType
    }

    class UnityCliLoopSettingsWindowEventHandler {
        <<Helper>>
        +HandleEditorUpdate()
        +HandleServerEvents()
        +HandleLogUpdates()
    }

    class McpServerOperations {
        <<Helper>>
        +StartServer()
        +StopServer()
        +ValidateServerConfig()
    }

    UnityCliLoopSettingsWindow --> UnityCliLoopSettingsModel : manages state
    UnityCliLoopSettingsWindow --> UnityCliLoopSettingsWindowUI : delegates rendering
    UnityCliLoopSettingsWindow --> UnityCliLoopSettingsWindowEventHandler : delegates events
    UnityCliLoopSettingsWindow --> McpServerOperations : delegates operations
    UnityCliLoopSettingsWindowUI --> UnityCliLoopSettingsWindowViewData : receives
    UnityCliLoopSettingsModel --> UnityCliLoopSettingsWindowViewData : creates
```

### 2.6. Schema-Driven and Type-Safe Communication
To avoid manual and error-prone JSON parsing, the system uses a schema-driven approach for commands.

- **`*Schema.cs` files** (e.g., `CompileSchema.cs`, `GetLogsSchema.cs`): These classes define the expected parameters for a command using simple C# properties. Attributes like `[Description]` and default values are used to automatically generate a JSON Schema for the client.
- **`*Response.cs` files** (e.g., `CompileResponse.cs`): These define the structure of the data returned to the client.
- **`CommandParameterSchemaGenerator.cs`**: This utility uses reflection on the `*Schema.cs` files to generate the parameter schema dynamically, ensuring the C# code is the single source of truth.

This design eliminates inconsistencies between the server and client and provides strong type safety within the C# code.

### 2.7. SOLID Principles
- **Single Responsibility Principle (SRP)**: Each class has a well-defined responsibility.
    - `McpBridgeServer`: Handles raw TCP communication.
    - `McpServerController`: Manages the server's lifecycle and state across domain reloads.
    - `McpEditorSettings`: Handles Editor window preference persistence.
    - `ToolSkillSynchronizer`: Handles skill installation file updates.
    - `JsonRpcProcessor`: Deals exclusively with parsing and formatting JSON-RPC 2.0 messages.
    - **UI Layer Examples**:
        - `UnityCliLoopSettingsModel`: Manages application state and business logic only.
        - `UnityCliLoopSettingsWindowUI`: Handles UI rendering only.
        - `UnityCliLoopSettingsWindowEventHandler`: Manages Unity Editor events only.
        - `McpServerOperations`: Handles server operations only.
- **Open/Closed Principle (OCP)**: The system is open for extension but closed for modification. The Command Pattern is the prime example; new commands can be added without altering the core execution logic. The MVP + Helper pattern also demonstrates this principle - new functionality can be added by creating new helper classes without modifying existing components.

### 2.8. MVP + Helper Pattern for UI Architecture
The UI layer implements a sophisticated **MVP (Model-View-Presenter) + Helper Pattern** that evolved from a monolithic 1247-line class into a well-structured, maintainable architecture.

#### Pattern Components
- **Model (`UnityCliLoopSettingsModel`)**: Contains all application state, configuration data, and business logic. Provides methods for state updates while maintaining encapsulation. Handles persistence through Unity's `SessionState` and `EditorPrefs`.
- **View (`UnityCliLoopSettingsWindowUI`)**: Pure UI rendering component with no business logic. Receives all necessary data through `UnityCliLoopSettingsWindowViewData` transfer objects.
- **Presenter (`UnityCliLoopSettingsWindow`)**: Coordinates between Model and View, handles Unity-specific lifecycle events, and delegates complex operations to specialized helper classes.
- **Helper Classes**: Specialized components that handle specific aspects of functionality:
  - Event management (`UnityCliLoopSettingsWindowEventHandler`)
  - Server operations (`McpServerOperations`)
  - Skill installation services (`ToolSkillSynchronizer`)

#### Benefits of This Architecture
1. **Separation of Concerns**: Each component has a single, clear responsibility
2. **Testability**: Helper classes can be unit tested independently from Unity Editor context
3. **Maintainability**: Complex logic is broken down into manageable, focused components
4. **Extensibility**: New features can be added through new helper classes without modifying existing code
5. **Reduced Cognitive Load**: Developers can focus on one aspect of functionality at a time

#### Implementation Guidelines
- **State Management**: All state changes go through the Model layer
- **UI Updates**: View receives data through transfer objects, never directly accesses Model
- **Complex Operations**: Delegate to appropriate helper classes rather than implementing in Presenter
- **Event Handling**: Isolate all Unity Editor event management in dedicated EventHandler

### 2.9. Domain Reload Resilience (UseCase Integration)
A significant challenge in the Unity Editor is the "domain reload," which resets the application's state. The DDD-integrated architecture handles this gracefully at the UseCase level:

#### Domain Reload Recovery UseCase
- **`DomainReloadRecoveryUseCase`**: Orchestrates the entire domain reload workflow
- **`DomainReloadDetectionService`**: Detects and determines domain reload state
- **`SessionRecoveryService`**: Handles session state preservation and restoration

#### McpServerController Integration
- **`McpServerController`**: Uses `[InitializeOnLoad]` to hook into Editor lifecycle events
- **UseCase Invocation**: Executes UseCases in `OnBeforeAssemblyReload` and `OnAfterAssemblyReload`
- **`AssemblyReloadEvents`**: Delegates pre/post reload processing to UseCases
- **`SessionState`**: Domain reload data persistence (managed by UseCases)

#### Orchestrated Workflow
1. **Before Reload**: `DomainReloadRecoveryUseCase.ExecuteBeforeDomainReload()` saves server state
2. **After Reload**: `DomainReloadRecoveryUseCase.ExecuteAfterDomainReloadAsync()` restores state

This UseCase integration ensures domain reload processing is managed as a single business workflow, improving maintainability and reliability.

## 3. Implemented UseCases and Tools

The system currently implements 12 production-ready features using **Domain-Driven Design** architecture with **UseCase + Tool Pattern**. Each feature provides business workflow orchestration through UseCases, single-function implementation through Application Services, and CLI-facing tool commands:

### 3.1. Core System UseCases and Tools
- **`PingTool`**: Connection health check and latency testing
- **`CompileUseCase` + `CompileTool`**: Compilation state validation and execution separated by Application Services, with detailed error reporting
- **`ClearConsoleTool`**: Unity Console log clearing with confirmation
- **`GetCommandDetailsTool`**: Tool introspection and metadata retrieval

### 3.2. Information Retrieval UseCases and Tools
- **`GetLogsUseCase` + `GetLogsTool`**: Log retrieval and filtering separated by Application Services, with type selection
- **`GetHierarchyUseCase` + `GetHierarchyTool`**: Scene hierarchy information collection and export with component information
- **`GetMenuItemsUseCase` + `GetMenuItemsTool`**: Unity menu item discovery and metadata collection
- **`GetProviderDetailsUseCase` + `GetProviderDetailsTool`**: Unity Search provider information collection

### 3.3. GameObject and Scene UseCases and Tools
- **`FindGameObjectsUseCase` + `FindGameObjectsTool`**: Multi-criteria search logic orchestrated by UseCase, advanced GameObject search
- **`UnitySearchUseCase` + `UnitySearchTool`**: Unity Search API integrated unified search across assets, scenes, and project resources

### 3.4. Execution UseCases and Tools
- **`RunTestsUseCase` + `RunTestsTool`**: Test filter creation and execution separated by Application Services, NUnit XML export (security-controlled)
- **`ExecuteMenuItemUseCase` + `ExecuteMenuItemTool`**: Menu item search and execution orchestrated by UseCase, reflection-based execution (security-controlled)

### 3.5. Security-Controlled UseCases and Tools
Several UseCases and Tools are subject to security restrictions and can be disabled via settings:
- **Test Execution**: `RunTestsUseCase`/`RunTestsTool` requires "Enable Tests Execution" setting
- **Menu Item Execution**: `ExecuteMenuItemUseCase`/`ExecuteMenuItemTool` requires "Allow Menu Item Execution" setting
- **Unknown Tools**: Blocked by default unless explicitly configured

### 3.6. Server Lifecycle UseCases
- **`McpServerInitializationUseCase`**: Orchestrates complex server initialization workflow
- **`McpServerShutdownUseCase`**: Manages proper server shutdown processing
- **`DomainReloadRecoveryUseCase`**: Completely orchestrates state management before/after domain reloads

These UseCases are not directly exposed as CLI tools but are called internally by `McpServerController` to manage the system lifecycle.

## 4. Key Components (Directory Breakdown)

### `/Server`
This directory contains the core networking and lifecycle management components.
- **`McpBridgeServer.cs`**: The low-level TCP server. It listens on a specified port, accepts client connections, and handles the reading/writing of JSON data using Content-Length framing over the network stream. It operates on a background thread.
- **`FrameParser.cs`**: Specialized class for Content-Length header parsing and validation. Handles frame integrity verification and JSON content extraction.
- **`DynamicBufferManager.cs`**: Dynamic buffer pool management class. Achieves memory efficiency and buffer reuse. Supports dynamic buffering up to 1MB.
- **`MessageReassembler.cs`**: TCP fragmented message reassembly class. Properly handles partially received frames and extracts complete messages.
- **`McpServerController.cs`**: The high-level, static manager for the server. It controls the lifecycle (Start, Stop, Restart) of the `McpBridgeServer` instance. It is the central point for managing state across domain reloads.
- **`McpServerConfig.cs`**: A static class holding constants for server configuration (e.g., default port, buffer sizes).

### `/Security`
Contains the security infrastructure for command execution control.
- **`McpSecurityChecker.cs`**: Central security validation component that implements permission checking for command execution. Evaluates security attributes and settings to determine if a command should be allowed to execute.

### `/Api`
This is the heart of the command processing logic.
- **`/Commands`**: Contains the implementation of all supported commands.
    - **`/Core`**: The foundational classes for the command system.
        - **`IUnityCommand.cs`**: Defines the contract for all commands, including `CommandName`, `Description`, `ParameterSchema`, and the `ExecuteAsync` method.
        - **`AbstractUnityCommand.cs`**: The generic base class that simplifies command creation by handling the boilerplate of parameter deserialization and response creation.
        - **`UnityCommandRegistry.cs`**: Discovers all classes with the `[McpTool]` attribute and registers them in a dictionary, mapping a command name to its implementation.
        - **`McpToolAttribute.cs`**: A simple attribute used to mark a class for automatic registration as a command.
    - **Command-specific folders**: Each implemented command has its own folder containing:
        - `*Command.cs`: The main command implementation
        - `*Schema.cs`: Type-safe parameter definition
        - `*Response.cs`: Structured response format
        - Commands include: `/Compile`, `/RunTests`, `/GetLogs`, `/Ping`, `/ClearConsole`, `/FindGameObjects`, `/GetHierarchy`, `/GetMenuItems`, `/ExecuteMenuItem`, `/UnitySearch`, `/GetProviderDetails`, `/GetCommandDetails`
- **`JsonRpcProcessor.cs`**: Responsible for parsing incoming JSON strings into `JsonRpcRequest` objects and serializing response objects back into JSON strings, adhering to the JSON-RPC 2.0 specification.
- **`UnityApiHandler.cs`**: The entry point for API calls. It receives the method name and parameters from the `JsonRpcProcessor` and uses the `UnityCommandRegistry` to execute the appropriate command. Integrates with `McpSecurityChecker` for permission validation.

### `/Core`
Contains core infrastructure components for session and state management.

#### Session Management
- **`McpSessionManager.cs`**: Singleton session manager implemented as `ScriptableSingleton` that maintains server session metadata and survives domain reloads.

### `/UI`
Contains the code for the user-facing Editor Window, implemented using the **MVP (Model-View-Presenter) + Helper Pattern**.

#### Core MVP Components
- **`UnityCliLoopSettingsWindow.cs`**: The **Presenter** layer (503 lines). Acts as the coordinator between the Model and View, handling Unity-specific lifecycle events and user interactions. Delegates complex operations to specialized helper classes.
- **`UnityCliLoopSettingsModel.cs`**: The **Model** layer (470 lines). Manages all application state, persistence, and business logic. Contains UI state, server configuration, and provides methods for state updates with proper encapsulation.
- **`UnityCliLoopSettingsWindowUI.cs`**: The **View** layer. Handles pure UI rendering logic, completely separated from business logic. Receives data through `UnityCliLoopSettingsWindowViewData` and renders the interface.
- **`UnityCliLoopSettingsWindowViewData.cs`**: Data transfer object that carries all necessary information from the Model to the View, ensuring clean separation of concerns.

#### Specialized Helper Classes
- **`UnityCliLoopSettingsWindowEventHandler.cs`**: Manages Unity Editor events. Handles `EditorApplication.update`, `McpCommunicationLogger.OnLogUpdated`, server lifecycle events, and state change detection. Completely isolates event management logic from the main window.
- **`McpServerOperations.cs`**: Handles complex server operations (131 lines). Contains server validation, starting, and stopping logic. Supports both user-interactive and internal operation modes with comprehensive error handling.
- **`McpCommunicationLog.cs`**: Manages the in-memory and `SessionState`-backed log of requests and responses displayed in the "Developer Tools" section of the window.

#### Architectural Benefits
This MVP + Helper pattern provides:
- **Single Responsibility**: Each class has one clear, focused responsibility
- **Testability**: Helper classes can be unit tested independently
- **Maintainability**: Complex logic is separated into specialized, manageable components
- **Extensibility**: New features can be added by creating new helper classes without modifying existing code
- **Reduced Complexity**: The main Presenter went from 1247 lines to 503 lines (59% reduction) through proper responsibility distribution

### `/Config`
Manages Unity CLI Loop Editor settings, tool access settings, skill installation, and project path resolution.
- **`McpEditorSettings.cs`**: Persists Editor window state and setup preferences.
- **`ULoopSettings.cs`**: Persists CLI-related setup and security-adjacent settings.
- **`ToolSettings.cs`**: Stores per-tool access settings used by the security layer.
- **`ToolSkillSynchronizer.cs`**: Installs generated skill files into supported AI tool directories.
- **`UnityMcpPathResolver.cs`**: Resolves the Unity project root and package paths. The class name is historical.

### `/Tools`
Contains higher-level utilities that wrap core Unity Editor functionality.
- **`/ConsoleUtility` & `/ConsoleLogFetcher`**: A set of classes, primarily `ConsoleLogRetriever`, that use reflection to access Unity's internal console log entries. This allows the `getlogs` command to retrieve logs with specific types and filters.
- **`/TestRunner`**: Contains the logic for executing Unity tests.
    - **`PlayModeTestExecuter.cs`**: A key class that handles the complexity of running PlayMode tests, which involves disabling domain reloads (`DomainReloadDisableScope`) to ensure the `async` task can complete successfully.
    - **`NUnitXmlResultExporter.cs`**: Formats test results into NUnit-compatible XML files.
- **`/Util`**: General-purpose utilities.
    - **`CompileController.cs`**: Wraps the `CompilationPipeline` API to provide a simple `async` interface for compiling the project.

### `/Utils`
Contains low-level, general-purpose helper classes.
- **`MainThreadSwitcher.cs`**: A crucial utility that provides an `awaitable` object to switch execution from a background thread (like the TCP server's) back to Unity's main thread. This is essential because most Unity APIs can only be called from the main thread.
- **`EditorDelay.cs`**: A custom, `async/await`-compatible implementation of a frame-based delay, useful for waiting a few frames for the Editor to reach a stable state, especially after domain reloads.
- **`VibeLogger.cs`**: AI-friendly structured logger for Unity CLI Loop diagnostics.

## 5. Key Workflows

### 5.1. UseCase + Tool Execution Flow with Security

```mermaid
sequenceDiagram
    box CLI CLIENT
    participant CLI as uloop CLI
    end
    
    box Unity TCP SERVER
    participant MB as McpBridgeServer<br/>McpBridgeServer.cs
    participant JP as JsonRpcProcessor<br/>JsonRpcProcessor.cs
    participant UA as UnityApiHandler<br/>UnityApiHandler.cs
    participant SC as McpSecurityChecker<br/>McpSecurityChecker.cs
    participant UR as UnityToolRegistry<br/>UnityToolRegistry.cs
    participant AT as AbstractUnityTool<br/>AbstractUnityTool.cs
    participant Tool as Concrete Tool<br/>*Tool.cs
    participant UC as UseCase<br/>*UseCase.cs
    participant AS as Application Service<br/>*Service.cs
    end

    CLI->>MB: JSON String
    MB->>JP: ProcessRequest(json)
    JP->>JP: Deserialize to JsonRpcRequest
    JP->>UA: ExecuteToolAsync(name, params)
    UA->>SC: ValidateTool(name, params)
    alt Security Check Passed
        SC-->>UA: Validation Success
        UA->>UR: GetTool(name)
        UR-->>UA: IUnityTool instance
        UA->>AT: ExecuteAsync(JToken)
        AT->>AT: Deserialize to Schema
        AT->>Tool: ExecuteAsync(Schema)
        Tool->>UC: ExecuteAsync(Schema, CancellationToken)
        UC->>AS: Call Application Services
        AS->>AS: Execute single function
        AS-->>UC: ServiceResult<T>
        UC-->>Tool: UseCase Response
        Tool-->>AT: Tool Response
        AT-->>UA: Response
    else Security Check Failed
        SC-->>UA: Validation Failed
        UA-->>UA: Create Error Response
    end
    UA-->>JP: Response
    JP->>JP: Serialize to JSON
    JP-->>MB: JSON Response
    MB-->>CLI: Send Response
```

### 5.2. UI Interaction Flow (MVP + Helper Pattern)
1.  **User Interaction**: User interacts with the Unity Editor window (button clicks, field changes, etc.).
2.  **Presenter Processing**: `UnityCliLoopSettingsWindow` (Presenter) receives the Unity Editor event.
3.  **State Update**: Presenter calls appropriate method on `UnityCliLoopSettingsModel` to update application state.
4.  **Complex Operations**: For complex operations (server start/stop, validation), Presenter delegates to specialized helper classes:
    - `McpServerOperations` for server-related operations
    - `UnityCliLoopSettingsWindowEventHandler` for event management
    - `ToolSkillSynchronizer` for skill installation operations
5.  **View Data Preparation**: Model state is packaged into `UnityCliLoopSettingsWindowViewData` transfer objects.
6.  **UI Rendering**: `UnityCliLoopSettingsWindowUI` receives the transfer objects and renders the interface.
7.  **Event Propagation**: `UnityCliLoopSettingsWindowEventHandler` manages Unity Editor events and updates the Model accordingly.
8.  **Persistence**: Model automatically handles state persistence through Unity's `SessionState` and `EditorPrefs`.

This workflow ensures clean separation of concerns while maintaining responsiveness and proper state management throughout the application lifecycle.

### 5.3. Security Validation Flow

```mermaid
sequenceDiagram
    box Unity TCP SERVER
    participant UA as UnityApiHandler<br/>UnityApiHandler.cs
    participant SC as McpSecurityChecker<br/>McpSecurityChecker.cs
    participant Settings as Security Settings
    participant Command as Command Instance<br/>*Command.cs
    end
    
    UA->>SC: ValidateCommand(commandName)
    SC->>Settings: Check security policy
    alt Command is security-controlled
        Settings-->>SC: Security status
        alt Security disabled
            SC-->>UA: Validation Failed
        else Security enabled
            SC-->>UA: Validation Success
        end
    else Command is not security-controlled
        SC-->>UA: Validation Success
    end
    UA->>Command: Execute (if validated)
```
