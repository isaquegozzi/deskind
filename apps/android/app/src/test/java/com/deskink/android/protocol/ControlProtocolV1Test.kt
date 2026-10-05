package com.deskink.android.protocol

import java.nio.ByteBuffer
import java.nio.ByteOrder
import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class ControlProtocolV1Test {
    @Test
    fun helloAndInputBindUseExpectedHeaderAndPayload() {
        val hello = ControlProtocolV1.encodeHello(0x1122334455667788L)
        val buffer = ByteBuffer.wrap(hello).order(ByteOrder.LITTLE_ENDIAN)
        assertEquals(0x494B5344, buffer.int)
        assertEquals(ControlProtocolV1.HELLO_TYPE, hello[6].toInt() and 0xff)
        assertEquals(0x1122334455667788L, buffer.getLong(ProtocolV1Codec.HEADER_BYTES))

        val token = ByteArray(ControlProtocolV1.BIND_TOKEN_BYTES) { it.toByte() }
        val bind = ControlProtocolV1.encodeInputBind(7L, token)
        assertEquals(ControlProtocolV1.INPUT_BIND_TYPE, bind[6].toInt() and 0xff)
        assertEquals(7L, ByteBuffer.wrap(bind).order(ByteOrder.LITTLE_ENDIAN).getLong(12))
        assertArrayEquals(token, bind.copyOfRange(ProtocolV1Codec.HEADER_BYTES, bind.size))
    }

    @Test
    fun helloAckDecodesAndRejectsBadTokenLength() {
        val token = ByteArray(ControlProtocolV1.BIND_TOKEN_BYTES) { (0xa0 + it).toByte() }
        val ack = controlFrame(
            ControlProtocolV1.HELLO_ACK_TYPE,
            9L,
            token + byteArrayOf(2, 1),
        )
        val decoded = ControlProtocolV1.decodeHelloAck(ack)
        assertEquals(9L, decoded.sessionId)
        assertArrayEquals(token, decoded.inputBindToken)
        assertEquals(2, decoded.monitorCount)
        assertEquals(1, decoded.selectedMonitorIndex)

        assertThrows(ProtocolException::class.java) {
            ControlProtocolV1.encodeInputBind(9L, token.copyOf(15))
        }
    }

    @Test
    fun persistentPenScrollModeUsesSessionControlFrame() {
        val enabled = ControlProtocolV1.encodeSetMouseMode(11L, penScrollEnabled = true)
        val disabled = ControlProtocolV1.encodeSetMouseMode(11L, penScrollEnabled = false)
        assertEquals(ControlProtocolV1.SET_MOUSE_MODE_TYPE, enabled[6].toInt() and 0xff)
        assertEquals(11L, ByteBuffer.wrap(enabled).order(ByteOrder.LITTLE_ENDIAN).getLong(12))
        assertEquals(1, enabled[ProtocolV1Codec.HEADER_BYTES].toInt())
        assertEquals(0, disabled[ProtocolV1Codec.HEADER_BYTES].toInt())
    }

    @Test
    fun clockSyncFramesRoundTrip() {
        val ping = ControlProtocolV1.encodePing(13L, 1_000L)
        assertEquals(ControlProtocolV1.PING_TYPE, ping[6].toInt() and 0xff)
        val payload = ByteBuffer.allocate(24).order(ByteOrder.LITTLE_ENDIAN)
            .putLong(1_000L)
            .putLong(1_100L)
            .putLong(1_105L)
            .array()
        val pong = ControlProtocolV1.decodePong(controlFrame(ControlProtocolV1.PONG_TYPE, 13L, payload))
        assertEquals(13L, pong.sessionId)
        assertEquals(1_000L, pong.clientTimeUs)
        assertEquals(1_100L, pong.hostReceiveTimeUs)
        assertEquals(1_105L, pong.hostSendTimeUs)
        val sync = ControlProtocolV1.encodeSetClockSync(13L, 55L, 7L)
        assertEquals(ControlProtocolV1.SET_CLOCK_SYNC_TYPE, sync[6].toInt() and 0xff)
    }

    @Test
    fun monitorSelectionUsesSessionControlFrame() {
        val frame = ControlProtocolV1.encodeSetMonitor(11L, 1)
        assertEquals(ControlProtocolV1.SET_MONITOR_TYPE, frame[6].toInt() and 0xff)
        assertEquals(11L, ByteBuffer.wrap(frame).order(ByteOrder.LITTLE_ENDIAN).getLong(12))
        assertEquals(1, frame[ProtocolV1Codec.HEADER_BYTES].toInt())
    }

    @Test
    fun overlayControlsUseSessionFrames() {
        val tool = ControlProtocolV1.encodeSetOverlayTool(12L, 1)
        val command = ControlProtocolV1.encodeOverlayCommand(12L, 2)
        val mode = ControlProtocolV1.encodeSetOverlayMode(12L, 1)
        val output = ControlProtocolV1.encodeSetOutputMode(12L, 2)
        assertEquals(ControlProtocolV1.SET_OVERLAY_TOOL_TYPE, tool[6].toInt() and 0xff)
        assertEquals(ControlProtocolV1.OVERLAY_COMMAND_TYPE, command[6].toInt() and 0xff)
        assertEquals(ControlProtocolV1.SET_OVERLAY_MODE_TYPE, mode[6].toInt() and 0xff)
        assertEquals(ControlProtocolV1.SET_OUTPUT_MODE_TYPE, output[6].toInt() and 0xff)
    }

    @Test
    fun shortcutUsesClosedCommandSet() {
        val shortcut = ControlProtocolV1.encodeExecuteShortcut(12L, 2)
        assertEquals(ControlProtocolV1.EXECUTE_SHORTCUT_TYPE, shortcut[6].toInt() and 0xff)
        assertEquals(2, shortcut[ProtocolV1Codec.HEADER_BYTES].toInt())
        assertThrows(ProtocolException::class.java) {
            ControlProtocolV1.encodeExecuteShortcut(12L, 8)
        }
    }

    private fun controlFrame(type: Int, sessionId: Long, payload: ByteArray): ByteArray {
        val size = ProtocolV1Codec.HEADER_BYTES + payload.size
        return ByteBuffer.allocate(size).order(ByteOrder.LITTLE_ENDIAN).apply {
            putInt(0x494B5344)
            put(1)
            put(0)
            put(type.toByte())
            put(0)
            putShort(ProtocolV1Codec.HEADER_BYTES.toShort())
            putShort(size.toShort())
            putLong(sessionId)
            putInt(0)
            putLong(0)
            put(payload)
        }.array()
    }
}
