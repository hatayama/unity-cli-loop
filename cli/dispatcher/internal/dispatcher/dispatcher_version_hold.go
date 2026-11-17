package dispatcher

import (
	"encoding/json"
	"errors"
	"io/fs"
	"os"
	"path/filepath"
	"runtime"

	"github.com/hatayama/unity-cli-loop/dispatcher/internal/nativepath"
	sharedupdate "github.com/hatayama/unity-cli-loop/dispatcher/internal/update"
)

// A version hold is what `uloop update --to-version` records: while it exists, the periodic
// self-update stays off. It lives in the config directory rather than the cache because a
// cache cleanup that deleted it would silently resume automatic updates.
const (
	dispatcherVersionHoldFileName       = "dispatcher-version-hold.json"
	dispatcherVersionHoldUnknownVersion = "unknown"
)

type dispatcherVersionHoldContent struct {
	Version string `json:"version"`
}

// dispatcherVersionHoldSnapshot is the hold file as it was before `uloop update --to-version`
// replaced it, so a failed install can put it back exactly.
type dispatcherVersionHoldSnapshot struct {
	exists  bool
	content []byte
}

func dispatcherVersionHoldPath() (string, error) {
	configRoot, err := nativepath.ConfigRoot(runtime.GOOS, nativepath.DefaultEnvironment())
	if err != nil {
		return "", err
	}
	return filepath.Join(configRoot, dispatcherVersionHoldFileName), nil
}

// readDispatcherVersionHold reports whether a hold exists and the version it holds. Any file at
// the hold path counts as a hold, even one that cannot be read or parsed: someone asked for a
// fixed version, and silently updating them is worse than asking them to run `uloop update`.
func readDispatcherVersionHold() (bool, string) {
	holdPath, err := dispatcherVersionHoldPath()
	if err != nil {
		return false, ""
	}
	content, err := os.ReadFile(holdPath)
	if errors.Is(err, fs.ErrNotExist) {
		return false, ""
	}
	if err != nil {
		return true, dispatcherVersionHoldUnknownVersion
	}
	hold := dispatcherVersionHoldContent{}
	if err := json.Unmarshal(content, &hold); err != nil {
		return true, dispatcherVersionHoldUnknownVersion
	}
	version := sharedupdate.NormalizeTargetVersion(hold.Version)
	if !sharedupdate.IsValidTargetVersion(version) {
		return true, dispatcherVersionHoldUnknownVersion
	}
	return true, version
}

func snapshotDispatcherVersionHold() dispatcherVersionHoldSnapshot {
	holdPath, err := dispatcherVersionHoldPath()
	if err != nil {
		return dispatcherVersionHoldSnapshot{}
	}
	content, err := os.ReadFile(holdPath)
	if errors.Is(err, fs.ErrNotExist) {
		return dispatcherVersionHoldSnapshot{}
	}
	return dispatcherVersionHoldSnapshot{exists: true, content: content}
}

func writeDispatcherVersionHold(version string) error {
	holdPath, err := dispatcherVersionHoldPath()
	if err != nil {
		return err
	}
	content, err := json.Marshal(dispatcherVersionHoldContent{Version: version})
	if err != nil {
		return err
	}
	if err := os.MkdirAll(filepath.Dir(holdPath), 0o755); err != nil {
		return err
	}
	return os.WriteFile(holdPath, content, 0o644)
}

// restoreDispatcherVersionHold puts back the hold that existed before a failed install, or
// removes the new one when there was none. Failures are ignored: the install already failed and
// that error is what the user needs to see.
func restoreDispatcherVersionHold(snapshot dispatcherVersionHoldSnapshot) {
	holdPath, err := dispatcherVersionHoldPath()
	if err != nil {
		return
	}
	if !snapshot.exists {
		_ = os.Remove(holdPath)
		return
	}
	_ = os.WriteFile(holdPath, snapshot.content, 0o644)
}

// removeDispatcherVersionHold deletes the hold and reports whether one existed.
func removeDispatcherVersionHold() (bool, error) {
	holdPath, err := dispatcherVersionHoldPath()
	if err != nil {
		return false, nil
	}
	err = os.Remove(holdPath)
	if errors.Is(err, fs.ErrNotExist) {
		return false, nil
	}
	if err != nil {
		return false, err
	}
	return true, nil
}
