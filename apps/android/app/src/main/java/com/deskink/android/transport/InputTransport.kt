package com.deskink.android.transport

enum class TransportState {
    DISCONNECTED,
    CONNECTING,
    CONNECTED,
    ERROR,
}

data class TransportMetrics(
    val inputFramesSent: Long = 0,
    val controlFramesSent: Long = 0,
    val bytesSent: Long = 0,
    val controlRttUs: Long? = null,
    val clockUncertaintyUs: Long? = null,
)

interface InputTransport {
    val state: TransportState
    val metrics: TransportMetrics
    val lastError: String?
    val sessionId: Long
    val monitorCount: Int
    val selectedMonitorIndex: Int

    fun connect()
    fun disconnect()
    fun sendInput(frame: ByteArray)
    fun sendControl(frame: ByteArray)
}
