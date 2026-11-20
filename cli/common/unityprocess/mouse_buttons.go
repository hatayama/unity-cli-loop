package unityprocess

// mouseButtonHeld reads GetAsyncKeyState's SHORT answer as its raw 16 bits: only the high bit says
// the key is down right now, while the low bit says it was pressed at some point since the last call.
func mouseButtonHeld(asyncKeyState uint16) bool {
	return asyncKeyState&0x8000 != 0
}
