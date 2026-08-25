package projectrunner

import "testing"

// Verifies file:line marker ids use the same slash normalization and decimal line formatting as
// source-location enable, so query commands address the marker that Unity registered.
func TestComposePausePointFileLineIDMatchesSourceEnableConvention(t *testing.T) {
	tests := []struct {
		name string
		file string
		line int
		want string
	}{
		{
			name: "backslashes",
			file: `Assets\Scripts\Marker.cs`,
			line: 42,
			want: "Assets/Scripts/Marker.cs:42",
		},
		{
			name: "absolute path",
			file: "/source/Assets/Scripts/Marker.cs",
			line: 7,
			want: "/source/Assets/Scripts/Marker.cs:7",
		},
	}

	for _, test := range tests {
		t.Run(test.name, func(t *testing.T) {
			got := composePausePointFileLineID(test.file, test.line)
			if got != test.want {
				t.Fatalf("id = %q, want %q", got, test.want)
			}
		})
	}
}
