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

# Timestamp | frame type | command_length | command
def decode_frame(raw_frame):
    
    # Expect little endian byte order
    endianness = "<"

    # In each frame, a header is transmitted
    # Timestamp | frame type | command_length
    header_format = "qii"
    
    header_size = struct.calcsize(endianness + header_format)
    header = struct.unpack(endianness + header_format, raw_frame[:header_size])

    timestamp, frame_type, command_length = header
    
    #print timestamp, frame_type, command_length
    
    command_format = str(command_length) + "s"
    
    command = struct.unpack_from(endianness + command_format, raw_frame, header_size)[0]
    
    return (timestamp, frame_type, command)

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
    #print frame_size
    return recv_all(sock, frame_size) 
    

if __name__ == '__main__':

    s = connect()
    if s is None:
        sys.exit(0)
    
    while True:
        try:
            f = recv_speech_frame(s)
        except:
            break
        timestamp, frame_type, command = decode_frame(f)
        if command != "":
            print timestamp, frame_type, command
            print "\n\n"


    s.close()
    sys.exit(0)
