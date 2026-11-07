module github.com/hatayama/unity-cli-loop/project-runner

go 1.27.0

require (
	github.com/Microsoft/go-winio v0.6.4
	github.com/hatayama/unity-cli-loop/common v0.0.0-00010101000000-000000000000
)

require golang.org/x/sys v0.49.0 // indirect

replace github.com/hatayama/unity-cli-loop/common => ../common
