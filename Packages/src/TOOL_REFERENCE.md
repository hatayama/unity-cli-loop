[日本語](TOOL_REFERENCE_ja.md)

# uLoopMCP Tool Reference

This document provides detailed specifications for all uLoopMCP tools.

## Common Response Format

All Unity MCP tools share the following common elements:

### Common Response Properties
All tools automatically include the following property:
- `Ver` (string): uLoopMCP server version for CLI compatibility check

---

## Unity Core Tools

### 1. compile
- **Description**: Executes compilation after AssetDatabase.Refresh(). Returns compilation results with detailed timing information.
- **Parameters**:
  - `ForceRecompile` (boolean): Whether to perform forced recompilation (default: false)
  - `WaitForDomainReload` (boolean): Whether to wait for domain reload completion before returning (default: false)
- **Response**:
  - `Success` (boolean | null): Whether compilation was successful. Null when ForceRecompile=true because results are unavailable until domain reload completes
  - `ErrorCount` (number | null): Total number of errors. Null when ForceRecompile=true
  - `WarningCount` (number | null): Total number of warnings. Null when ForceRecompile=true
  - `Errors` (array | null): Array of compilation errors. Null when ForceRecompile=true
    - `Message` (string): Error message
    - `File` (string): File path where error occurred
    - `Line` (number): Line number where error occurred
  - `Warnings` (array | null): Array of compilation warnings. Null when ForceRecompile=true
    - `Message` (string): Warning message
    - `File` (string): File path where warning occurred
    - `Line` (number): Line number where warning occurred
  - `Message` (string): Optional message for additional information
  - `ProjectRoot` (string): Unity project root path. Set only when WaitForDomainReload=true

### 2. get-logs
- **Description**: Retrieves log information from Unity console with filtering and advanced search capabilities
- **Parameters**:
  - `LogType` (enum): Log type to filter - "Error", "Warning", "Log", "All" (default: "All")
  - `MaxCount` (number): Maximum number of logs to retrieve (default: 100)
  - `SearchText` (string): Text to search within log messages (retrieve all if empty) (default: "")
  - `UseRegex` (boolean): Whether to use regular expression for search (default: false)
  - `SearchInStackTrace` (boolean): Whether to search within stack trace as well (default: false)
  - `IncludeStackTrace` (boolean): Whether to display stack traces (default: false)
- **Response**:
  - `TotalCount` (number): Total number of logs available
  - `DisplayedCount` (number): Number of logs displayed in this response
  - `LogType` (string): Log type filter used
  - `MaxCount` (number): Maximum count limit used
  - `SearchText` (string): Search text filter used
  - `IncludeStackTrace` (boolean): Whether stack trace was included
  - `Logs` (array): Array of log entries
    - `Type` (string): Log type (Error, Warning, Log)
    - `Message` (string): Log message
    - `StackTrace` (string): Stack trace (if IncludeStackTrace is true)

### 3. run-tests
- **Description**: Executes Unity Test Runner and retrieves test results with comprehensive reporting
- **Parameters**:
  - `FilterType` (enum): Type of test filter - "all"(0), "exact"(1), "regex"(2), "assembly"(3) (default: "all")
  - `FilterValue` (string): Filter value (specify when FilterType is other than all) (default: "")
    - `exact`: Individual test method name (exact match) (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs)
    - `regex`: Class name or namespace (regex pattern) (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests, io.github.hatayama.uLoopMCP)
    - `assembly`: Assembly name (e.g.: uLoopMCP.Tests.Editor)
  - `TestMode` (enum): Test mode - "EditMode"(0), "PlayMode"(1) (default: "EditMode")
    - **PlayMode Warning**: During PlayMode test execution, domain reload is temporarily disabled
- **Response**:
  - `Success` (boolean): Whether test execution was successful
  - `Message` (string): Test execution message
  - `CompletedAt` (string): Test execution completion timestamp (ISO format)
  - `TestCount` (number): Total number of tests executed
  - `PassedCount` (number): Number of passed tests
  - `FailedCount` (number): Number of failed tests
  - `SkippedCount` (number): Number of skipped tests
  - `XmlPath` (string): NUnit XML result file path (auto-saved when tests fail). XML files are saved to `{project root}/.uloop/outputs/TestResults/` folder

### 4. clear-console
- **Description**: Clears Unity console logs for clean development workflow
- **Parameters**:
  - `AddConfirmationMessage` (boolean): Whether to add a confirmation log message after clearing (default: false)
- **Response**:
  - `Success` (boolean): Whether the console clear operation was successful
  - `ClearedLogCount` (number): Number of logs that were cleared from the console
  - `ClearedCounts` (object): Breakdown of cleared logs by type
    - `ErrorCount` (number): Number of error logs that were cleared
    - `WarningCount` (number): Number of warning logs that were cleared
    - `LogCount` (number): Number of info logs that were cleared
  - `Message` (string): Message describing the clear operation result
  - `ErrorMessage` (string): Error message if the operation failed

### 5. find-game-objects
- **Description**: Find multiple GameObjects with advanced search criteria (component type, tag, layer, etc.)
- **Parameters**:
  - `NamePattern` (string): GameObject name pattern to search for (default: "")
  - `SearchMode` (enum): Search mode - "Exact", "Path", "Regex", "Contains", "Selected" (default: "Exact")
  - `RequiredComponents` (array): Array of component type names that GameObjects must have (default: [])
  - `Tag` (string): Tag filter (default: "")
  - `Layer` (number): Layer filter (default: null)
  - `IncludeInactive` (boolean): Whether to include inactive GameObjects (default: false)
  - `MaxResults` (number): Maximum number of results to return (default: 20)
  - `IncludeInheritedProperties` (boolean): Whether to include inherited properties (default: false)
- **Response**:
  - `results` (array): Array of found GameObjects
    - `name` (string): GameObject name
    - `path` (string): Full hierarchy path
    - `isActive` (boolean): Whether the GameObject is active
    - `tag` (string): GameObject tag
    - `layer` (number): GameObject layer
    - `components` (array): Array of components on the GameObject
      - `type` (string): Component type name
      - `fullTypeName` (string): Full assembly qualified type name
      - `properties` (array): Component properties (if IncludeInheritedProperties is true)
  - `totalFound` (number): Total number of GameObjects found
  - `errorMessage` (string): Error message if search failed
  - `resultsFilePath` (string): File path where results were saved (when results are exported to file)
  - `message` (string): Operation message
  - `processingErrors` (array): Array of objects that failed to serialize
    - `gameObjectName` (string): Name of the GameObject that failed
    - `gameObjectPath` (string): Path of the GameObject that failed
    - `error` (string): Error description

---

## Unity Search & Discovery Tools

### 6. get-hierarchy
- **Description**: Get Unity Hierarchy structure in nested JSON format for AI-friendly processing
- **Parameters**:
  - `IncludeInactive` (boolean): Whether to include inactive GameObjects in the hierarchy result (default: true)
  - `MaxDepth` (number): Maximum depth to traverse the hierarchy (-1 for unlimited depth) (default: -1)
  - `RootPath` (string): Root GameObject path to start hierarchy traversal from (empty/null for all root objects) (default: null)
  - `IncludeComponents` (boolean): Whether to include component information for each GameObject in the hierarchy (default: true)
  - `IncludePaths` (boolean): Whether to include path information for nodes (default: false)
  - `UseComponentsLut` (string): Use LUT for components - "auto", "true", "false" (default: "auto")
  - `UseSelection` (boolean): Whether to use currently selected GameObject(s) as root(s). When true, RootPath is ignored (default: false)
- **Response**:
  - `message` (string): Human-readable guidance for clients to locate and read the JSON file
  - `hierarchyFilePath` (string): File path where hierarchy data was saved (e.g., "{project_root}/.uloop/outputs/HierarchyResults/hierarchy_2025-07-10_21-30-15.json"). The exported JSON file contains `hierarchy` (nested array of GameObjects) and `context` (scene info, node count, max depth)

### 7. execute-dynamic-code
- **Description**: Execute C# code dynamically within Unity Editor. Implements security levels and automatic using statement processing with enhanced error messaging
- **Parameters**:
  - `Code` (string): The C# code to execute (default: "")
  - `Parameters` (Dictionary<string, object>): Runtime parameters for execution (default: {})
  - `CompileOnly` (boolean): Only compile, do not execute (default: false)
- **Response**:
  - `Success` (boolean): Whether execution was successful
  - `Result` (string): Execution result
  - `Logs` (array): Array of log messages
  - `CompilationErrors` (array): Array of compilation errors (if any)
    - `Message` (string): Error message
    - `Line` (number): Line number where error occurred
    - `Column` (number): Column number where error occurred
    - `ErrorCode` (string): Compiler error code (e.g., CS0103)
    - `Hint` (string): Optional hint for resolving the error
    - `Suggestions` (array of string): Suggested fixes (e.g., add using or qualify)
    - `Context` (string): Context lines around the error with a caret pointer
    - `PointerColumn` (number): Pointer column for caret rendering (1-based)
  - `ErrorMessage` (string): Error message (if failed)
  - `SecurityLevel` (string): Current security level ("Disabled", "Restricted", "FullAccess")
  - `UpdatedCode` (string): Updated code (after applying fixes)
  - `DiagnosticsSummary` (string): Summary of diagnostics (unique count, total count, first error brief)
  - `Diagnostics` (array): Structured diagnostics for rich clients (same structure as CompilationErrors)

### 8. focus-window
- **Description**: Brings Unity Editor window to front on macOS and Windows
- **Parameters**: None
- **Response**:
  - `Success` (boolean): Whether the operation was successful
  - `Message` (string): Operation result message
  - `ErrorMessage` (string): Error message if operation failed

### 9. screenshot
- **Description**: Take a screenshot of Unity EditorWindow and save as PNG. Supports capturing any open EditorWindow by name with flexible matching modes
- **Parameters**:
  - `WindowName` (string): Window name to capture (e.g., "Game", "Scene", "Console", "Inspector", "Project", "Hierarchy") (default: "Game")
  - `ResolutionScale` (number): Resolution scale for the captured image, 0.1 to 1.0 (default: 1)
  - `MatchMode` (enum): Window name matching mode (all case-insensitive) - "exact", "prefix", "contains" (default: "exact")
    - `exact`: Window name must match exactly
    - `prefix`: Window name must start with the input
    - `contains`: Window name must contain the input anywhere
  - `OutputDirectory` (string): Output directory path for saving screenshots. When empty, uses default path (.uloop/outputs/Screenshots/). Accepts absolute paths (default: "")
- **Response**:
  - `ScreenshotCount` (number): Number of windows captured
  - `Screenshots` (array): Array of screenshot info
    - `ImagePath` (string): Absolute path to the saved PNG file
    - `FileSizeBytes` (number): Size of the saved file in bytes
    - `Width` (number): Captured image width in pixels
    - `Height` (number): Captured image height in pixels

### 10. control-play-mode
- **Description**: Control Unity Editor play mode (play/stop/pause)
- **Parameters**:
  - `Action` (enum): Action to perform - "Play", "Stop", "Pause" (default: "Play")
    - `Play`: Start play mode (also resumes from pause)
    - `Stop`: Exit play mode and return to edit mode
    - `Pause`: Pause the game while remaining in play mode
- **Response**:
  - `IsPlaying` (boolean): Whether Unity is currently in play mode
  - `IsPaused` (boolean): Whether play mode is paused
  - `Message` (string): Description of the action performed

---

### 11. simulate-mouse-ui
- **Description**: Simulate mouse click, long-press, and drag on PlayMode UI elements via EventSystem screen coordinates. Uses EventSystem and ExecuteEvents to dispatch pointer events directly — works independently of both old and new Input System. For game logic that reads Input System (e.g. `Mouse.current.leftButton.wasPressedThisFrame`), use `simulate-mouse-input` instead
- **Parameters**:
  - `Action` (enum): Mouse action - "Click", "Drag", "DragStart", "DragMove", "DragEnd", "LongPress" (default: "Click")
    - `Click`: Click at (X, Y). Fires PointerDown → PointerUp → PointerClick
    - `LongPress`: Press and hold at (X, Y) for Duration seconds, then release. No PointerClick fired
    - `Drag`: One-shot drag from (FromX, FromY) to (X, Y). Fires BeginDrag → Drag×N → EndDrag
    - `DragStart`: Begin drag at (X, Y) and hold
    - `DragMove`: Animate from current position to (X, Y) at the specified speed
    - `DragEnd`: Animate to (X, Y), then release drag
  - `X` (number): Target X position in screen pixels, origin: top-left (default: 0)
  - `Y` (number): Target Y position in screen pixels, origin: top-left (default: 0)
  - `FromX` (number): Start X position for Drag action (default: 0)
  - `FromY` (number): Start Y position for Drag action (default: 0)
  - `DragSpeed` (number): Drag speed in pixels per second, 0 for instant (default: 2000)
  - `Duration` (number): Hold duration in seconds for LongPress action (default: 0.5)
  - `Button` (enum): Mouse button - "Left", "Right", "Middle" (default: "Left")
- **Response**:
  - `Success` (boolean): Whether the action completed successfully
  - `Message` (string): Description of the action performed
  - `Action` (string): The action that was executed
  - `HitGameObjectName` (string): Name of the UI element hit (null if none)
  - `PositionX` (number): X position used
  - `PositionY` (number): Y position used
  - `EndPositionX` (number): End X position (for Drag actions)
  - `EndPositionY` (number): End Y position (for Drag actions)

### 12. simulate-mouse-input
- **Description**: Simulate mouse input in PlayMode via Input System. Injects button clicks, mouse delta, and scroll wheel directly into `Mouse.current` for game logic that reads Input System (e.g. `wasPressedThisFrame`, `Mouse.current.delta`). Requires the Input System package and Active Input Handling set to `Input System Package (New)` or `Both` in Player Settings. For UI elements with IPointerClickHandler, use `simulate-mouse-ui` instead
- **Parameters**:
  - `Action` (enum): Mouse input action - "Click", "LongPress", "MoveDelta", "SmoothDelta", "Scroll" (default: "Click")
    - `Click`: Inject button press and release so `wasPressedThisFrame` returns true
    - `LongPress`: Inject button hold for Duration seconds
    - `MoveDelta`: Inject mouse delta one-shot (for FPS camera/look control)
    - `SmoothDelta`: Inject mouse delta smoothly over Duration seconds (human-like camera pan)
    - `Scroll`: Inject scroll wheel (for hotbar switching, zoom, etc.)
  - `X` (number): Target X position in screen pixels, origin: top-left. Used by Click and LongPress (default: 0)
  - `Y` (number): Target Y position in screen pixels, origin: top-left. Used by Click and LongPress (default: 0)
  - `Button` (enum): Mouse button - "Left", "Right", "Middle" (default: "Left"). Used by Click and LongPress
  - `Duration` (number): Hold duration for LongPress, or minimum hold time for Click. 0 = one-shot tap (default: 0)
  - `DeltaX` (number): Delta X in pixels for MoveDelta. Positive = right (default: 0)
  - `DeltaY` (number): Delta Y in pixels for MoveDelta. Positive = up (default: 0)
  - `ScrollX` (number): Horizontal scroll delta for Scroll action (default: 0)
  - `ScrollY` (number): Vertical scroll delta for Scroll action. Positive = up, typically 120 per notch (default: 0)
- **Response**:
  - `Success` (boolean): Whether the action completed successfully
  - `Message` (string): Description of the action performed
  - `Action` (string): The action that was executed
  - `Button` (string): The button used (for Click/LongPress)
  - `PositionX` (number, nullable): X position used (null for non-positional actions)
  - `PositionY` (number, nullable): Y position used (null for non-positional actions)

### 13. simulate-keyboard
- **Description**: Simulate keyboard key input in PlayMode via Input System. Supports single key taps, sustained holds, and multi-key combinations. Requires the Input System package, and Active Input Handling must be set to `Input System Package (New)` or `Both` in Player Settings. Game code must read input via Input System API (e.g. `Keyboard.current[Key.W].isPressed`), not legacy `Input.GetKey()`
- **Parameters**:
  - `Action` (enum): Keyboard action - "Press", "KeyDown", "KeyUp" (default: "Press")
    - `Press`: One-shot key tap (KeyDown then KeyUp). Use `Duration` for timed holds
    - `KeyDown`: Hold key down until explicitly released with KeyUp
    - `KeyUp`: Release a key currently held by KeyDown
  - `Key` (string): Key name matching the Input System Key enum (e.g. "W", "Space", "LeftShift", "Enter"). Case-insensitive
  - `Duration` (number): Hold duration in seconds for Press action, 0 for one-shot tap (default: 0). Ignored by KeyDown/KeyUp
- **Response**:
  - `Success` (boolean): Whether the action completed successfully
  - `Message` (string): Description of the action performed
  - `Action` (string): The action that was executed
  - `KeyName` (string, nullable): Name of the key that was acted upon

---

## Related Documentation

- [Main README](README.md) - Project overview and setup
- [Architecture Documentation](ARCHITECTURE.md) - Technical architecture details
- [Changelog](CHANGELOG.md) - Version history and updates
