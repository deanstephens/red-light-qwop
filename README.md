# Red Light Qwop

Red Light, Green Light with QWOP-style controls. You steer a physics ragdoll one limb at a time
toward the finish line while a doll at the far end turns around on red. Move on red and you are out.
Ten computer runners race alongside you, and some of them do not stop in time.

**Play in the browser:** https://deanstephens.github.io/red-light-qwop/

## Controls

| Key | Action |
|-----|--------|
| Q / W | Swing the hips (left leg forward / right leg forward) |
| O / P | Bend the knees (left / right) |
| R | Restart the round |

A stride that works: hold Q and tap O, keep holding Q, then switch to W and tap P. Keep it brisk.
Release everything the moment the light turns red.

## Multiplayer

- **Host online** creates a session and shows a join code. Friends enter it in the Join box.
  Works across the internet through Unity Relay with no port forwarding.
- **Host on LAN** (desktop builds) listens on UDP 7777. Friends join by IP address.
- The browser version plays through the relay for everything, including solo.
- Open the web build with `?join=CODE` in the URL to join a session directly.

## Project

- Unity 6000.6 (URP), Netcode for GameObjects, Unity Multiplayer Services (Relay), Cinemachine.
- Regenerate the scene from the menu **Red Light Qwop > Build Game Scene**.
- PlayMode tests live in `Assets/Tests/PlayMode`.
- `docs/` holds the WebGL build served by GitHub Pages.
