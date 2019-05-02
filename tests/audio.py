import random
import socket
import struct
import sys
import numpy as np
import wave

src_addr = 'localhost'
src_port = 8000

stream_id = 8;

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
        sock.sendall(struct.pack('<iBi', 5, 1, stream_id));
    except:
        print("Error: Stream rejected")
        return None
    print("Successfully connected to host")
    return sock


def decode_frame(raw_frame):
    # The format is given according to the following assumption of network data

    # Expect little endian byte order
    endianness = "<"

    # [ commonTimestamp | frame type | sample count
    header_format = "qii"

    timestamp, frame_type, sample_count = struct.unpack(endianness + header_format, raw_frame[:struct.calcsize(header_format)])

    samples_format = str(sample_count) + "f" 
    # Unpack the raw frame into individual pieces of data as a tuple
    frame_pieces = struct.unpack(endianness + samples_format, raw_frame[struct.calcsize(header_format):])
    
    return (timestamp, frame_type, sample_count, list(frame_pieces))



def recv_all(sock, size):
    result = b''
    while len(result) < size:
        data = sock.recv(size - len(result))
        if not data:
            raise EOFError("Error: Received only {} bytes into {} byte message".format(len(data), size))
        result += data
    return result


def recv_audio_frame(sock):
    """
    To read each stream frame from the server
    """
    (load_size,) = struct.unpack("<i", recv_all(sock, struct.calcsize("<i")))
    #print(load_size)
    return recv_all(sock, load_size)
    
if __name__ == '__main__':
    s = connect()
    if s is None:
        sys.exit(0)
        
    sign = lambda x: 1 if x >= 0.0 else -1
    
    do_write = True if len(sys.argv) > 1 and sys.argv[1] == '--write' else False

    if do_write:
        out_file = sys.argv[2] if len(sys.argv) > 2 else 'out.wav' 
        outwav = wave.open(out_file, 'wb')
        outwav.setparams((1, 2, 16000, 0, "NONE", "NONE"))
    
    while True:
        try:
            f = recv_audio_frame(s)
            timestamp, frame_type, sample_count, samples = decode_frame(f)
            # PCM-16
            samples = [ int(sm * 32767) if -1.0 <= sm <= 1.0 else (sign(sm) * 32767 ) for sm in samples ]
            if do_write:
                outwav.writeframes(struct.pack('<' + str(len(samples)) + 'h', *samples))
            print(timestamp, frame_type, sample_count, random.sample(samples, 5), max(samples))
        except:
            
            
            break
        print("\n\n")
       
    s.close()
    if do_write:
        outwav.close()
