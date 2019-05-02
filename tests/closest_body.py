import socket
import struct
import time
import sys

from decode import read_frame

src_addr = 'localhost'
src_port = 8000

stream_id = 32

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


def decode_content(raw_frame, offset):
    """
    raw_frame: frame starting from 4 to end (4 for length field)
    offset: index where header ends; header is header_l, timestamp, frame_type
    """
    endianness = "<"

    content_header_format = "BB"  # Tracked body count | Engaged
    content_header_size = struct.calcsize(endianness + content_header_format)
    content_header = struct.unpack_from(endianness + content_header_format, raw_frame, offset)

    tracked_body_count, engaged = content_header

    # For each body, a header is transmitted
    # TrackingId | HandLeftConfidence | HandLeftState | HandRightConfidence | HandRightState ]
    body_format = "Q4B"

    # For each of the 25 joints, the following info is transmitted
    # [ JointType | TrackingState | Position.X | Position.Y | Position.Z | Orientation.W | Orientation.X | Orientation.Y | Orientation.Z ]
    joint_format = "BB7f"

    frame_format = body_format + (joint_format * 25)

    # Unpack the raw frame into individual pieces of data as a tuple
    frame_pieces = struct.unpack_from(endianness + (frame_format * engaged), raw_frame, offset + content_header_size)

    # decoded = (tracked_body_count, engaged) + frame_pieces
    decoded = (tracked_body_count, engaged, frame_pieces)
    offset = offset + content_header_size + struct.calcsize(
        endianness + frame_format * engaged)  # new offset from where tail starts
    return decoded, offset

    
if __name__ == '__main__':
    s = connect()
    if s is None:
        sys.exit(0)
    
    start_time = time.time()
    count = 0
    while True:
        try:
            (timestamp, frame_type), (tracked_body_count, engaged, frame_pieces), (writer_data,) = read_frame(s, decode_content)
        except:
            s.close()
            break

        print("{:<20d} {:<4d} {:<4d} {:<5s} '{}'".format(timestamp, frame_type, tracked_body_count, str(engaged > 0), writer_data.decode('ascii')))
        print("\n\n")

        count += 1
        if count == 100:
            print('='*30)
            print('FPS: ', 100.0 / (time.time() - start_time))
            print('='*30)
            start_time = time.time()
            count = 0
