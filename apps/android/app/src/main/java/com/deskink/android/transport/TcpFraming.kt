package com.deskink.android.transport

import com.deskink.android.protocol.ProtocolException
import java.io.EOFException
import java.io.InputStream
import java.io.OutputStream

object TcpFraming {
    fun write(output: OutputStream, frame: ByteArray) {
        val length = frame.size
        output.write(length and 0xff)
        output.write(length ushr 8 and 0xff)
        output.write(length ushr 16 and 0xff)
        output.write(length ushr 24 and 0xff)
        output.write(frame)
        output.flush()
    }

    fun read(input: InputStream, maximumFrameBytes: Int): ByteArray {
        val length = readByte(input) or
            (readByte(input) shl 8) or
            (readByte(input) shl 16) or
            (readByte(input) shl 24)
        if (length !in 1..maximumFrameBytes) {
            throw ProtocolException("Stream frame length outside limits: $length")
        }
        val frame = ByteArray(length)
        var offset = 0
        while (offset < length) {
            val read = input.read(frame, offset, length - offset)
            if (read < 0) throw EOFException("Stream ended inside frame")
            offset += read
        }
        return frame
    }

    private fun readByte(input: InputStream): Int {
        val value = input.read()
        if (value < 0) throw EOFException("Stream ended inside frame prefix")
        return value
    }
}
