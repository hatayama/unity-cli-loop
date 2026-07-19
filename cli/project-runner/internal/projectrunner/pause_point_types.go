package projectrunner

type pausePointStatusResponse struct {
	Success                         bool                             `json:"Success"`
	Id                              string                           `json:"Id"`
	Status                          string                           `json:"Status"`
	IsEnabled                       bool                             `json:"IsEnabled"`
	IsHit                           bool                             `json:"IsHit"`
	HitCount                        int                              `json:"HitCount"`
	TimeoutSeconds                  int                              `json:"TimeoutSeconds"`
	Mode                            string                           `json:"Mode"`
	MaxHistory                      int                              `json:"MaxHistory"`
	CapturedVariableHistory         []pausePointCapturedHistoryFrame `json:"CapturedVariableHistory"`
	HistoryDroppedCount             int                              `json:"HistoryDroppedCount"`
	Expired                         bool                             `json:"Expired"`
	EnabledAtUtc                    string                           `json:"EnabledAtUtc"`
	ElapsedSinceEnabledMilliseconds int64                            `json:"ElapsedSinceEnabledMilliseconds"`
	RemainingMilliseconds           int64                            `json:"RemainingMilliseconds"`
	Generation                      int                              `json:"Generation"`
	EditorState                     pausePointEditorState            `json:"EditorState"`
	FirstHitAtUtc                   string                           `json:"FirstHitAtUtc"`
	LastHitAtUtc                    string                           `json:"LastHitAtUtc"`
	FirstHitSequence                int                              `json:"FirstHitSequence"`
	LastHitSequence                 int                              `json:"LastHitSequence"`
	Message                         string                           `json:"Message"`
	RecommendedNextAction           string                           `json:"RecommendedNextAction"`
	CapturedVariables               []pausePointCapturedVariable     `json:"CapturedVariables"`
	CapturedVariablesTruncated      bool                             `json:"CapturedVariablesTruncated"`
	ClearedReason                   string                           `json:"ClearedReason"`
	StatusBeforeClear               string                           `json:"StatusBeforeClear"`
	LateHitDiscardedAfterClear      bool                             `json:"LateHitDiscardedAfterClear"`
}

type pausePointEditorState struct {
	IsPlaying  bool   `json:"IsPlaying"`
	IsPaused   bool   `json:"IsPaused"`
	CapturedAt string `json:"CapturedAt"`
}

// pausePointCapturedHistoryFrame mirrors the Unity-side history DTO field-for-field.
type pausePointCapturedHistoryFrame struct {
	HitSequence       int                          `json:"HitSequence"`
	FrameCount        int                          `json:"FrameCount"`
	HitAtUtc          string                       `json:"HitAtUtc"`
	CapturedVariables []pausePointCapturedVariable `json:"CapturedVariables"`
	Truncated         bool                         `json:"Truncated"`
}

// pausePointCapturedVariable mirrors the flat Unity-side
// PausePointStatusCapturedVariable/UloopCapturedVariable DTO field-for-field: one variable
// captured at a source pause point (a local, a parameter, or a `this` instance field).
//
// UnityObjectKind is the discriminator for "is this a Unity object variable", not
// UnityObjectInstanceId: Unity's SourcePausePointVariableFormatter hardcodes all three
// UnityObject* fields to their zero value for non-Unity-object variables, but always sets a
// non-empty Kind for Unity object variables (including destroyed ones). InstanceId can in
// theory land on zero for a real Unity object on 6000.4+ (it is the lower 32 bits of an
// EntityId there), so independent omitempty per field is safe: Kind still surfaces in that
// case, and no consumer needs InstanceId==0 to mean "not a Unity object".
type pausePointCapturedVariable struct {
	Name                  string `json:"Name"`
	Scope                 string `json:"Scope"`
	TypeName              string `json:"TypeName"`
	Value                 string `json:"Value"`
	UnityObjectKind       string `json:"UnityObjectKind,omitempty"`
	UnityObjectPath       string `json:"UnityObjectPath,omitempty"`
	UnityObjectInstanceId int    `json:"UnityObjectInstanceId,omitempty"`
}
