package com.deskink.android.input

enum class CapabilityState {
    UNKNOWN,
    SUPPORTED,
    UNSUPPORTED,
}

data class AxisCapability(
    val state: CapabilityState,
    val minimum: Float? = null,
    val maximum: Float? = null,
    val resolution: Float? = null,
    val fuzz: Float? = null,
)

data class StylusCapabilities(
    val deviceId: Int = -1,
    val descriptor: String = "unknown",
    val deviceName: String = "No stylus observed",
    val pressure: AxisCapability = AxisCapability(CapabilityState.UNKNOWN),
    val tilt: AxisCapability = AxisCapability(CapabilityState.UNKNOWN),
    val orientation: AxisCapability = AxisCapability(CapabilityState.UNKNOWN),
    val distance: AxisCapability = AxisCapability(CapabilityState.UNKNOWN),
    val hover: CapabilityState = CapabilityState.UNKNOWN,
    val sideButton: CapabilityState = CapabilityState.UNKNOWN,
    val eraser: CapabilityState = CapabilityState.UNKNOWN,
)
