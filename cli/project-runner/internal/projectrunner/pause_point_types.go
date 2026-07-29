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
	MaxPreviewElements              int                              `json:"MaxPreviewElements"`
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

	// Warning is set by Unity on enable/clear tool responses when this shared type decodes those
	// envelopes. The status bridge never sets it. On enable-pause-point --await hits, that enable
	// response text is exposed as EnableTimeWarning on the wait payload, not as hit-time Warning.
	Warning string `json:"Warning,omitempty"`

	// ResolvedLine / ResolvedLineText / ResolvedMethod / SnapshotTiming are copied from the
	// enable-pause-point response on the --await hit path so a single await payload records
	// both which source line was armed and what was captured. Method-name arms leave them empty
	// on the Unity side, so omitempty keeps the historical await schema unchanged for those cases.
	ResolvedLine     int    `json:"ResolvedLine,omitempty"`
	ResolvedLineText string `json:"ResolvedLineText,omitempty"`
	ResolvedMethod   string `json:"ResolvedMethod,omitempty"`
	SnapshotTiming   string `json:"SnapshotTiming,omitempty"`

	// CapturedVariableNameFilterNoMatch is set by the CLI, not Unity, when
	// --captured-variable-names was passed but none of the requested names matched any
	// captured variable (current or history), so an agent doesn't mistake an empty
	// CapturedVariables array for "nothing was captured at this hit".
	CapturedVariableNameFilterNoMatch bool `json:"CapturedVariableNameFilterNoMatch,omitempty"`

	// CapturedVariableNamesNotFound is set by the CLI, not Unity: the requested
	// --captured-variable-names that matched no captured variable, in the order they were
	// requested. Without it a partial match is indistinguishable from a full one, since the
	// response only carries the names that did match and CapturedVariableNameFilterNoMatch covers
	// the all-or-nothing case. Both are emitted when nothing matched at all.
	CapturedVariableNamesNotFound []string `json:"CapturedVariableNamesNotFound,omitempty"`

	// TriggerResult is set by the CLI, not Unity, only when --trigger was passed. It is omitted
	// entirely otherwise, so callers that never use --trigger see no schema change at all.
	TriggerResult *pausePointTriggerResult `json:"TriggerResult,omitempty"`

	// ResumePlayResult is set by the CLI, not Unity, only when --resume-play was passed. It is
	// omitted entirely otherwise, matching TriggerResult's omit-when-unused contract.
	ResumePlayResult *pausePointResumePlayResult `json:"ResumePlayResult,omitempty"`

	// TriggerFailed is set by the CLI, not Unity, only when --trigger was passed and the trigger is
	// known to have failed. It repeats at the top level what TriggerResult already carries three
	// levels down, because the loss it guards against is a caller reading Success:true / Status:Hit
	// and never opening TriggerResult at all. A pointer so the field is absent — rather than a
	// misleading false — when no trigger ran or its outcome is unknown.
	TriggerFailed *bool `json:"TriggerFailed,omitempty"`
}

// pausePointStatusResult wraps a status response with the CLI-evaluated --expect verdicts.
// pause-point-status marshals the Unity response directly, so it needs this wrapper to carry the
// two extra fields; the names match pausePointWaitResult's so one query shape reads both commands.
type pausePointStatusResult struct {
	pausePointStatusResponse

	// Both fields are omitted unless --expect was passed, and AllExpectationsPassed is a pointer
	// for the same reason as on pausePointWaitResult: to distinguish "no --expect given" from
	// "the given expectations failed".
	Expectations          []pausePointExpectationResult `json:"Expectations,omitempty"`
	AllExpectationsPassed *bool                         `json:"AllExpectationsPassed,omitempty"`
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
	Name     string `json:"Name"`
	Scope    string `json:"Scope"`
	TypeName string `json:"TypeName"`

	// Value is a pointer so a genuinely empty string (e.g. a captured `string s = ""`) still
	// serializes as "Value":"" in full mode, distinct from names mode setting it to nil to omit
	// the field entirely. A plain string with `omitempty` cannot make that distinction, since an
	// empty string and an absent value would both be omitted.
	Value                 *string `json:"Value,omitempty"`
	UnityObjectKind       string  `json:"UnityObjectKind,omitempty"`
	UnityObjectPath       string  `json:"UnityObjectPath,omitempty"`
	UnityObjectInstanceId int     `json:"UnityObjectInstanceId,omitempty"`
}

// pausePointVariableValue returns a pointer to value for use in pausePointCapturedVariable
// struct literals, where a plain string literal cannot have its address taken inline.
func pausePointVariableValue(value string) *string {
	return &value
}
