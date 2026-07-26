package tooldocs

// EnumValueForNumericDefault converts a numeric default on an enum property into the member name
// that has to be typed on the command line. Unity's schema generator serializes a C# enum default
// as its ordinal, so both option listings would otherwise report "default: 0" for a parameter that
// only accepts names such as "Press".
//
// The conversion assumes the enum is zero-based and contiguous, which is what a name lookup by
// ordinal requires. A value outside the listed range yields no conversion so the raw number is
// shown instead of a wrong member name.
func EnumValueForNumericDefault(defaultValue any, values []string) (string, bool) {
	if len(values) == 0 || defaultValue == nil {
		return "", false
	}

	switch value := defaultValue.(type) {
	case int:
		return enumValueAtIndex(value, values)
	case float64:
		index := int(value)
		if value != float64(index) {
			return "", false
		}
		return enumValueAtIndex(index, values)
	default:
		return "", false
	}
}

func enumValueAtIndex(index int, values []string) (string, bool) {
	if index < 0 || index >= len(values) {
		return "", false
	}
	return values[index], true
}
