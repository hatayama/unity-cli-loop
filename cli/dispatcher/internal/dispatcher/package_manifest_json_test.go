package dispatcher

import (
	"encoding/json"
	"errors"
	"io"
	"reflect"
	"testing"
)

func TestParseOrderedJSONObjectBytesRejectsIncompleteObjects(t *testing.T) {
	// Verifies empty, truncated, and value-less object input is rejected instead of yielding a partial object.
	cases := []struct {
		input   string
		wantEOF bool
		want    string
	}{
		{input: "", wantEOF: true},
		{input: `{"a":1`, wantEOF: true},
		{input: `{"a":}`, want: "invalid character '}' looking for beginning of value"},
	}
	for _, testCase := range cases {
		_, err := parseOrderedJSONObjectBytes([]byte(testCase.input))
		if testCase.wantEOF && !errors.Is(err, io.EOF) || !testCase.wantEOF && (err == nil || err.Error() != testCase.want) {
			t.Fatalf("%q: unexpected error %v", testCase.input, err)
		}
	}
}

func TestParseJSONRawArrayRejectsMalformedArrays(t *testing.T) {
	// Verifies empty, truncated, invalid, and trailing-garbage arrays are rejected.
	cases := []struct {
		input   string
		wantEOF bool
		want    string
	}{
		{input: "", wantEOF: true},
		{input: "[1", wantEOF: true},
		{input: "[1,}", want: "invalid character '}' looking for beginning of value"},
		{input: "[1] 2", want: "unexpected trailing JSON token: 2"},
	}
	for _, testCase := range cases {
		_, err := parseJSONRawArray([]byte(testCase.input))
		if testCase.wantEOF && !errors.Is(err, io.EOF) || !testCase.wantEOF && (err == nil || err.Error() != testCase.want) {
			t.Fatalf("%q: unexpected error %v", testCase.input, err)
		}
	}
}

func TestEmitOrderedJSONObjectRejectsInconsistentObjects(t *testing.T) {
	// Verifies emitting fails for a key without a value or with an unusable value instead of writing broken JSON.
	cases := []struct {
		name   string
		object orderedJSONObject
		want   string
	}{
		{name: "missing value", object: orderedJSONObject{keys: []string{"a"}, values: map[string]json.RawMessage{}}, want: `missing value for key "a"`},
		{name: "blank value", object: orderedJSONObject{keys: []string{"a"}, values: map[string]json.RawMessage{"a": json.RawMessage(" ")}}, want: "empty JSON value"},
		{name: "invalid array value", object: orderedJSONObject{keys: []string{"a"}, values: map[string]json.RawMessage{"a": json.RawMessage("[1,")}}, want: "unexpected end of JSON input"},
		{name: "invalid nested object", object: orderedJSONObject{keys: []string{"a"}, values: map[string]json.RawMessage{"a": json.RawMessage(`{"b"`)}}, want: "EOF"},
	}
	for _, testCase := range cases {
		t.Run(testCase.name, func(t *testing.T) {
			_, err := emitOrderedJSONObject(testCase.object, 0)
			if err == nil || err.Error() != testCase.want {
				t.Fatalf("expected %q, got %v", testCase.want, err)
			}
		})
	}
	if _, err := emitJSONRawArray([]json.RawMessage{json.RawMessage(" ")}, 0); err == nil || err.Error() != "empty JSON value" {
		t.Fatalf("expected the array element error, got %v", err)
	}
}

func TestOrderedJSONObjectInsertAfterReplacesExistingKeyInPlace(t *testing.T) {
	// Verifies inserting an existing key replaces its value without moving it, and a zero object gets a value map.
	object := orderedJSONObject{keys: []string{"a", "b"}, values: map[string]json.RawMessage{"a": json.RawMessage("1"), "b": json.RawMessage("2")}}
	object.insertAfter("a", json.RawMessage("3"), "b")

	if !reflect.DeepEqual(object.keys, []string{"a", "b"}) || string(object.values["a"]) != "3" {
		t.Fatalf("unexpected object: %+v", object)
	}

	empty := orderedJSONObject{}
	empty.insertAfter("a", json.RawMessage("1"), "")
	if !reflect.DeepEqual(empty.keys, []string{"a"}) || string(empty.values["a"]) != "1" {
		t.Fatalf("unexpected object: %+v", empty)
	}
}

func TestSortStringsByteOrderSortsByBytes(t *testing.T) {
	// Verifies scopes are sorted by byte order, so uppercase sorts before lowercase.
	values := []string{"io.b", "com.a", "Z.upper", "io.a"}

	sortStringsByteOrder(values)

	if want := []string{"Z.upper", "com.a", "io.a", "io.b"}; !reflect.DeepEqual(values, want) {
		t.Fatalf("got %v want %v", values, want)
	}
}
