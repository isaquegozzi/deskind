package com.deskink.android.transport

import org.junit.Assert.assertArrayEquals
import org.junit.Assert.assertEquals
import org.junit.Assert.assertThrows
import org.junit.Test

class InMemoryInputTransportTest {
    @Test
    fun capturesDefensiveCopiesAndMetrics() {
        val transport = InMemoryInputTransport()
        transport.connect()
        val input = byteArrayOf(1, 2, 3)
        transport.sendInput(input)
        input[0] = 9
        transport.sendControl(byteArrayOf(4, 5))

        assertArrayEquals(byteArrayOf(1, 2, 3), transport.capturedInputFrames.single())
        assertEquals(1, transport.metrics.inputFramesSent)
        assertEquals(1, transport.metrics.controlFramesSent)
        assertEquals(5, transport.metrics.bytesSent)
    }

    @Test
    fun rejectsSendWhileDisconnected() {
        val transport = InMemoryInputTransport()
        assertThrows(IllegalStateException::class.java) {
            transport.sendInput(byteArrayOf(1))
        }
    }
}
