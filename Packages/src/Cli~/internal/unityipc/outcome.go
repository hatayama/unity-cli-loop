package unityipc

import (
	"encoding/json"
	"time"
)

type UnitySendOutcome struct {
	Result            json.RawMessage
	RequestDispatched bool
	RequestAccepted   bool
	Timing            UnitySendTiming
}

type UnitySendTiming struct {
	Total  time.Duration
	Dial   time.Duration
	Write  time.Duration
	Read   time.Duration
	Decode time.Duration
}
