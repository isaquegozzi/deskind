package com.deskink.android.transport

import com.deskink.android.protocol.ProtocolException
import java.nio.ByteBuffer
import java.nio.ByteOrder
import java.nio.charset.CodingErrorAction
import java.nio.charset.StandardCharsets

data class LanDiscoveryReply(
    val nonce: Long,
    val controlPort: Int,
    val certificateFingerprint: ByteArray,
    val hostName: String,
)

object LanDiscoveryProtocol {
    private const val MAGIC = 0x444B5344
    private const val VERSION = 1
    private const val QUERY_TYPE = 1
    private const val REPLY_TYPE = 2
    private const val HEADER_BYTES = 16
    private const val FINGERPRINT_BYTES = 32
    private const val MAXIMUM_HOST_NAME_BYTES = 63

    fun encodeQuery(nonce: Long): ByteArray = header(QUERY_TYPE, HEADER_BYTES, nonce).array()

    fun decodeQuery(packet: ByteArray): Long {
        validateHeader(packet, QUERY_TYPE, HEADER_BYTES)
        return ByteBuffer.wrap(packet).order(ByteOrder.LITTLE_ENDIAN).getLong(8)
    }

    fun encodeReply(reply: LanDiscoveryReply): ByteArray {
        if (reply.controlPort !in 1..65535) throw ProtocolException("Discovery control port is invalid")
        if (reply.certificateFingerprint.size != FINGERPRINT_BYTES)
            throw ProtocolException("Discovery fingerprint must be SHA-256")
        val name = reply.hostName.toByteArray(StandardCharsets.UTF_8)
        if (name.size !in 1..MAXIMUM_HOST_NAME_BYTES)
            throw ProtocolException("Discovery host name length is invalid")
        return header(REPLY_TYPE, 51 + name.size, reply.nonce)
            .putShort(reply.controlPort.toShort())
            .put(reply.certificateFingerprint)
            .put(name.size.toByte())
            .put(name)
            .array()
    }

    fun decodeReply(packet: ByteArray): LanDiscoveryReply {
        if (packet.size < 51) throw ProtocolException("Discovery reply is truncated")
        validateHeader(packet, REPLY_TYPE, packet.size)
        val buffer = ByteBuffer.wrap(packet).order(ByteOrder.LITTLE_ENDIAN)
        val nonce = buffer.getLong(8)
        val port = buffer.getShort(16).toInt() and 0xffff
        val fingerprint = packet.copyOfRange(18, 50)
        val nameBytes = packet[50].toInt() and 0xff
        if (port == 0 || nameBytes !in 1..MAXIMUM_HOST_NAME_BYTES || packet.size != 51 + nameBytes)
            throw ProtocolException("Discovery reply metadata is invalid")
        val hostName = try {
            StandardCharsets.UTF_8.newDecoder()
                .onMalformedInput(CodingErrorAction.REPORT)
                .onUnmappableCharacter(CodingErrorAction.REPORT)
                .decode(ByteBuffer.wrap(packet, 51, nameBytes))
                .toString()
        } catch (failure: Exception) {
            throw ProtocolException("Discovery host name is not UTF-8")
        }
        return LanDiscoveryReply(nonce, port, fingerprint, hostName)
    }

    private fun header(type: Int, length: Int, nonce: Long): ByteBuffer =
        ByteBuffer.allocate(length).order(ByteOrder.LITTLE_ENDIAN)
            .putInt(MAGIC)
            .put(VERSION.toByte())
            .put(type.toByte())
            .putShort(length.toShort())
            .putLong(nonce)

    private fun validateHeader(packet: ByteArray, type: Int, expectedLength: Int) {
        if (packet.size != expectedLength) throw ProtocolException("Discovery packet length mismatch")
        val buffer = ByteBuffer.wrap(packet).order(ByteOrder.LITTLE_ENDIAN)
        if (buffer.int != MAGIC) throw ProtocolException("Discovery magic mismatch")
        if ((buffer.get().toInt() and 0xff) != VERSION) throw ProtocolException("Discovery version mismatch")
        if ((buffer.get().toInt() and 0xff) != type) throw ProtocolException("Discovery packet type mismatch")
        if ((buffer.short.toInt() and 0xffff) != packet.size)
            throw ProtocolException("Discovery header length mismatch")
    }
}
