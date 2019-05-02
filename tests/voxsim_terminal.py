import socket
import struct

frameIds = {
    'color': 2,
    'speech': 4,
    'audio': 8,
    'depth': 16,
    'body': 32,
    'lhdepth': 64,
    'rhdepth': 128,
    'headdepth': 256
}

def connect(hostname, port):
    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        sock.connect((hostname, port))
        return sock
    except:
        print("Error: Could not connect to {}:{}".format(hostname, port))
        return None

def register(sock):
    try:
        sock.sendall(struct.pack('<iB', 1, 2))
    except:
        print("Error: Registration failed")
        return False
    print("Registration succeeded")
    return True


def send_command(sock: socket.socket, command_dict: dict):
    msg = struct.pack('<BB', 3, len(command_dict))
    for frameId, command in command_dict.items():
        command = command.encode('ascii')
        msg += struct.pack('<ii{}s'.format(len(command)), frameId, len(command), command)

    msg = struct.pack('<i', len(msg)) + msg
    try:
        sock.sendall(msg)
    except:
        print("Failed to send commands")


def parse_command(command: str):
    command_dict = {}
    for subcommand in command.split(';'):
        recognizer, command = subcommand.split(',')
        recognizerId = frameIds[recognizer.strip().lower()]
        command = command.strip()
        command_dict[recognizerId] = command
    return command_dict


if __name__ == '__main__':
    s = connect('localhost', 8000)
    success = register(s)
    if success:
        while True:
            try:
                cmd = input()
            except EOFError:
                break

            command_dict = None
            try:
                command_dict = parse_command(cmd)
            except:
                print("Unable to parse command: {}".format(cmd))
                continue

            if command_dict:
                send_command(s, command_dict)

    if s is not None:
        s.close()
