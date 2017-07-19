#!/usr/bin/env python

import socket, sys, struct

src_addr = '127.0.0.1'
src_port = 8000

stream_id = 4;

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
    
def decode_frame(raw_frame):
    
    # Expect network byte order
    endianness = "<"

    # In each frame, a header is transmitted
    # Timestamp | frame type | command_length
    header_format = "qii"
    
    header_size = struct.calcsize(endianness + header_format)
    header = struct.unpack(endianness + header_format, raw_frame[:header_size])

    timestamp, frame_type, command_length = header
    
    command_format = str(command_length) + "B"
    
    command_data = struct.unpack_from(endianness + command_format, raw_frame, header_size)
    
    return (timestamp, frame_type, width, height, command_data.decode('ascii'))

def recv_all(sock, size):
    result = b''
    while len(result) < size:
        data = sock.recv(size - len(result))
        if not data:
            raise EOFError("Error: Received only {} bytes into {} byte message".format(len(data), size))
        result += data
    return result

def recv_speech_frame(sock):
    """
    Experimental function to read each stream frame from the server
    """
    (frame_size,) = struct.unpack("<i", recv_all(sock, 4))

    return recv_all(sock, frame_size) 
    

# By default read 100 frames
if __name__ == '__main__':

    # Time the network performance 
    s = connect()

    while True:
        try:
            f = recv_speech_frame(s)
        except:
            s.close()
            break
        timestamp, frame_type, command = decode_frame(f)
        print timestamp, frame_type, command
        print "\n\n"


    s.close()
    sys.exit(0)
