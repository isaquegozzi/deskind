package com.deskink.android.input

import org.junit.Assert.assertEquals
import org.junit.Test

class RollingTimingWindowTest {
    @Test
    fun reportsNearestRankPercentilesAndKeepsBoundedWindow() {
        val window = RollingTimingWindow(capacity = 4)
        listOf(1L, 2L, 3L, 4L, 100L).forEach { microseconds ->
            window.record(microseconds * 1_000L)
        }

        assertEquals(TimingPercentiles(p50Us = 3, p95Us = 100, p99Us = 100), window.percentiles())
    }
}
