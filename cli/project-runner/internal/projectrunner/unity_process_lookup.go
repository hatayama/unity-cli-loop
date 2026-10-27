package projectrunner

import (
	"github.com/hatayama/unity-cli-loop/common/unityprocess"
)

// unityProcessLookup is the lookup every default dependency set uses to find this project's Editor.
// Why a package variable: whole-command tests drive a scripted server through the default
// dependencies, and they must be able to say that an Editor process stands behind it. The tests of
// this package are not parallel.
var unityProcessLookup = unityprocess.FindRunningUnityProcess
