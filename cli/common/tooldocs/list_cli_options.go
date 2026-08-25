package tooldocs

const ListNamesFlagName = "names"

// ListCLIOnlyOptions returns list's native-only flags from the same table its help renderer reads,
// so the parser and its recovery guidance cannot advertise a flag that list rejects.
func ListCLIOnlyOptions() []PausePointCLIOnlyOption {
	return []PausePointCLIOnlyOption{
		{
			FlagName:    ListNamesFlagName,
			Type:        "boolean",
			Description: "Show command names only, one per line",
		},
	}
}
