package domain

import "encoding/json"

type UnitySendOutcome struct {
	Result            json.RawMessage
	RequestDispatched bool
}
