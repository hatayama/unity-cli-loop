package projectrunner

import (
	clierrors "github.com/hatayama/unity-cli-loop/common/errors"

	"github.com/hatayama/unity-cli-loop/common/tooldocs"
)

type listOptions struct {
	namesOnly bool
}

func parseListOptions(args []string) (listOptions, error) {
	options := listOptions{}
	for _, arg := range args {
		if arg == "--"+tooldocs.ListNamesFlagName {
			options.namesOnly = true
			continue
		}
		return listOptions{}, &clierrors.ArgumentError{
			Message:     "Unknown option for list: " + arg,
			Option:      arg,
			Command:     "list",
			NextActions: []string{"Run `uloop list --help` to inspect supported options."},
		}
	}
	return options, nil
}
