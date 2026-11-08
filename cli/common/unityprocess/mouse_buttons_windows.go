//go:build windows

package unityprocess

// heldMouseButton pairs the name the CLI reports with the Windows virtual-key code it is read from.
type heldMouseButton struct {
	name       string
	virtualKey uint16
}

// heldMouseButtons is in the order the CLI reports them; a map would shuffle it between runs.
var heldMouseButtons = []heldMouseButton{
	{name: "left", virtualKey: 0x01},   // VK_LBUTTON
	{name: "right", virtualKey: 0x02},  // VK_RBUTTON
	{name: "middle", virtualKey: 0x04}, // VK_MBUTTON
	{name: "x1", virtualKey: 0x05},     // VK_XBUTTON1
	{name: "x2", virtualKey: 0x06},     // VK_XBUTTON2
}

// HeldMouseButtons reports which mouse buttons Windows currently sees as held, in a fixed order, and
// true. It is diagnostic input for a compile Unity withholds because it believes a button is held:
// agreement with Unity points at a physically held button, disagreement at a swallowed mouse-up.
func HeldMouseButtons() ([]string, bool) {
	held := []string{}
	for _, button := range heldMouseButtons {
		if mouseButtonHeld(user32.GetAsyncKeyState(button.virtualKey)) {
			held = append(held, button.name)
		}
	}
	return held, true
}
