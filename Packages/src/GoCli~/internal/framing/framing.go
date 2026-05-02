package framing

import (
	"bufio"
	"bytes"
	"fmt"
	"io"
	"strconv"
	"strings"
)

const (
	contentLengthHeader = "Content-Length:"
	headerSeparator     = "\r\n\r\n"
)

func Write(writer io.Writer, payload []byte) error {
	if len(payload) == 0 {
		return fmt.Errorf("payload must not be empty")
	}

	_, err := fmt.Fprintf(writer, "%s %d%s", contentLengthHeader, len(payload), headerSeparator)
	if err != nil {
		return err
	}

	_, err = writer.Write(payload)
	return err
}

func Read(reader *bufio.Reader) ([]byte, error) {
	contentLength, err := readContentLength(reader)
	if err != nil {
		return nil, err
	}

	payload := make([]byte, contentLength)
	_, err = io.ReadFull(reader, payload)
	if err != nil {
		return nil, err
	}

	return payload, nil
}

func readContentLength(reader *bufio.Reader) (int, error) {
	var contentLength int
	foundContentLength := false

	for {
		line, err := reader.ReadBytes('\n')
		if err != nil {
			return 0, err
		}

		line = bytes.TrimRight(line, "\r\n")
		if len(line) == 0 {
			break
		}

		header := string(line)
		if !strings.HasPrefix(strings.ToLower(header), strings.ToLower(contentLengthHeader)) {
			continue
		}
		if foundContentLength {
			return 0, fmt.Errorf("duplicate Content-Length header")
		}

		value := strings.TrimSpace(header[len(contentLengthHeader):])
		parsed, err := strconv.Atoi(value)
		if err != nil || parsed < 0 {
			return 0, fmt.Errorf("invalid Content-Length header: %s", header)
		}

		contentLength = parsed
		foundContentLength = true
	}

	if !foundContentLength {
		return 0, fmt.Errorf("Content-Length header was not found")
	}

	return contentLength, nil
}
