package projectrunner

type watchResponse struct {
	Success             bool                            `json:"Success"`
	Id                  string                          `json:"Id"`
	Expression          string                          `json:"Expression"`
	MaxHistory          int                             `json:"MaxHistory"`
	HistoryDroppedCount int                             `json:"HistoryDroppedCount"`
	ClearedCount        int                             `json:"ClearedCount"`
	Message             string                          `json:"Message"`
	Watches             []watchEntryResponse            `json:"Watches"`
	CompilationErrors   []watchCompilationErrorResponse `json:"CompilationErrors"`
}

type watchEntryResponse struct {
	Id                  string                 `json:"Id"`
	Expression          string                 `json:"Expression"`
	MaxHistory          int                    `json:"MaxHistory"`
	HistoryDroppedCount int                    `json:"HistoryDroppedCount"`
	History             []watchHistoryResponse `json:"History"`
}

type watchHistoryResponse struct {
	FrameCount     int    `json:"FrameCount"`
	EvaluatedAtUtc string `json:"EvaluatedAtUtc"`
	Success        bool   `json:"Success"`
	Value          string `json:"Value"`
	ErrorTypeName  string `json:"ErrorTypeName"`
	ErrorMessage   string `json:"ErrorMessage"`
}

type watchCompilationErrorResponse struct {
	Line      int    `json:"Line"`
	Column    int    `json:"Column"`
	Message   string `json:"Message"`
	ErrorCode string `json:"ErrorCode"`
}
