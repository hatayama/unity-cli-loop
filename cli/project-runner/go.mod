module github.com/hatayama/unity-cli-loop/project-runner

go 1.26.0

require (
	github.com/Microsoft/go-winio v0.6.3
	github.com/hatayama/unity-cli-loop/common v0.0.0-00010101000000-000000000000
)

require golang.org/x/sys v0.48.0 // indirect

replace github.com/hatayama/unity-cli-loop/common => ../common
