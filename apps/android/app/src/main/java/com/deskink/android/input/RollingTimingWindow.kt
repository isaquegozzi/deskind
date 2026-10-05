package com.deskink.android.input

data class TimingPercentiles(
    val p50Us: Long,
    val p95Us: Long,
    val p99Us: Long,
)

internal class RollingTimingWindow(capacity: Int) {
    private val values = LongArray(capacity)
    private var count = 0
    private var next = 0

    @Synchronized
    fun record(durationNs: Long) {
        values[next] = (durationNs / 1_000L).coerceAtLeast(0)
        next = (next + 1) % values.size
        count = minOf(count + 1, values.size)
    }

    @Synchronized
    fun percentiles(): TimingPercentiles? {
        if (count == 0) return null
        val snapshot = values.copyOf(count).apply { sort() }
        fun at(percentile: Double): Long {
            val index = (kotlin.math.ceil(percentile * snapshot.size).toInt() - 1)
                .coerceIn(0, snapshot.lastIndex)
            return snapshot[index]
        }
        return TimingPercentiles(at(0.50), at(0.95), at(0.99))
    }
}
