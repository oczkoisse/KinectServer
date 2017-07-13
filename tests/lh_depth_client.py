#!/usr/bin/env python

import socket, sys, struct
import time
import numpy as np
import matplotlib.pyplot as plt

src_addr = '127.0.0.1'
src_port = 8000

stream_id = 64;

def connect():
    """
    Connect to a specific port
    """

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

    try:
        sock.connect((src_addr, src_port))
    except:
        print "Error connecting to {}:{}".format(src_addr, src_port)
        return None
    try:
		print "Sending stream info"
		sock.sendall(struct.pack('<i', stream_id));
    except:
        print "Error: Stream rejected"
        return None
    print "Successfully connected to host"
    return sock
    

# timestamp (long) | depth_hands_count(int) | left_hand_height (int) | left_hand_width (int) |
# right_hand_height (int) | right_hand_width (int)| left_hand_pos_x (float) | left_hand_pos_y (float) | ... |
# left_hand_depth_data ([left_hand_width * left_hand_height]) |
# right_hand_depth_data ([right_hand_width * right_hand_height])
def decode_frame(raw_frame):
    
    # Expect network byte order
    endianness = "<"

    # In each frame, a header is transmitted
    header_format = "qiiiff"
    header_size = struct.calcsize(endianness + header_format)
    header = struct.unpack(endianness + header_format, raw_frame[:header_size])

    timestamp, frame_type, width, height, posx, posy = header
    
    depth_data_format = str(width * height) + "H"
    
    depth_data = struct.unpack_from(endianness + depth_data_format, raw_frame, header_size)
    
    return (timestamp, frame_type, width, height, posx, posy, list(depth_data))

def recv_all(sock, size):
    result = b''
    while len(result) < size:
        data = sock.recv(size - len(result))
        if not data:
            raise EOFError("Error: Received only {} bytes into {} byte message".format(len(data), size))
        result += data
    return result

def recv_depth_frame(sock):
    """
    Experimental function to read each stream frame from the server
    """
    (frame_size,) = struct.unpack("<i", recv_all(sock, 4))

    return recv_all(sock, frame_size) 
    

# By default read 100 frames
if __name__ == '__main__':

    # Time the network performance 
    s = connect()
    
    i = 0

    avg_frame_time = 0.0

    while True:
        try:
            t_begin = time.time()
            f = recv_depth_frame(s)
            t_end = time.time()
        except:
            break
        print "Time take for this frame: {}".format(t_end - t_begin)
        avg_frame_time += (t_end - t_begin)
        timestamp, frame_type, width, height, posx, posy, depth_data = decode_frame(f)
        print timestamp, width, height
        do_plot = False
        
        if do_plot and i % 20 == 0:
            image = np.array(depth_data).reshape((height, width))
            im = plt.imshow(image, cmap='gray')
            plt.show()

        print "\n\n"
        i += 1

    if i != 0:
        print "Total frame time: {}".format(avg_frame_time)
        avg_frame_time /= i
        print "Average frame time over {} frames: {}".format(i, avg_frame_time)

    s.close()
    sys.exit(0)
