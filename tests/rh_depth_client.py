#!/usr/bin/env python

import socket, sys, struct
import time
import numpy as np
import matplotlib.pyplot as plt
from decode import read_frame

src_addr = 'localhost'
src_port = 8000

stream_id = 128


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


"""
# timestamp (long) | depth_hands_count(int) | left_hand_height (int) | left_hand_width (int) |
# right_hand_height (int) | right_hand_width (int)| left_hand_pos_x (float) | left_hand_pos_y (float) | ... |
# left_hand_depth_data ([left_hand_width * left_hand_height]) |
# right_hand_depth_data ([right_hand_width * right_hand_height])
"""


def decode_content(raw_frame, offset):
    """
    raw_frame: frame starting from 4 to end (4 for length field)
    offset: index where header ends; header is header_l, timestamp, frame_type
    """
    endianness = "<"

    content_header_format = "iiff"  # width, height, posx, posy
    content_header_size = struct.calcsize(endianness + content_header_format)
    content_header = struct.unpack_from(endianness + content_header_format, raw_frame, offset)

    width, height, posx, posy = content_header
    # print(width, height, posx, posy)

    depth_data_format = str(width * height) + "H"
    depth_data = struct.unpack_from(endianness + depth_data_format, raw_frame, offset + content_header_size)

    offset = offset + content_header_size + struct.calcsize(
        endianness + depth_data_format)  # new offset from where tail starts
    return (width, height, posx, posy, list(depth_data)), offset


if __name__ == '__main__':
    s = connect()
    if s is None:
        sys.exit(0)

    do_plot = True if len(sys.argv) > 1 and sys.argv[1] == '--plot' else False

    start_time = time.time()
    count = 0
    while True:
        try:
            (timestamp, frame_type), (width, height, posx, posy, depth_data), (writer_data,) = read_frame(
                s, decode_content)
        except:
            s.close()
            break

        print("{:<20d} {:<4d} {:<4d} {:<4d} '{}'".format(timestamp, frame_type, width, height, writer_data))

        count += 1
        if count == 100:
            print('=' * 30)
            print('FPS: ', 100.0 / (time.time() - start_time))
            print('=' * 30)
            start_time = time.time()
            count = 0

        if do_plot and count % 20 == 0 and height * width > 0:
            image = np.array(depth_data).reshape((height, width))
            im = plt.imshow(image, cmap='gray')
            plt.show()
