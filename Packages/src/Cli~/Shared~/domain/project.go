package domain

type Endpoint struct {
	Network string
	Address string
}

type Connection struct {
	Endpoint    Endpoint
	ProjectRoot string
}
