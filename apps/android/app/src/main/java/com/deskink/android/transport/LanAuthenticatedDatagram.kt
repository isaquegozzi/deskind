package com.deskink.android.transport

import com.deskink.android.protocol.ProtocolException
import com.deskink.android.protocol.ProtocolV1Codec
import java.security.MessageDigest
import javax.crypto.Mac
import javax.crypto.spec.SecretKeySpec

object LanAuthenticatedDatagram {
    const val SESSION_SECRET_BYTES = 32
    const val AUTHENTICATION_TAG_BYTES = 16
    private const val MAXIMUM_INPUT_FRAME_BYTES = 1_200

    fun encode(logicalFrame: ByteArray, sessionSecret: ByteArray): ByteArray {
        validate(logicalFrame, sessionSecret)
        val tag = hmac(logicalFrame, sessionSecret)
        return logicalFrame + tag.copyOf(AUTHENTICATION_TAG_BYTES)
    }

    fun decode(datagram: ByteArray, sessionSecret: ByteArray): ByteArray {
        if (sessionSecret.size != SESSION_SECRET_BYTES) {
            throw ProtocolException("LAN session secret must be 32 bytes")
        }
        val frameBytes = datagram.size - AUTHENTICATION_TAG_BYTES
        if (frameBytes !in ProtocolV1Codec.HEADER_BYTES..MAXIMUM_INPUT_FRAME_BYTES) {
            throw ProtocolException("LAN datagram length is invalid")
        }
        val frame = datagram.copyOfRange(0, frameBytes)
        val receivedTag = datagram.copyOfRange(frameBytes, datagram.size)
        val expectedTag = hmac(frame, sessionSecret).copyOf(AUTHENTICATION_TAG_BYTES)
        if (!MessageDigest.isEqual(receivedTag, expectedTag)) {
            throw ProtocolException("LAN authentication tag mismatch")
        }
        return frame
    }

    private fun validate(logicalFrame: ByteArray, sessionSecret: ByteArray) {
        if (sessionSecret.size != SESSION_SECRET_BYTES) {
            throw ProtocolException("LAN session secret must be 32 bytes")
        }
        if (logicalFrame.size !in ProtocolV1Codec.HEADER_BYTES..MAXIMUM_INPUT_FRAME_BYTES) {
            throw ProtocolException("LAN logical frame length is invalid")
        }
    }

    private fun hmac(frame: ByteArray, sessionSecret: ByteArray): ByteArray =
        Mac.getInstance("HmacSHA256").run {
            init(SecretKeySpec(sessionSecret, "HmacSHA256"))
            doFinal(frame)
        }
}
