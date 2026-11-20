// Package filelock takes and releases an exclusive, non-blocking OS lock on an open file. The OS
// drops the lock when the holding process exits, so a crashed holder never leaves it behind.
// Locks taken through separate opens of the same file conflict even within one process.
package filelock
