package com.deskink.android.input

data class StylusTelemetrySnapshot(
    val sampleCount: Long,
    val eventCount: Long,
    val historicalSampleCount: Long,
    val cancelCount: Long,
    val pointerId: Int,
    val toolType: Int,
    val action: Int,
    val x: Float,
    val y: Float,
    val pressure: Float,
    val tiltRadians: Float,
    val orientationRadians: Float,
    val distance: Float,
    val buttonState: Int,
    val actionButton: Int,
    val eventTimeNanos: Long,
    val isHistorical: Boolean,
    val isCanceledByPalm: Boolean,
    val capabilities: StylusCapabilities,
)
