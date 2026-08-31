#!/usr/bin/env python3
"""
Port blocker script for testing UnityCliLoop automatic port adjustment
Usage: python3 test_port_blocker.py [port_number]
Default port: 7400
"""

import socket
import time
import sys

def block_port(port=8700):
    """Block the specified port to test UnityCliLoop port adjustment"""
    try:
        # Create socket and bind to port
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
        s.bind(('localhost', port))
        s.listen(0)
        
        print(f"✅ Port {port} is now occupied and blocked")
        print("📋 Now you can test UnityCliLoop automatic port adjustment:")
        print("   1. Open Unity UnityCliLoop Window")
        print("   2. Try to start server on port 8700")
        print("   3. It should automatically find port 8701")
        print("\n🛑 Press Ctrl+C to stop blocking the port")
        
        # Keep the port blocked
        try:
            while True:
                time.sleep(1)
        except KeyboardInterrupt:
            print("\n\n🔄 Stopping port blocker...")
            
    except socket.error as e:
        print(f"❌ Error: Could not bind to port {port}")
        print(f"   Reason: {e}")
        print(f"   Port {port} might already be in use")
        return False
    except Exception as e:
        print(f"❌ Unexpected error: {e}")
        return False
    finally:
        try:
            s.close()
            print(f"✅ Port {port} released")
        except:
            pass
    
    return True

if __name__ == "__main__":
    # Get port number from command line argument or use default
    port = 8700
    if len(sys.argv) > 1:
        try:
            port = int(sys.argv[1])
        except ValueError:
            print("❌ Invalid port number. Using default port 8700")
            port = 8700
    
    print(f"🚀 Starting port blocker for port {port}")
    block_port(port)