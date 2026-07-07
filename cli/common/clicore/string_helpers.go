package clicore

func FirstNonEmpty(values ...string) string {
	for _, value := range values {
		if value != "" {
			return value
		}
	}
	return ""
}

func ErrorMessage(err error) string {
	if err == nil {
		return ""
	}
	return err.Error()
}
