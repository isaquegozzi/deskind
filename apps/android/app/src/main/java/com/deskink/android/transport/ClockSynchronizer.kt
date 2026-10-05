package com.deskink.android.transport

import com.deskink.android.protocol.ControlProtocolV1
import java.io.InputStream
import java.io.OutputStream

data class ClockSyncResult(
    val hostMinusClientUs: Long,
    val roundTripUs: Long,
    val uncertaintyUs: Long,
)

object ClockSynchronizer {
    private const val SAMPLE_COUNT = 8
    private const val MAXIMUM_CONTROL_FRAME_BYTES = 65_535

    fun calibrate(input: InputStream, output: OutputStream, sessionId: Long): ClockSyncResult {
        var best: ClockSyncResult? = null
        repeat(SAMPLE_COUNT) {
            val clientSendUs = monotonicTimeUs()
            TcpFraming.write(output, ControlProtocolV1.encodePing(sessionId, clientSendUs))
            val pong = ControlProtocolV1.decodePong(
                TcpFraming.read(input, MAXIMUM_CONTROL_FRAME_BYTES),
            )
            val clientReceiveUs = monotonicTimeUs()
            check(pong.sessionId == sessionId && pong.clientTimeUs == clientSendUs) {
                "Clock sync response does not match request"
            }
            val hostProcessingUs = (pong.hostSendTimeUs - pong.hostReceiveTimeUs).coerceAtLeast(0)
            val roundTripUs = (clientReceiveUs - clientSendUs - hostProcessingUs).coerceAtLeast(0)
            val offsetUs = ((pong.hostReceiveTimeUs - clientSendUs) +
                (pong.hostSendTimeUs - clientReceiveUs)) / 2
            val candidate = ClockSyncResult(offsetUs, roundTripUs, (roundTripUs + 1) / 2)
            if (best == null || candidate.roundTripUs < best!!.roundTripUs) best = candidate
        }
        val result = checkNotNull(best)
        TcpFraming.write(
            output,
            ControlProtocolV1.encodeSetClockSync(
                sessionId,
                result.hostMinusClientUs,
                result.uncertaintyUs,
            ),
        )
        return result
    }

    private fun monotonicTimeUs(): Long = System.nanoTime() / 1_000L
}
