package framing

import (
	"bufio"
	"bytes"
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
