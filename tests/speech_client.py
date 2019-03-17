#!/usr/bin/env python

import socket
import sys
import struct
from decode import read_frame

src_addr = 'localhost'
src_port = 8000

stream_id = 4


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
    """
    raw_frame: frame starting from 4 to end (4 for length)
    offset: index where header ends  # header is header_l, timestamp, frame_type
    """
    endianness = "<"

    content_header_format = "i"  # command_length
    content_header_size = struct.calcsize(endianness + content_header_format)
    content_header, = struct.unpack_from(endianness + content_header_format, raw_frame, offset)

    command_length = content_header
    command_format = str(command_length) + "s"

    command = struct.unpack_from(endianness + command_format, raw_frame, offset + content_header_size)[0]
    command = command.decode('ascii')

    offset = offset + content_header_size + struct.calcsize(
        endianness + command_format)  # new offset from where tail starts
    return (command_length, command), offset


if __name__ == '__main__':

    s = connect()
    if s is None:
        sys.exit(0)

    while True:
        try:
            (timestamp, frame_type), (command_length, command), (writer_data,) = read_frame(s, decode_content)
        except:
            s.close()
            break

        if len(command) > 0 or len(writer_data) > 0:
            print("{:<20d} {:<4d} '{}' '{}'".format(timestamp, frame_type, command, writer_data.decode('ascii')))
            print("\n\n")
