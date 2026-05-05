module github.com/hatayama/unity-cli-loop/Packages/src/Cli/Core

go 1.26

require (
	github.com/Microsoft/go-winio v0.6.2
	github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared v0.0.0
)

require golang.org/x/sys v0.10.0 // indirect

replace github.com/hatayama/unity-cli-loop/Packages/src/Cli/Shared => ../Shared~
