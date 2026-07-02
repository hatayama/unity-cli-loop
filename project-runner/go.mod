module github.com/hatayama/unity-cli-loop/project-runner

go 1.26

require github.com/hatayama/unity-cli-loop/common v0.0.0-00010101000000-000000000000

require (
	github.com/Microsoft/go-winio v0.6.2 // indirect
	golang.org/x/sys v0.10.0 // indirect
)

replace github.com/hatayama/unity-cli-loop/common => ../common
