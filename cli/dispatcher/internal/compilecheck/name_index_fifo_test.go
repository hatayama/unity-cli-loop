//go:build !windows

package compilecheck

import (
	"os"
	"path/filepath"
	"syscall"
	"testing"
	"time"
)

// Verifies the generator output is read before start returns: with the sources read only after the
// generated directory is gone, as a compile that started meanwhile removes it, and that read held on a
// source until the test releases it, the names only the generator wrote are still in the index.
func TestNameIndexReadsGeneratedCodeBeforeStartReturns(t *testing.T) {
	root := t.TempDir()
	writeUnitSources(t, root, map[string]string{"Generated/Gen.g.cs": "partial class First { Foo f; }"})
	source := filepath.Join(root, "Assets", "A.cs")
	if err := os.MkdirAll(filepath.Dir(source), 0o755); err != nil {
		t.Fatalf("failed to create the source directory: %v", err)
	}
	// Why a FIFO: reading it blocks until the test writes it, which holds the background read at a
	// point the test chooses.
	if err := syscall.Mkfifo(source, 0o644); err != nil {
		t.Fatalf("failed to create the source FIFO: %v", err)
	}
	unit := CompileUnit{Assembly: ResponseFile{AssemblyName: "Unit"}, Sources: []string{"Assets/A.cs"}}
	index := nameIndex{}

	index.start(root, []CompileUnit{unit}, []string{filepath.Join(root, "Generated")}, []bool{true})
	if err := os.RemoveAll(filepath.Join(root, "Generated")); err != nil {
		t.Fatalf("failed to remove the generated code: %v", err)
	}
	index.readSources()
	releaseFIFO(t, source, "class First { }")

	set, _, err := index.lookup(0)
	if err != nil {
		t.Fatalf("expected the identifiers of the unit, got %v", err)
	}
	assertHasIdentifiers(t, set, []string{"First", "Foo"}, nil)
}

// Verifies a run that never asks for the sources does not read them: with a source no writer ever
// opens, wait and report still return at once after start.
func TestNameIndexWaitReturnsAtOnceWhenTheSourcesWereNeverAsked(t *testing.T) {
	root := t.TempDir()
	source := filepath.Join(root, "Assets", "A.cs")
	if err := os.MkdirAll(filepath.Dir(source), 0o755); err != nil {
		t.Fatalf("failed to create the source directory: %v", err)
	}
	// Why a FIFO no one writes: opening it blocks forever, so any read of the sources holds wait.
	if err := syscall.Mkfifo(source, 0o644); err != nil {
		t.Fatalf("failed to create the source FIFO: %v", err)
	}
	unit := CompileUnit{Assembly: ResponseFile{AssemblyName: "Unit"}, Sources: []string{"Assets/A.cs"}}
	index := nameIndex{}
	index.start(root, []CompileUnit{unit}, []string{filepath.Join(root, "Generated")}, []bool{true})

	reported := make(chan NameIndexReport, 1)
	go func() {
		index.wait()
		reported <- index.report()
	}()

	select {
	case report := <-reported:
		if report.SourcesRead || report.Units != 1 {
			t.Errorf("report = %+v, want one unit whose sources were not read", report)
		}
	case <-time.After(5 * time.Second):
		t.Fatal("wait or report held on sources nothing asked for")
	}
}

// releaseFIFO writes content to the FIFO at path once its reader opens it, and fails the test when
// no reader comes.
func releaseFIFO(t *testing.T, path string, content string) {
	t.Helper()
	written := make(chan error, 1)
	go func() {
		writer, err := os.OpenFile(path, os.O_WRONLY, 0)
		if err != nil {
			written <- err

			return
		}
		_, err = writer.WriteString(content)
		if closeErr := writer.Close(); err == nil {
			err = closeErr
		}
		written <- err
	}()
	select {
	case err := <-written:
		if err != nil {
			t.Fatalf("failed to write the source FIFO: %v", err)
		}
	case <-time.After(10 * time.Second):
		t.Fatal("nothing read the source FIFO")
	}
}
