# DotDesk

<!-- <p align="center">
  <img src="http://www.dtros.com/dotdesk-hero.png" alt="DotDesk Preview" width="850">
</p> -->

<p align="center">
  <b>DotDesk is an open-source remote desktop project built with C# and WebRTC.</b>
</p>

<p align="center">
  <a href="https://github.com/guanlinbo/DotDesk">
    <img src="https://img.shields.io/badge/GitHub-DotDesk-181717?style=flat-square&logo=github" alt="GitHub">
  </a>
  <img src="https://img.shields.io/badge/platform-Windows-blue?style=flat-square" alt="Platform">
  <img src="https://img.shields.io/badge/language-C%23-512BD4?style=flat-square&logo=csharp" alt="C#">
  <img src="https://img.shields.io/badge/WebRTC-P2P-brightgreen?style=flat-square" alt="WebRTC">
  <img src="https://img.shields.io/badge/status-developing-orange?style=flat-square" alt="Status">
</p>

---

## Introduction

DotDesk is an open-source remote desktop software project.

The goal of DotDesk is to build a simple, secure, low-latency and self-hostable remote desktop solution.

DotDesk is currently under active development. It is mainly used for learning, research and implementation of remote desktop core technologies, including screen capture, video encoding, WebRTC communication, NAT traversal, signaling server and remote input control.

---

## Preview

<p align="center">
  <img src="http://www.dtros.com/dotdesk-hero.png" alt="DotDesk UI Preview" width="850">
</p>

---

## Features

- Remote desktop control
- Device ID connection
- One-time password authentication
- WebSocket signaling server
- WebRTC P2P connection
- STUN / TURN support
- H264 video transmission
- Mouse input control
- Keyboard input control
- WebRTC DataChannel control channel
- DXGI / GDI screen capture
- AntdUI based Windows client UI
- Network offline page
- Recent connections page
- Basic logging support

---

## Tech Stack

| Part | Technology |
| --- | --- |
| Desktop Client | C# / WinForms / AntdUI |
| Screen Capture | DXGI / GDI |
| Video | H264 |
| Realtime Communication | WebRTC |
| Signaling | WebSocket |
| NAT Traversal | STUN / TURN |
| Control Channel | WebRTC DataChannel |
| Server | C++ / WebSocket |
| Platform | Windows |

---

## Project Structure

```txt
DotDesk
├── DotDesk.App
│   ├── Main window
│   ├── Home page
│   ├── Settings page
│   └── Offline page
│
├── DotDesk.Core
│   ├── Common models
│   ├── Protocol definitions
│   └── Utilities
│
├── DotDesk.Client
│   ├── Host side logic
│   ├── Screen capture
│   ├── Video encoding
│   └── Input injection
│
├── DotDesk.Controller
│   ├── Controller side logic
│   ├── Video decoding
│   ├── Mouse control
│   └── Keyboard control
│
└── DotDesk.Server
    ├── Signaling server
    ├── WebSocket service
    └── Peer pairing
```

---

## How It Works

```txt
Controller enters the host device ID
        │
        ▼
Connect to DotDesk signaling server
        │
        ▼
Exchange Offer / Answer / ICE candidates
        │
        ▼
Try WebRTC P2P connection first
        │
        ├── Success: transfer video and control data directly
        │
        └── Failed: fallback to TURN / relay
        │
        ▼
Host captures screen and encodes video
        │
        ▼
Controller receives and displays video stream
        │
        ▼
Mouse and keyboard events are sent through DataChannel
```

---

## Getting Started

### Clone

```bash
git clone https://github.com/guanlinbo/DotDesk.git
cd DotDesk
```

---

## Server

Enter the server directory:

```bash
cd DotDesk.Server
```

Build example:

```bash
mkdir build
cd build
cmake ..
make
```

Run:

```bash
./DotDesk_Server
```

Default signaling URL example:

```txt
ws://your-server-ip:5000/ws/{deviceId}/{role}
```

---

## Client

Open the solution with Visual Studio.

Set the startup project to:

```txt
DotDesk.App
```

Then build and run.

---

## Network

DotDesk uses WebRTC for remote desktop communication.

Recommended deployment:

```txt
Signaling Server  -> Peer registration and SDP / ICE exchange
STUN Server       -> NAT detection
TURN Server       -> Relay fallback when P2P fails
```

In complex NAT environments, TURN relay may be required to improve connection success rate.

---

## Roadmap

- [ ] File transfer
- [ ] View-only mode
- [ ] Unattended access
- [ ] Multiple monitor support
- [ ] Clipboard synchronization
- [ ] Audio transmission
- [ ] Adaptive bitrate
- [ ] Dynamic quality control
- [ ] GPU hardware encoding
- [ ] DXGI Dirty Rect optimization
- [ ] Device list
- [ ] Connection history
- [ ] Server authentication
- [ ] Self-hosting documentation
- [ ] Linux / macOS support

---

## Development Goal

DotDesk is currently focused on implementing the core remote desktop pipeline:

```txt
Capture -> Encode -> Transport -> Decode -> Render -> Control
```

The project aims to keep the code structure simple and readable, making it easier to learn, debug and extend.

---

## Security Notice

DotDesk is still under development.

Please do not use it directly in production or high-security environments.

If you expose DotDesk services to the public network, make sure to configure:

- Strong password
- HTTPS / WSS
- TURN long-term credentials
- Firewall rules
- Access control
- Server-side authentication

---

## Contributing

Contributions are welcome.

You can help with:

- UI improvement
- Remote control experience
- Video encoding optimization
- WebRTC connection stability
- P2P success rate
- Server stability
- Documentation
- Bug fixes

Feel free to open an Issue or Pull Request.

---

## License

This project is currently for learning, research and technical communication.

Please evaluate security, stability and compliance before using it in production or commercial environments.
