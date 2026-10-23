# Remote Access Setup

## Architecture

Each development machine runs the Python MCP server alongside Unity. Claude Desktop
on Machine A connects to whichever machine's server you want to work with.

```
Machine A: Claude Desktop ──── HTTP (streamable-http) ──────────┐
                                                                  ▼
Machine B: Python MCP server (port 8000) ── localhost:11000 ── Unity Editor
Machine C: Python MCP server (port 8000) ── localhost:11000 ── Unity Editor
Machine D: Python MCP server (port 8000) ── localhost:11000 ── Unity Editor
```

Unity's HTTP listener stays on `localhost` on every machine. Only the Python MCP
server is exposed to the network, and it only accepts clients that send its
token (`UNITY_MCP_SERVER_TOKEN`, see below).

The Unity HTTP API has its own token too, the bridge token. Unity writes it to
`~/.unity-mcp/bridge-token` and the Python server on the same machine reads it
from there, so you never need to copy it for this setup.

---

## Setup on each development machine (B, C, D)

### 1. Install dependencies

```bash
cd <EadonMCP>/server
uv sync
```

### 2. Start the MCP server in network mode

Run this on each machine you want to access remotely. Unity must also be open.

By default the server only accepts connections from its own machine
(`127.0.0.1`). To accept connections from the network, bind to `0.0.0.0` and
set `UNITY_MCP_SERVER_TOKEN`: the server refuses to start on the network
without it, because anyone who can reach the port could otherwise call every
tool, including writing scripts into your project. Use a long random value,
for example the output of `openssl rand -hex 32`, and keep it out of shared
files.

```bash
# Make a token once and keep it (64 characters)
export UNITY_MCP_SERVER_TOKEN=$(openssl rand -hex 32)
echo $UNITY_MCP_SERVER_TOKEN

# Network, port 8000
uv run unity-mcp --transport streamable-http --host 0.0.0.0

# Custom port (if 8000 is taken)
uv run unity-mcp --transport streamable-http --host 0.0.0.0 --port 8001

# Or via environment variables
UNITY_MCP_TRANSPORT=streamable-http UNITY_MCP_SERVER_HOST=0.0.0.0 UNITY_MCP_SERVER_PORT=8000 \
UNITY_MCP_SERVER_TOKEN=<your-token> uv run unity-mcp
```

`--transport sse` works the same way for older clients; its URL ends in `/sse`
instead of `/mcp`.

The server prints the URL for clients:

```
[unity-mcp] Starting STREAMABLE-HTTP server on 0.0.0.0:8000 (clients must send the UNITY_MCP_SERVER_TOKEN token)
[unity-mcp] Client URL: http://<this-machine-ip>:8000/mcp
```

Every client must send the token as an HTTP header:

```
Authorization: Bearer <your-token>
```

### 3. Find the machine's IP address

```bash
# macOS
ipconfig getifaddr en0

# Windows
ipconfig | findstr IPv4

# Linux
ip addr show | grep 'inet '
```

### 4. Firewall

Make sure port 8000 (or whichever port you chose) is open for inbound TCP
connections. On macOS, the system may prompt you when the server first starts.

### 5. Check the connection from the client machine

`server/scripts/remote_check.py` checks the whole path from the machine that
will connect: a wrong token is refused, the right token opens an MCP session
and lists the tools, and `ping_unity` reaches Unity. Copy the one file to the
client machine (uv fetches Python and the MCP library by itself):

```bash
uv run --with "mcp>=2.3,<3" remote_check.py <server-ip> <your-token>
```

Expected output:

```
1. Wrong token refused: yes
2. MCP session OK: 78 tools listed
3. ping_unity: Unity 6000.3.26f1 is running. Active scene: ...
```

If your token is refused, the script says so and shows its length: copy it
again, all 64 characters.

---

## Setup on Machine A (Claude Desktop)

Claude Desktop starts its MCP servers as local commands (stdio); it does not
take a remote URL and headers in its config file. The `mcp-remote` package
bridges the two: Claude Desktop runs it locally, and it connects to the remote
server with the token. It needs Node.js (for `npx`) on Machine A.

Edit `~/Library/Application Support/Claude/claude_desktop_config.json`
(macOS) or `%APPDATA%\Claude\claude_desktop_config.json` (Windows).

Add an entry for each machine you want to reach. You can have as many as you
like. Each entry sends that machine's `UNITY_MCP_SERVER_TOKEN`:

```json
{
  "mcpServers": {
    "unity-macbook": {
      "command": "npx",
      "args": ["mcp-remote", "http://192.168.1.101:8000/mcp", "--allow-http",
               "--header", "Authorization:${AUTH_HEADER}"],
      "env": { "AUTH_HEADER": "Bearer <macbook-token>" }
    },
    "unity-desktop": {
      "command": "npx",
      "args": ["mcp-remote", "http://192.168.1.102:8000/mcp", "--allow-http",
               "--header", "Authorization:${AUTH_HEADER}"],
      "env": { "AUTH_HEADER": "Bearer <desktop-token>" }
    }
  }
}
```

The header is written as `Authorization:${AUTH_HEADER}`, with the
space-containing `Bearer <token>` value in `env`, because Windows can split
arguments that contain spaces. `--allow-http` is needed because the server
speaks plain HTTP on your LAN; the token travels unencrypted, so use this on a
trusted network only.

Use hostnames if your network has DNS / mDNS set up (e.g. `macbook.local`
on macOS) in place of the IP address.

Restart Claude Desktop after editing the config. The new servers appear in
Claude Desktop's tool list.

---

## Local use (unchanged)

Machine A can still use its own local Unity instance via stdio, alongside the
remote entries. The local entry uses `command`/`args` as before, and needs no
token settings: the server reads the bridge token from `~/.unity-mcp/bridge-token`.

```json
{
  "mcpServers": {
    "unity-local": {
      "command": "uv",
      "args": ["run", "--project", "<EadonMCP>/server", "unity-mcp"],
      "env": { "UNITY_MCP_PORT": "11000" }
    },
    "unity-desktop": {
      "command": "npx",
      "args": ["mcp-remote", "http://192.168.1.102:8000/mcp", "--allow-http",
               "--header", "Authorization:${AUTH_HEADER}"],
      "env": { "AUTH_HEADER": "Bearer <desktop-token>" }
    }
  }
}
```

---

## Alternative: connecting a central MCP server to a remote Unity instance

If you prefer to run a single MCP server on Machine A and point it at Unity on
different machines, use `UNITY_MCP_URL` or `UNITY_MCP_HOST`, plus that
machine's bridge token in `UNITY_MCP_TOKEN`:

```json
{
  "mcpServers": {
    "unity-on-desktop": {
      "command": "uv",
      "args": ["run", "--project", "<EadonMCP>/server", "unity-mcp"],
      "env": {
        "UNITY_MCP_URL": "http://192.168.1.102:11000",
        "UNITY_MCP_TOKEN": "<bridge token of 192.168.1.102>"
      }
    }
  }
}
```

**This requires Unity's HTTP listener on Machine B to accept non-local
connections.** By default it only listens on `localhost`. To change that, on
Machine B:

1. Open **Project Settings > Eadon > MCP Bridge** and tick **Allow Remote Connections**.
2. Click **Restart Listener** on that page (or run **Eadon > MCP Bridge > Restart Listener**). The console warns that the
   bridge is listening on all network interfaces.
3. Run **Eadon > MCP Bridge > Copy Bridge Token** and paste the token into
   `UNITY_MCP_TOKEN` on Machine A.
4. Allow port 11000 (or your bridge port) through Machine B's firewall.

Every Unity project on a machine shares the same bridge token. **Eadon > MCP
Bridge > Regenerate Bridge Token** replaces it; clients on other machines then
need the new value.

⚠️ The bridge speaks plain HTTP, so the token travels unencrypted. Only use
this on a trusted network.

---

## Using a local LLM on another machine

A local LLM app that can act as an MCP client (LM Studio, for example) can
drive Unity on another machine in either of the two ways above. Say the LLM
runs on Machine C and Unity on Machine B.

**Through Machine B's MCP server.** Start the server on Machine B in network
mode (see *Setup on each development machine*), then add it to the LLM app's
MCP config on Machine C as a URL with the `Authorization` header (check first
with `remote_check.py` from Machine C). If the app cannot send headers, use the
`mcp-remote` entry shown for Claude Desktop instead:

```json
{
  "mcpServers": {
    "unity-on-b": {
      "url": "http://192.168.1.102:8000/mcp",
      "headers": { "Authorization": "Bearer <Machine B's UNITY_MCP_SERVER_TOKEN>" }
    }
  }
}
```

**Through a local MCP server talking to Machine B's bridge.** Clone EadonMCP
and run `uv sync` in its `server` folder on Machine C, tick Allow Remote Connections on Machine B (see
the alternative above), and add a `command` entry on Machine C:

```json
{
  "mcpServers": {
    "unity-on-b": {
      "command": "uv",
      "args": ["run", "--project", "<EadonMCP>/server", "unity-mcp"],
      "env": {
        "UNITY_MCP_URL": "http://192.168.1.102:11000",
        "UNITY_MCP_TOKEN": "<Machine B's bridge token>"
      }
    }
  }
}
```

The in-editor Agent Chat window needs neither: it always talks to the bridge
of the Unity it runs in. To use an LLM served by another machine there, set
the agent settings' **Base URL** to that machine (for example
`http://192.168.1.105:1234` for LM Studio).

---

## Troubleshooting

**Connection refused from Claude Desktop**
- Confirm the MCP server is running in network mode (`unity-mcp --transport streamable-http --host 0.0.0.0`)
- Confirm the URL and port match what's in claude_desktop_config.json (`/mcp` for streamable-http, `/sse` for sse)
- Run `remote_check.py` from Machine A: it tells a network problem from a token problem
- Check the firewall on the target machine allows the port

**Tools show as unavailable**
- Confirm Unity is open on the target machine with the MCP Bridge active
- Run `ping_unity` — it will show a clear error if Unity isn't reachable

**401 Unauthorized when connecting to the MCP server**
- The client isn't sending `Authorization: Bearer <token>`, or the token doesn't
  match the `UNITY_MCP_SERVER_TOKEN` the server was started with

**`[UNAUTHORIZED] Missing or wrong bridge token` from a tool**
- The MCP server reached Unity but sent no bridge token, or the wrong one
- Same machine: check `~/.unity-mcp/bridge-token` exists (Unity creates it when
  the bridge starts) and that `UNITY_MCP_TOKEN` isn't set to an old value
- Other machine: set `UNITY_MCP_TOKEN` to the token from **Eadon > MCP Bridge >
  Copy Bridge Token** on the Unity machine; after a regenerate, copy it again

**"Cannot reach Unity Editor" error from a tool**
- The MCP server started but Unity isn't responding. Unity may have crashed,
  the bridge may have stopped, or the port may have changed. Check the Unity
  Editor console.
