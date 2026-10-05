package com.deskink.android.transport

import com.deskink.android.protocol.ProtocolException
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class LanAuthenticatedDatagramTest {
    @Test
    fun roundtripAndTamperRejection() {
        val secret = ByteArray(LanAuthenticatedDatagram.SESSION_SECRET_BYTES) { it.toByte() }
        val frame = ByteArray(60) { (it * 3).toByte() }
        val datagram = LanAuthenticatedDatagram.encode(frame, secret)
        assertEquals(frame.size + LanAuthenticatedDatagram.AUTHENTICATION_TAG_BYTES, datagram.size)
        assertArrayEquals(frame, LanAuthenticatedDatagram.decode(datagram, secret))

        datagram[35] = (datagram[35].toInt() xor 1).toByte()
        assertThrows(ProtocolException::class.java) {
            LanAuthenticatedDatagram.decode(datagram, secret)
        }
    }
}
