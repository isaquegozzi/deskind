package com.deskink.android.input

import org.junit.Assert.assertEquals
import org.junit.Test

class StylusPacketPublisherTest {
    @Test
    fun coordinateQuantizationClampsToActiveSurface() {
        assertEquals(0, StylusPacketPublisher.normalizeCoordinate(-5f, 100))
        assertEquals(0, StylusPacketPublisher.normalizeCoordinate(0f, 100))
        assertEquals(65535, StylusPacketPublisher.normalizeCoordinate(99f, 100))
        assertEquals(65535, StylusPacketPublisher.normalizeCoordinate(150f, 100))
    }

    @Test
    fun axisQuantizationUsesDeclaredRange() {
        val capability = AxisCapability(CapabilityState.SUPPORTED, minimum = 0f, maximum = 2f)
        assertEquals(0, StylusPacketPublisher.normalizeAxis(0f, capability))
        assertEquals(32768, StylusPacketPublisher.normalizeAxis(1f, capability))
        assertEquals(65535, StylusPacketPublisher.normalizeAxis(2f, capability))
        assertEquals(
            0,
            StylusPacketPublisher.normalizeAxis(
                1f,
                AxisCapability(CapabilityState.UNKNOWN, minimum = 0f, maximum = 2f),
            ),
        )
    }

    @Test
    fun androidAnglesBecomeProtocolCentidegrees() {
        assertEquals(9000, StylusPacketPublisher.radiansToCentidegrees(Math.PI.toFloat() / 2, 0, 9000))
        assertEquals(-9000, StylusPacketPublisher.radiansToCentidegrees(-Math.PI.toFloat() / 2, -18000, 18000))
    }
}
