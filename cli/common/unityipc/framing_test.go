package unityipc

import (
	"bufio"
	"bytes"
	"errors"
	"io"
	"testing"
)

func TestWriteAndReadRoundTrip(t *testing.T) {
	var buffer bytes.Buffer
	payload := []byte(`{"jsonrpc":"2.0","id":1}`)

	if err := Write(&buffer, payload); err != nil {
		t.Fatalf("Write failed: %v", err)
	}

	actual, err := Read(bufio.NewReader(&buffer))
	if err != nil {
		t.Fatalf("Read failed: %v", err)
	}

	if string(actual) != string(payload) {
		t.Fatalf("payload mismatch: got %q want %q", string(actual), string(payload))
	}
}

func TestReadRejectsMissingContentLength(t *testing.T) {
	_, err := Read(bufio.NewReader(bytes.NewBufferString("\r\n{}")))
	if err == nil {
		t.Fatal("Read succeeded for missing Content-Length")
	}
}

func TestReadRejectsDuplicateContentLength(t *testing.T) {
	input := "Content-Length: 2\r\nContent-Length: 3\r\n\r\n{}"

	_, err := Read(bufio.NewReader(bytes.NewBufferString(input)))
	if err == nil {
		t.Fatal("Read succeeded for duplicate Content-Length")
	}
}

type failingWriter struct {
	failOnCall int
	calls      int
	written    bytes.Buffer
	err        error
}

func (writer *failingWriter) Write(payload []byte) (int, error) {
	writer.calls++
	if writer.calls == writer.failOnCall {
		return 0, writer.err
	}
	return writer.written.Write(payload)
}

// Verifies that Write refuses an empty payload without emitting a header.
func TestWriteRejectsEmptyPayload(t *testing.T) {
	writer := &failingWriter{}

	err := Write(writer, nil)

	if err == nil || err.Error() != "payload must not be empty" {
		t.Fatalf("expected empty payload error, got %v", err)
	}
	if writer.calls != 0 {
		t.Fatalf("empty payload must not write anything: %d calls", writer.calls)
	}
}

// Verifies that a header write failure is returned without writing the payload, and a payload
// write failure is returned after the header was written.
func TestWritePropagatesWriterFailures(t *testing.T) {
	cases := []struct {
		name            string
		failOnCall      int
		expectedWritten string
	}{
		{name: "header write fails", failOnCall: 1, expectedWritten: ""},
		{name: "payload write fails", failOnCall: 2, expectedWritten: "Content-Length: 2\r\n\r\n"},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			writerErr := errors.New(testCase.name)
			writer := &failingWriter{failOnCall: testCase.failOnCall, err: writerErr}

			err := Write(writer, []byte("{}"))

			if !errors.Is(err, writerErr) {
				t.Fatalf("expected writer error %q, got %v", testCase.name, err)
			}
			if writer.calls != testCase.failOnCall {
				t.Fatalf("write must stop at the failing call: %d calls", writer.calls)
			}
			if writer.written.String() != testCase.expectedWritten {
				t.Fatalf("written bytes mismatch: %q", writer.written.String())
			}
		})
	}
}

// Verifies that headers other than Content-Length are skipped, case-insensitively matched.
func TestReadSkipsUnrelatedHeaders(t *testing.T) {
	input := "Content-Type: application/json\r\ncontent-length: 2\r\n\r\n{}"

	payload, err := Read(bufio.NewReader(bytes.NewBufferString(input)))
	if err != nil {
		t.Fatalf("Read failed: %v", err)
	}
	if string(payload) != "{}" {
		t.Fatalf("payload mismatch: %q", string(payload))
	}
}

// Verifies that malformed or truncated frames fail with the error specific to each defect.
func TestReadRejectsMalformedFrames(t *testing.T) {
	cases := []struct {
		name          string
		input         string
		expectedError string
	}{
		{name: "non-numeric length", input: "Content-Length: abc\r\n\r\n{}", expectedError: "invalid Content-Length header: Content-Length: abc"},
		{name: "negative length", input: "Content-Length: -1\r\n\r\n{}", expectedError: "invalid Content-Length header: Content-Length: -1"},
		{name: "missing length", input: "Content-Type: application/json\r\n\r\n{}", expectedError: "Content-Length header was not found"},
		{name: "duplicate length", input: "Content-Length: 2\r\nContent-Length: 3\r\n\r\n{}", expectedError: "duplicate Content-Length header"},
		{name: "truncated header", input: "Content-Length: 2", expectedError: io.EOF.Error()},
		{name: "truncated payload", input: "Content-Length: 10\r\n\r\n{}", expectedError: io.ErrUnexpectedEOF.Error()},
	}

	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			payload, err := Read(bufio.NewReader(bytes.NewBufferString(testCase.input)))

			if err == nil || err.Error() != testCase.expectedError {
				t.Fatalf("expected error %q, got %v", testCase.expectedError, err)
			}
			if payload != nil {
				t.Fatalf("failed read must not return a payload: %q", string(payload))
			}
		})
	}
}
