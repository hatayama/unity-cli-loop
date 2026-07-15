//go:build windows

package unityipc

// Why: named pipes do not use the Unix filesystem endpoint boundary; the shared dial
// choke point deliberately keeps Windows transport behavior unchanged.
func validateEndpointSecurity(endpoint Endpoint) error {
	return nil
}
