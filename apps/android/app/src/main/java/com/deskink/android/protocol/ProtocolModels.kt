package com.deskink.android.protocol

enum class ToolKind(val wireValue: Int) {
    UNKNOWN(0),
    FINGER(1),
    STYLUS(2),
    ERASER(3),
    MOUSE(4),
    ;

    companion object {
        fun fromWire(value: Int): ToolKind = entries.firstOrNull { it.wireValue == value }
            ?: throw ProtocolException("Unknown tool kind: $value")
    }
}

object SampleStateFlags {
    const val IN_RANGE = 0x0001
    const val CONTACT = 0x0002
    const val PRESSURE_VALID = 0x0004
    const val DISTANCE_VALID = 0x0008
    const val TILT_VALID = 0x0010
    const val ORIENTATION_VALID = 0x0020
    const val HISTORICAL = 0x0040
    const val CANCELED = 0x0080
    const val KNOWN_MASK = 0x00ff
}

data class PenSample(
    val deltaTimeUs: Long,
    val stateGeneration: Long,
    val xNormalized: Int,
    val yNormalized: Int,
    val pressureNormalized: Int,
    val distanceNormalized: Int,
    val tiltCentidegrees: Int,
    val orientationCentidegrees: Int,
    val buttons: Int,
    val stateFlags: Int,
)

data class InputBatchFrame(
    val sessionId: Long,
    val sequence: Long,
    val baseMonotonicTimeUs: Long,
    val toolKind: ToolKind,
    val pointerId: Int,
    val samples: List<PenSample>,
)

class ProtocolException(message: String) : IllegalArgumentException(message)
