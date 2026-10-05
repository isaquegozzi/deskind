package com.deskink.android.ui

import kotlin.math.hypot

internal class TwoFingerTapDetector(
    private val maximumDurationMs: Long,
    private val maximumMovementPx: Float,
) {
    private var armed = false
    private var startedAtMs = 0L
    private var startX = 0f
    private var startY = 0f
    private var maximumObservedMovement = 0f

    fun begin(eventTimeMs: Long, centroidX: Float, centroidY: Float) {
        armed = true
        startedAtMs = eventTimeMs
        startX = centroidX
        startY = centroidY
        maximumObservedMovement = 0f
    }

    fun move(centroidX: Float, centroidY: Float) {
        if (!armed) return
        maximumObservedMovement = maxOf(
            maximumObservedMovement,
            hypot(centroidX - startX, centroidY - startY),
        )
    }

    fun end(eventTimeMs: Long, centroidX: Float, centroidY: Float): Boolean {
        if (!armed) return false
        move(centroidX, centroidY)
        val recognized = eventTimeMs - startedAtMs <= maximumDurationMs &&
            maximumObservedMovement <= maximumMovementPx
        cancel()
        return recognized
    }

    fun cancel() {
        armed = false
    }
}
