package com.deskink.android.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder

data class HelloAck(
    val sessionId: Long,
    val inputBindToken: ByteArray,
    val monitorCount: Int,
    val selectedMonitorIndex: Int,
)

data class LanPairAck(
    val sessionId: Long,
    val sessionSecret: ByteArray,
    val monitorCount: Int,
    val selectedMonitorIndex: Int,
    val udpInputPort: Int,
)

data class Pong(
    val sessionId: Long,
    val clientTimeUs: Long,
    val hostReceiveTimeUs: Long,
    val hostSendTimeUs: Long,
)

object ControlProtocolV1 {
    const val HELLO_TYPE = 0x01
    const val HELLO_ACK_TYPE = 0x02
    const val INPUT_BIND_TYPE = 0x03
    const val PING_TYPE = 0x04
    const val PONG_TYPE = 0x05
    const val SET_CLOCK_SYNC_TYPE = 0x07
    const val SET_MOUSE_MODE_TYPE = 0x20
    const val SET_MONITOR_TYPE = 0x21
    const val SET_OVERLAY_TOOL_TYPE = 0x22
    const val OVERLAY_COMMAND_TYPE = 0x23
    const val SET_OVERLAY_MODE_TYPE = 0x24
    const val SET_OUTPUT_MODE_TYPE = 0x25
    const val EXECUTE_SHORTCUT_TYPE = 0x26
    const val LAN_PAIR_CHALLENGE_TYPE = 0x30
    const val LAN_PAIR_CONFIRM_TYPE = 0x31
    const val LAN_PAIR_ACK_TYPE = 0x32
    const val BIND_TOKEN_BYTES = 16

    private const val MAGIC = 0x494B5344
    private const val MAJOR = 1
    private const val MINOR = 0

    fun encodeHello(clientNonce: Long): ByteArray = encode(
        messageType = HELLO_TYPE,
        sessionId = 0,
        payloadBytes = Long.SIZE_BYTES,
    ) { putLong(clientNonce) }

    fun decodeHelloAck(frame: ByteArray): HelloAck {
        val payload = validate(frame, HELLO_ACK_TYPE, BIND_TOKEN_BYTES + 2)
        val sessionId = ByteBuffer.wrap(frame, 12, Long.SIZE_BYTES)
            .order(ByteOrder.LITTLE_ENDIAN)
            .long
        if (sessionId == 0L) throw ProtocolException("HELLO_ACK session is zero")
        val monitorCount = payload[BIND_TOKEN_BYTES].toInt() and 0xff
        val selectedMonitorIndex = payload[BIND_TOKEN_BYTES + 1].toInt() and 0xff
        if (monitorCount == 0 || selectedMonitorIndex >= monitorCount) {
            throw ProtocolException("Invalid monitor selection in HELLO_ACK")
        }
        return HelloAck(
            sessionId,
            payload.copyOfRange(0, BIND_TOKEN_BYTES),
            monitorCount,
            selectedMonitorIndex,
        )
    }

    fun encodeInputBind(sessionId: Long, token: ByteArray): ByteArray {
        if (sessionId == 0L) throw ProtocolException("INPUT_BIND session is zero")
        if (token.size != BIND_TOKEN_BYTES) throw ProtocolException("Invalid bind token length")
        return encode(INPUT_BIND_TYPE, sessionId, BIND_TOKEN_BYTES) { put(token) }
    }

    fun encodePing(sessionId: Long, clientTimeUs: Long): ByteArray {
        if (sessionId == 0L) throw ProtocolException("PING session is zero")
        return encode(PING_TYPE, sessionId, Long.SIZE_BYTES) { putLong(clientTimeUs) }
    }

    fun decodePong(frame: ByteArray): Pong {
        val payload = validate(frame, PONG_TYPE, Long.SIZE_BYTES * 3)
        val sessionId = ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN).getLong(12)
        if (sessionId == 0L) throw ProtocolException("PONG session is zero")
        val values = ByteBuffer.wrap(payload).order(ByteOrder.LITTLE_ENDIAN)
        return Pong(sessionId, values.long, values.long, values.long)
    }

    fun encodeSetClockSync(sessionId: Long, hostMinusClientUs: Long, uncertaintyUs: Long): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_CLOCK_SYNC session is zero")
        if (uncertaintyUs !in 0..0xffff_ffffL) throw ProtocolException("Clock uncertainty is outside u32")
        return encode(SET_CLOCK_SYNC_TYPE, sessionId, 12) {
            putLong(hostMinusClientUs)
            putInt(uncertaintyUs.toInt())
        }
    }

    fun encodeSetMouseMode(sessionId: Long, penScrollEnabled: Boolean): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_MOUSE_MODE session is zero")
        return encode(SET_MOUSE_MODE_TYPE, sessionId, 1) {
            put(if (penScrollEnabled) 1.toByte() else 0.toByte())
        }
    }

    fun encodeSetMonitor(sessionId: Long, monitorIndex: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_MONITOR session is zero")
        if (monitorIndex !in 0..255) throw ProtocolException("Monitor index is outside u8")
        return encode(SET_MONITOR_TYPE, sessionId, 1) { put(monitorIndex.toByte()) }
    }

    fun encodeSetOutputMode(sessionId: Long, outputMode: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_OUTPUT_MODE session is zero")
        if (outputMode !in 0..2) throw ProtocolException("Unknown output mode")
        return encode(SET_OUTPUT_MODE_TYPE, sessionId, 1) { put(outputMode.toByte()) }
    }

    fun encodeSetOverlayTool(sessionId: Long, tool: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_OVERLAY_TOOL session is zero")
        if (tool !in 0..2) throw ProtocolException("Unknown overlay tool")
        return encode(SET_OVERLAY_TOOL_TYPE, sessionId, 1) { put(tool.toByte()) }
    }

    fun encodeOverlayCommand(sessionId: Long, command: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("OVERLAY_COMMAND session is zero")
        if (command !in 0..2) throw ProtocolException("Unknown overlay command")
        return encode(OVERLAY_COMMAND_TYPE, sessionId, 1) { put(command.toByte()) }
    }

    fun encodeSetOverlayMode(sessionId: Long, mode: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("SET_OVERLAY_MODE session is zero")
        if (mode !in 0..2) throw ProtocolException("Unknown overlay mode")
        return encode(SET_OVERLAY_MODE_TYPE, sessionId, 1) { put(mode.toByte()) }
    }

    fun encodeExecuteShortcut(sessionId: Long, command: Int): ByteArray {
        if (sessionId == 0L) throw ProtocolException("EXECUTE_SHORTCUT session is zero")
        if (command !in 0..7) throw ProtocolException("Unknown shortcut command")
        return encode(EXECUTE_SHORTCUT_TYPE, sessionId, 1) { put(command.toByte()) }
    }

    fun decodeLanPairChallenge(frame: ByteArray): ByteArray {
        val payload = validate(frame, LAN_PAIR_CHALLENGE_TYPE, 32)
        if (ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN).getLong(12) != 0L) {
            throw ProtocolException("LAN_PAIR_CHALLENGE session must be zero")
        }
        return payload
    }

    fun encodeLanPairConfirm(certificateFingerprint: ByteArray): ByteArray {
        if (certificateFingerprint.size != 32) {
            throw ProtocolException("LAN certificate fingerprint must be SHA-256")
        }
        return encode(LAN_PAIR_CONFIRM_TYPE, 0, 32) { put(certificateFingerprint) }
    }

    fun decodeLanPairAck(frame: ByteArray): LanPairAck {
        val payload = validate(frame, LAN_PAIR_ACK_TYPE, 36)
        val sessionId = ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN).getLong(12)
        if (sessionId == 0L) throw ProtocolException("LAN_PAIR_ACK session is zero")
        val monitorCount = payload[32].toInt() and 0xff
        val selectedMonitorIndex = payload[33].toInt() and 0xff
        val udpPort = ByteBuffer.wrap(payload, 34, 2).order(ByteOrder.LITTLE_ENDIAN).short.toInt() and 0xffff
        if (monitorCount == 0 || selectedMonitorIndex >= monitorCount || udpPort == 0) {
            throw ProtocolException("Invalid LAN pair metadata")
        }
        return LanPairAck(
            sessionId,
            payload.copyOfRange(0, 32),
            monitorCount,
            selectedMonitorIndex,
            udpPort,
        )
    }

    private fun encode(
        messageType: Int,
        sessionId: Long,
        payloadBytes: Int,
        writePayload: ByteBuffer.() -> Unit,
    ): ByteArray {
        val frameBytes = ProtocolV1Codec.HEADER_BYTES + payloadBytes
        val buffer = ByteBuffer.allocate(frameBytes).order(ByteOrder.LITTLE_ENDIAN)
        buffer.putInt(MAGIC)
        buffer.put(MAJOR.toByte())
        buffer.put(MINOR.toByte())
        buffer.put(messageType.toByte())
        buffer.put(0.toByte())
        buffer.putShort(ProtocolV1Codec.HEADER_BYTES.toShort())
        buffer.putShort(frameBytes.toShort())
        buffer.putLong(sessionId)
        buffer.putInt(0)
        buffer.putLong(0)
        buffer.writePayload()
        return buffer.array()
    }

    private fun validate(frame: ByteArray, messageType: Int, payloadBytes: Int): ByteArray {
        val expectedBytes = ProtocolV1Codec.HEADER_BYTES + payloadBytes
        if (frame.size != expectedBytes) throw ProtocolException("Control frame length mismatch")
        val buffer = ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN)
        if (buffer.int != MAGIC) throw ProtocolException("Bad protocol magic")
        val major = buffer.get().toInt() and 0xff
        val minor = buffer.get().toInt() and 0xff
        if (major != MAJOR || minor != MINOR) throw ProtocolException("Unsupported protocol version")
        if (buffer.get().toInt() and 0xff != messageType) throw ProtocolException("Unexpected control type")
        if (buffer.get().toInt() != 0) throw ProtocolException("Unknown control flags")
        if (buffer.short.toInt() and 0xffff != ProtocolV1Codec.HEADER_BYTES) {
            throw ProtocolException("Unexpected control header size")
        }
        if (buffer.short.toInt() and 0xffff != frame.size) {
            throw ProtocolException("Control header length mismatch")
        }
        return frame.copyOfRange(ProtocolV1Codec.HEADER_BYTES, frame.size)
    }
}
