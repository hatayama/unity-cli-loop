package domain

type Endpoint struct {
	Network string
	Address string
}

type RequestMetadata struct {
	ExpectedProjectRoot string `json:"expectedProjectRoot"`
}

type Connection struct {
	Endpoint        Endpoint
	ProjectRoot     string
	RequestMetadata *RequestMetadata
}
