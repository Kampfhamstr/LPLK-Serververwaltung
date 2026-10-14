"""Loopback-only SSH integration fixture. Uses synthetic credentials, never a real server."""
import base64
import hashlib
import logging
import socket
import subprocess
import sys
import threading
import time

import paramiko

logging.getLogger("paramiko").addHandler(logging.NullHandler())
logging.getLogger("paramiko").propagate = False
password = "  local-ssh-fixture-password-ONLY  "
host_key = paramiko.RSAKey.generate(2048)
fingerprint = "SHA256:" + base64.b64encode(hashlib.sha256(host_key.asbytes()).digest()).decode().rstrip("=")
records = []
stop = threading.Event()


class Fixture(paramiko.ServerInterface):
    def __init__(self, record):
        self.record = record

    def get_allowed_auths(self, username):
        return "password"

    def check_auth_password(self, username, supplied):
        self.record["auth_calls"] += 1
        return paramiko.AUTH_SUCCESSFUL if username == "root" and supplied == password else paramiko.AUTH_FAILED

    def check_channel_request(self, kind, channel_id):
        return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

    def check_channel_exec_request(self, channel, command):
        self.record["commands"].append(command.decode())

        def send_result():
            time.sleep(0.05)
            try:
                channel.sendall(("Benutzer: root\nUID: 0\nHostname: ssh-test-fixture\n" + password + "\n").encode())
                channel.send_exit_status(0)
                channel.shutdown_write()
                time.sleep(0.05)
                channel.close()
            except (OSError, EOFError):
                pass

        threading.Thread(target=send_result, daemon=True).start()
        return True


listener = socket.socket()
listener.bind(("127.0.0.1", 0))
listener.listen(10)
listener.settimeout(0.2)
port = listener.getsockname()[1]


def handle(client_socket, record):
    transport = paramiko.Transport(client_socket)
    transport.add_server_key(host_key)
    channels = []
    try:
        transport.start_server(server=Fixture(record))
        while transport.is_active() and not stop.is_set():
            channel = transport.accept(0.2)
            if channel:
                channels.append(channel)
    except (EOFError, OSError, paramiko.SSHException):
        pass
    finally:
        transport.close()


def accept_clients():
    while not stop.is_set():
        try:
            client_socket, _ = listener.accept()
        except socket.timeout:
            continue
        except OSError:
            break
        record = {"auth_calls": 0, "commands": []}
        records.append(record)
        threading.Thread(target=handle, args=(client_socket, record), daemon=True).start()


threading.Thread(target=accept_clients, daemon=True).start()
try:
    completed = subprocess.run([sys.argv[1], sys.argv[2], str(port), fingerprint], capture_output=True, text=True, timeout=90)
    print(completed.stdout, end="")
    if completed.returncode:
        print(completed.stderr, end="", file=sys.stderr)
        raise SystemExit(completed.returncode)
    assert len(records) == 5, f"Unexpected number of SSH sessions: {len(records)}"
    assert records[0]["auth_calls"] == 0, "Host-key probe transmitted a password"
    assert records[2]["auth_calls"] == 0, "Changed host key received a password"
    assert len(records[4]["commands"]) == 5, "Unexpected diagnostic command count"
    assert all(not any(word in command for word in ("restart", "reboot", "apt ", "rm ")) for command in records[4]["commands"])
    print("PASS: server confirmed that no password was sent during probing or after a host-key mismatch.")
finally:
    stop.set()
    listener.close()
