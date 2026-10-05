package com.deskink.android.ui

import org.junit.Assert.assertFalse
import org.junit.Assert.assertTrue
import org.junit.Test

class TwoFingerTapDetectorTest {
    @Test
    fun acceptsQuickStationaryGesture() {
        val detector = TwoFingerTapDetector(maximumDurationMs = 350, maximumMovementPx = 20f)

        detector.begin(eventTimeMs = 1_000, centroidX = 100f, centroidY = 200f)

        assertTrue(detector.end(eventTimeMs = 1_180, centroidX = 108f, centroidY = 204f))
        assertFalse(detector.end(eventTimeMs = 1_200, centroidX = 108f, centroidY = 204f))
    }

    @Test
    fun rejectsLongOrMovingGesture() {
        val detector = TwoFingerTapDetector(maximumDurationMs = 350, maximumMovementPx = 20f)

        detector.begin(eventTimeMs = 1_000, centroidX = 100f, centroidY = 200f)
        assertFalse(detector.end(eventTimeMs = 1_351, centroidX = 100f, centroidY = 200f))

        detector.begin(eventTimeMs = 2_000, centroidX = 100f, centroidY = 200f)
        detector.move(130f, 200f)
        assertFalse(detector.end(eventTimeMs = 2_100, centroidX = 100f, centroidY = 200f))
    }

    @Test
    fun cancellationDisarmsGesture() {
        val detector = TwoFingerTapDetector(maximumDurationMs = 350, maximumMovementPx = 20f)

        detector.begin(eventTimeMs = 1_000, centroidX = 100f, centroidY = 200f)
        detector.cancel()

        assertFalse(detector.end(eventTimeMs = 1_100, centroidX = 100f, centroidY = 200f))
    }
}
