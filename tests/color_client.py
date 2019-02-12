#!/usr/bin/env python

import socket, sys, struct
import time
import numpy as np
import matplotlib.pyplot as plt
import imageio

src_addr = 'cwc2'
src_port = 8000

stream_id = 2;

def connect():
    """
    Connect to a specific port
    """

    sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

    try:
        sock.connect((src_addr, src_port))
    except:
        print ("Error connecting to {}:{}".format(src_addr, src_port))
        return None
    try:
        print ("Sending stream info")
        sock.sendall(struct.pack('<i', stream_id));
    except:
        print ("Error: Stream rejected")
        return None
    print ("Successfully connected to host")
    return sock
    

# timestamp | frame type | stride | width | height | color_data
def decode_frame(raw_frame):
    # Expect little endian byte order
    endianness = "<"

    # In each frame, a header is transmitted
    # Timestamp | frame type | width | height
    header_format = "qiiii"
    header_size = struct.calcsize(endianness + header_format)
    header = struct.unpack(endianness + header_format, raw_frame[:header_size])

    timestamp, frame_type, stride, width, height = header
    
    color_data_format = str(stride * height) + "B"

    # Removed called to struct.unpack_from and decoded the jpeg file
    # Relies on the imageio library 
    color_data = imageio.imread(raw_frame[header_size:], "jpeg")
    
    return (timestamp, frame_type, stride, width, height, list(color_data))

def format_as_image(raw_image, width, height):
    # Convert to RGBA from BGRA
    image_as_pixels = np.array([ (raw_image[i+2], raw_image[i+1], raw_image[i], raw_image[i+3]) for i in range(0, len(raw_image), 4)] , dtype=np.float)
    image_as_pixels /= 255.0
    
    return image_as_pixels.reshape((height, width, 3))

def recv_all(sock, size):
    result = b''
    while len(result) < size:
        data = sock.recv(size - len(result))
        if not data:
            raise EOFError("Error: Received only {} bytes into {} byte message".format(len(data), size))
        result += data
    return result

def recv_color_frame(sock):
    """
    Experimental function to read each stream frame from the server
    """
    (frame_size,) = struct.unpack("<i", recv_all(sock, 4))

    return recv_all(sock, frame_size) 
    

if __name__ == '__main__':

    s = connect()
    if s is None:
        sys.exit(0)
        
    i = 0
    avg_frame_time = 0.0
    do_plot = True if len(sys.argv) > 1 and sys.argv[1] == '--plot' else False
    
    while True:
        try:
            t_begin = time.time()
            f = recv_color_frame(s)
            t_end = time.time()
        except:
            s.close()
            break
        print ("Time taken for this frame: {}".format(t_end - t_begin))
        avg_frame_time += (t_end - t_begin)
        timestamp, frame_type, stride, width, height, color_data = decode_frame(f)
        print (timestamp, frame_type, stride, width, height)

        if do_plot and i % 20 == 0:
            img = format_as_image(color_data, width, height)
            print (img[0,0])
            plt.imshow(img)
            plt.show()
            
        print ("\n\n")
        i += 1

    print ("Total frame time: {}".format(avg_frame_time))

    avg_frame_time /= i
    
    print ("Average frame time over {} frames: {}".format(i, avg_frame_time))

    s.close()
    sys.exit(0)
