#!/usr/bin/env python

import socket
import sys
import struct
from decode import read_frame

src_addr = 'localhost'
src_port = 8000

stream_id = 1024


def connect():
    """
    Connect to a specific port
    """

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

    try:
        sock.connect((src_addr, src_port))
    except:
        print("Error connecting to {}:{}".format(src_addr, src_port))
        return None
    try:
        print("Sending stream info")
        sock.sendall(struct.pack('<iBi', 5, 1, stream_id))
    except:
        print("Error: Stream rejected")
        return None
    print("Successfully connected to host")
    return sock


# Timestamp | frame type | command_length | command

def decode_content(raw_frame, offset):
    endianness = "<"

    # 4 bytes and 3 doubles
    content_format = "B" * 4 + "d" * 3
    content_size = struct.calcsize(endianness + content_format)
    content = struct.unpack_from(endianness + content_format, raw_frame, offset)
    return content, offset + content_size


if __name__ == '__main__':

    s = connect()
    if s is None:
        sys.exit(0)

    while True:
        try:
            (timestamp, frame_type), (faceFound, engaged, lookingAway, wearingGlasses, pitch, yaw, roll), (writer_data,) = read_frame(s, decode_content)
        except:
            s.close()
            break

        print(timestamp, frame_type)
        print("Face Found: {}, Engaged: {}, Looking Away: {}, Wearing Glasses: {}".format(faceFound, engaged, lookingAway, wearingGlasses))
        print("Pitch: {}, Yaw: {}, Roll: {}".format(pitch, yaw, roll))
        print("\n\n")
