package com.deskink.android.transport

import com.deskink.android.protocol.ProtocolException
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class LanDiscoveryProtocolTest {
    @Test
    fun queryAndReplyRoundTrip() {
        val nonce = 0x1020304050607080L
        assertEquals(nonce, LanDiscoveryProtocol.decodeQuery(LanDiscoveryProtocol.encodeQuery(nonce)))
        val fingerprint = ByteArray(32) { it.toByte() }
        val encoded = LanDiscoveryProtocol.encodeReply(
            LanDiscoveryReply(nonce, 27185, fingerprint, "DESK-PC"),
        )
        val reply = LanDiscoveryProtocol.decodeReply(encoded)
        assertEquals(nonce, reply.nonce)
        assertEquals(27185, reply.controlPort)
        assertEquals("DESK-PC", reply.hostName)
        assertArrayEquals(fingerprint, reply.certificateFingerprint)
        assertEquals("0001-0203", LanTransport.comparisonCode(fingerprint))
    }

    @Test
    fun rejectsTamperedLength() {
        val packet = LanDiscoveryProtocol.encodeReply(
            LanDiscoveryReply(7, 27185, ByteArray(32), "PC"),
        )
        packet[6] = (packet[6] - 1).toByte()
        assertThrows(ProtocolException::class.java) { LanDiscoveryProtocol.decodeReply(packet) }
    }
}
