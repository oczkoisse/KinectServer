import struct

def _recv_all(sock, size):
    result = b''
    while len(result) < size:
        data = sock.recv(size - len(result))
        if not data:
            raise EOFError("Error: Received only {} bytes into {} byte message".format(len(data), size))
        result += data
    return result
    
    
def _recv_frame(sock):
    """
    Return: frame_size (4:end), raw_frame which is excluding the frame size that was at the front
    """
    (frame_size,) = struct.unpack("<i", _recv_all(sock, 4))
    return (frame_size, _recv_all(sock, frame_size))
    
    
def _decode_header(raw_frame):
    endianness = "<"
    header_format = "qi"  # timestamp, frame_type
    header_size = struct.calcsize(endianness + header_format)
    header = struct.unpack_from(endianness + header_format, raw_frame)

    timestamp, frame_type = header
    
    offset = header_size
    return (timestamp, frame_type), offset
    
    
def _decode_tail(raw_frame, offset):
    endianness = "<"
    
    writer_header_format = "i"  # size of tail
    writer_header_offset = offset
    writer_data_length, = struct.unpack_from(endianness + writer_header_format, raw_frame, writer_header_offset)
    
    writer_data_offset = writer_header_offset + struct.calcsize(writer_header_format)
    if writer_data_length > 0:
        writer_data_format = "{}s".format(writer_data_length)
        writer_data, = struct.unpack_from(endianness + writer_data_format, raw_frame, writer_data_offset)
    else:
        writer_data = b""
    
    offset = writer_data_offset + writer_data_length
    return (writer_data,), offset
    
    
    
def read_frame(sock, decode_content):
    frame_size, raw_frame = _recv_frame(sock)
    header, offset = _decode_header(raw_frame)
    content, offset = decode_content(raw_frame, offset)
    tail, offset = _decode_tail(raw_frame, offset)
    
    assert offset == frame_size
        
    return header, content, tail
