// Package clicore contains command, tool-catalog, and runtime helpers shared by
// the dispatcher and project runner entrypoints.
//
// Import focused common packages such as errors, tooldocs, ui, and vibelog
// directly when their APIs are needed. This package should not re-export those
// packages as a convenience facade; it only keeps helpers that combine CLI
// command policy with shared data owned here.
package clicore
