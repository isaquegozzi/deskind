package com.deskink.android.protocol

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class ProtocolV1CodecTest {
    @Test
    fun androidEncoderMatchesGoldenVector() {
        assertArrayEquals(loadGoldenVector(), ProtocolV1Codec.encodeInputBatch(goldenFrame()))
    }

    @Test
    fun goldenVectorRoundTrips() {
        val decoded = ProtocolV1Codec.decodeInputBatch(loadGoldenVector())
        assertEquals(0x0102030405060708L, decoded.sessionId)
        assertEquals(42L, decoded.sequence)
        assertEquals(1_000_000L, decoded.baseMonotonicTimeUs)
        assertEquals(ToolKind.STYLUS, decoded.toolKind)
        assertEquals(7, decoded.pointerId)
        assertEquals(goldenFrame().samples.single(), decoded.samples.single())
    }

    @Test
    fun malformedMagicIsRejected() {
        val malformed = loadGoldenVector().also { it[0] = 0 }
        assertThrows(ProtocolException::class.java) {
            ProtocolV1Codec.decodeInputBatch(malformed)
        }
    }

    @Test
    fun contactWithoutRangeIsRejected() {
        val badSample = goldenFrame().samples.single().copy(
            stateFlags = SampleStateFlags.CONTACT,
        )
        assertThrows(ProtocolException::class.java) {
            ProtocolV1Codec.encodeInputBatch(goldenFrame().copy(samples = listOf(badSample)))
        }
    }

    private fun goldenFrame() = InputBatchFrame(
        sessionId = 0x0102030405060708L,
        sequence = 42,
        baseMonotonicTimeUs = 1_000_000,
        toolKind = ToolKind.STYLUS,
        pointerId = 7,
        samples = listOf(
            PenSample(
                deltaTimeUs = 250,
                stateGeneration = 9,
                xNormalized = 32768,
                yNormalized = 16384,
                pressureNormalized = 49151,
                distanceNormalized = 1234,
                tiltCentidegrees = 567,
                orientationCentidegrees = -9000,
                buttons = 32,
                stateFlags = 0x003f,
            ),
        ),
    )

    private fun loadGoldenVector(): ByteArray {
        val text = checkNotNull(javaClass.classLoader?.getResourceAsStream("input-batch-v1.hex"))
            .bufferedReader()
            .use { it.readText() }
            .filterNot(Char::isWhitespace)
        return text.chunked(2).map { it.toInt(16).toByte() }.toByteArray()
    }
}
